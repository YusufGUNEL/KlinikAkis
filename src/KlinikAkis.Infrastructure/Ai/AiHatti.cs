using System.ClientModel;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using KlinikAkis.Domain;
using KlinikAkis.Domain.Varliklar;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace KlinikAkis.Infrastructure.Ai;

/// <param name="Model">Kart başlığındaki model adı; yedek ve kuralda null.</param>
/// <param name="KotaDoldu">Günlük kota / sağlayıcı 429: model çağrılmadı, taslak kural tabanlı; kartta rozet çıkar.</param>
public sealed record AiSonucu(
    AiKaynak Kaynak, OnAnamnezTaslagi? Taslak, KirmiziBayrak? KirmiziBayrak, string? Model = null, bool KotaDoldu = false);

/// <summary>Tahlil açıklaması taslağı (SPEC §9.6): LLM ya da kural tabanlı yedek; model adı yalnızca LLM'de.</summary>
public sealed record TahlilAiSonucu(AiKaynak Kaynak, string Aciklama, string? Model = null, bool KotaDoldu = false);

/// <summary>Epikriz taslağı: modelin (ya da yedeğin) cümleleri + kodda kurulan alerji/tahlil satırları.</summary>
public sealed record EpikrizAiSonucu(AiKaynak Kaynak, EpikrizTaslagi Taslak, string? Model = null, bool KotaDoldu = false);

/// <summary>
/// Her LLM çağrısının geçtiği tek yol (SPEC §9.1): maskeleme → kural kontrolü → kota → IChatClient (JSON şema,
/// zaman aşımı) → sunucu doğrulaması → (hata/geçersiz/zaman aşımı) kural tabanlı yedek → denetim kaydı.
/// Kural tetiklenirse model hiç çağrılmaz. Her dal AiAuditLog ekler; kaydetmeyi çağıran yapar (tek SaveChanges:
/// ön anamnez durumu ile denetim kaydı birlikte yazılır, biri kalıp öbürü kaybolmaz).
/// </summary>
public sealed class AiHatti(
    IChatClient istemci, GunlukKota kota, KlinikDbContext db, TimeProvider zaman, AiAyarlari ayar)
{
    public const string OnAnamnezIstemSurumu = "onanamnez.v1";
    public const string TahlilIstemSurumu = "tahlil.v1";
    public const string EpikrizIstemSurumu = "epikriz.v1";
    public const string KotaNedeni = "kota";
    private const int OnAnamnezMaxToken = 1500;
    private const int TahlilMaxToken = 400;
    // Daha güçlü model düşünme belirteçleri de harcayabilir; üç kısa alan için pay bırakılır.
    private const int EpikrizMaxToken = 2000;

    private static readonly string OnAnamnezIstemSablonu = IstemOku(OnAnamnezIstemSurumu);
    private static readonly string TahlilIstemi = IstemOku(TahlilIstemSurumu);
    private static readonly string EpikrizIstemi = IstemOku(EpikrizIstemSurumu);

    /// <summary>Bir model çağrısının denetim kaydına giden ortak bilgisi.</summary>
    private sealed record Cagri(
        AiOzellik Ozellik, string IstemSurumu, Guid HastaId, string Maskeli, string Saglayici, string Model, Stopwatch Olcum);

    /// <summary>Model çağrısının sonucu: yanıt ya da (yedeğe düşme) nedeni.</summary>
    private sealed record CagriSonucu(ChatResponse? Yanit, string? Neden, bool KotaDoldu);

    /// <summary>Sohbet adımı sırasında: yalnızca kural kontrolü (LLM yok). Tetiklenirse denetim kaydı ekler.</summary>
    public KirmiziBayrak? KuralKontrolu(Hasta hasta, IReadOnlyList<SohbetKaydi> sohbet)
    {
        var bayrak = Degerlendir(hasta, sohbet);
        if (bayrak is not null)
            KuralDenetimi(hasta, MaskeliGirdi(hasta, sohbet), bayrak);
        return bayrak;
    }

    /// <summary>Sohbet bitti: kural → (temizse) model. Kural ham metinde çalışır, modele yalnızca maskeli metin gider.</summary>
    public async Task<AiSonucu> OnAnamnezOzetiAsync(Hasta hasta, IReadOnlyList<SohbetKaydi> sohbet, CancellationToken ct)
    {
        var maskeli = MaskeliGirdi(hasta, sohbet);
        if (Degerlendir(hasta, sohbet) is { } bayrak)
        {
            KuralDenetimi(hasta, maskeli, bayrak);
            return new AiSonucu(AiKaynak.Kural, null, bayrak);
        }

        var branslar = await db.Hekimler.AsNoTracking().Select(h => new { h.Brans!.Kod, h.Brans.Ad }).Distinct().ToListAsync(ct);
        var hekimli = branslar.Select(b => b.Kod).ToHashSet();
        var sistem = OnAnamnezIstemSablonu.Replace("{{BRANSLAR}}",
            string.Join('\n', branslar.OrderBy(b => b.Kod.ToString(), StringComparer.Ordinal).Select(b => $"- {b.Kod}: {b.Ad}")),
            StringComparison.Ordinal);
        List<ChatMessage> mesajlar = [new(ChatRole.System, sistem), new(ChatRole.User, maskeli)];
        var secenekler = new ChatOptions
        {
            MaxOutputTokens = OnAnamnezMaxToken,
            Temperature = 0.2f,
            ResponseFormat = OnAnamnezTaslagi.YanitBicimi,
        };

        var cagri = YeniCagri(AiOzellik.OnAnamnez, OnAnamnezIstemSurumu, hasta.Id, maskeli);
        var sonuc = await CagirAsync(cagri, mesajlar, secenekler, ct);
        if (sonuc.Yanit is not { } yanit)
            return Yedek(cagri, hasta, sohbet, hekimli, null, sonuc.Neden!, sonuc.KotaDoldu);

        cagri = cagri with { Model = yanit.ModelId ?? cagri.Model };
        var dogrulama = OnAnamnezDogrulayici.Dogrula(yanit.Text, sohbet.Count, hekimli);
        if (dogrulama.Taslak is { } aday)
            dogrulama = OnAnamnezDogrulayici.KodKurallari(aday, sohbet,
                IstanbulSaati.Yas(hasta.DogumTarihi, zaman.GetUtcNow().UtcDateTime), hekimli);
        if (dogrulama.Taslak is null)
            return Yedek(cagri, hasta, sohbet, hekimli, yanit.Text, dogrulama.Neden!, kotaDoldu: false);

        DenetimEkle(cagri, dogrulama.Taslak.JsonOlarak(), basarili: true, yedek: false, null);
        return new AiSonucu(AiKaynak.Llm, dogrulama.Taslak, null, cagri.Model);
    }

    /// <summary>
    /// Tahlil açıklaması (SPEC §9.6): işaret koddan gelir, model yalnızca cümle kurar. Modele kimlik gitmez; yanıt
    /// 600 karakteri aşar ya da işaretle çelişirse işarete göre şablon cümleye (kural tabanlı yedek) düşülür.
    /// </summary>
    public async Task<TahlilAiSonucu> TahlilAciklamasiAsync(Guid hastaId, TahlilGirdisi girdi, CancellationToken ct)
    {
        var maskeli = girdi.MaskeliMetin();
        List<ChatMessage> mesajlar = [new(ChatRole.System, TahlilIstemi), new(ChatRole.User, maskeli)];
        var secenekler = new ChatOptions
        {
            MaxOutputTokens = TahlilMaxToken,
            Temperature = 0.2f,
            ResponseFormat = TahlilAciklamaYaniti.YanitBicimi,
        };

        var cagri = YeniCagri(AiOzellik.TahlilAciklama, TahlilIstemSurumu, hastaId, maskeli);
        var sonuc = await CagirAsync(cagri, mesajlar, secenekler, ct);
        string? cikti = null;
        var neden = sonuc.Neden;
        if (sonuc.Yanit is { } yanit)
        {
            cagri = cagri with { Model = yanit.ModelId ?? cagri.Model };
            cikti = yanit.Text;
            (var aciklama, neden) = TahlilAciklamaDogrulayici.Dogrula(cikti, girdi);
            if (aciklama is not null)
            {
                DenetimEkle(cagri, new TahlilAciklamaYaniti(aciklama).JsonOlarak(), basarili: true, yedek: false, null);
                return new TahlilAiSonucu(AiKaynak.Llm, aciklama, cagri.Model);
            }
        }

        DenetimEkle(cagri, cikti, basarili: false, yedek: true, neden);
        return new TahlilAiSonucu(AiKaynak.Yedek, TahlilYedekSablonu.Olustur(girdi), null, sonuc.KotaDoldu);
    }

    /// <summary>
    /// Epikriz taslağı (SPEC §13.7a): serbest metinler maskelenir, model yalnızca öykü/değerlendirme/plan cümlelerini
    /// yazar; alerji ve tahlil satırları (işaretiyle) kodda kurulur. Model <c>AI:EpikrizModel</c> verildiyse o (istek
    /// başına <c>ChatOptions.ModelId</c>), yoksa <c>AI:Model</c>. Yedek, kayıttaki maskesiz onaylı metinlerden kurulur.
    /// </summary>
    public async Task<EpikrizAiSonucu> EpikrizAsync(Hasta hasta, EpikrizGirdisi girdi, CancellationToken ct)
    {
        var maskeli = girdi.Maskele(hasta.Ad, hasta.Soyad).MaskeliMetin();
        List<ChatMessage> mesajlar = [new(ChatRole.System, EpikrizIstemi), new(ChatRole.User, maskeli)];
        var secenekler = new ChatOptions
        {
            MaxOutputTokens = EpikrizMaxToken,
            Temperature = 0.2f,
            ResponseFormat = EpikrizYaniti.YanitBicimi,
            ModelId = ayar.EpikrizModel,
            // Üç kısa alan için düşük düşünme yeter; 15 sn bütçe korunur. OpenAI uyumlu uçta reasoning_effort →
            // Gemini thinking_level (ai.google.dev/gemini-api/docs/openai).
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Low },
        };

        var cagri = YeniCagri(AiOzellik.Epikriz, EpikrizIstemSurumu, hasta.Id, maskeli);
        if (ayar.EpikrizModel is { } epikrizModeli)
            cagri = cagri with { Model = epikrizModeli };
        var sonuc = await CagirAsync(cagri, mesajlar, secenekler, ct);
        string? cikti = null;
        var neden = sonuc.Neden;
        if (sonuc.Yanit is { } yanit)
        {
            cagri = cagri with { Model = yanit.ModelId ?? cagri.Model };
            cikti = yanit.Text;
            (var gecerli, neden) = EpikrizDogrulayici.Dogrula(cikti, maskeli);
            if (gecerli is not null)
            {
                DenetimEkle(cagri, gecerli.JsonOlarak(), basarili: true, yedek: false, null);
                return new EpikrizAiSonucu(AiKaynak.Llm, EpikrizTaslagi.Olustur(gecerli, girdi), cagri.Model);
            }
        }

        DenetimEkle(cagri, cikti, basarili: false, yedek: true, neden);
        return new EpikrizAiSonucu(AiKaynak.Yedek, EpikrizTaslagi.Olustur(EpikrizYedekSablonu.Olustur(girdi), girdi),
            null, sonuc.KotaDoldu);
    }

    private Cagri YeniCagri(AiOzellik ozellik, string istemSurumu, Guid hastaId, string maskeli) =>
        new(ozellik, istemSurumu, hastaId, maskeli,
            istemci.GetService<FakeChatClient>() is not null ? FakeChatClient.Saglayici : ayar.Saglayici,
            istemci.GetService<ChatClientMetadata>()?.DefaultModelId ?? ayar.Model,
            Stopwatch.StartNew());

    /// <summary>
    /// Kota → model (zaman aşımıyla). Kota dolunca model çağrılmaz (SPEC §9.9); hazır örnek metin kullanılmaz, taslak
    /// kural tabanlı üretilir. Kota yalnızca gerçek sağlayıcıyı sayar; Fake modda rozet çıkmaz.
    /// </summary>
    private async Task<CagriSonucu> CagirAsync(Cagri cagri, List<ChatMessage> mesajlar, ChatOptions secenekler, CancellationToken ct)
    {
        if (ayar.Saglayici != FakeChatClient.Saglayici && !kota.Ayir())
            return new(null, KotaNedeni, true);

        using var sure = CancellationTokenSource.CreateLinkedTokenSource(ct);
        sure.CancelAfter(ayar.ZamanAsimi);
        try
        {
            return new(await istemci.GetResponseAsync(mesajlar, secenekler, sure.Token), null, false);
        }
        catch (Exception e) when (SaglayiciKotasi(e))
        {
            // Ücretsiz katman doldu (429): günün kalanında sağlayıcı çağrılmaz.
            kota.SaglayiciDoldu();
            return new(null, Birlestir(KotaNedeni, "sağlayıcı 429"), true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Kullanıcı sayfadan ayrıldı: maskeli metin sağlayıcıya gitmiş olabilir, iz kalsın (istek iptal olsa da).
            DenetimEkle(cagri, null, basarili: false, yedek: false, "istemci iptal etti");
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception e)
        {
            return new(null, e is OperationCanceledException ? "zaman aşımı" : "sağlayıcı hatası", false);
        }
    }

    /// <summary>Yedek dalı: modelin ham çıktısı (varsa) denetime yazılır, taslak kural tabanlı üretilir.</summary>
    private AiSonucu Yedek(Cagri cagri, Hasta hasta, IReadOnlyList<SohbetKaydi> sohbet, IReadOnlySet<BransKodu> hekimli,
        string? cikti, string neden, bool kotaDoldu)
    {
        DenetimEkle(cagri, cikti, basarili: false, yedek: true, neden);
        var yas = IstanbulSaati.Yas(hasta.DogumTarihi, zaman.GetUtcNow().UtcDateTime);
        return new AiSonucu(AiKaynak.Yedek, KuralTabanliYedek.Olustur(sohbet, yas, hekimli), null, null, kotaDoldu);
    }

    /// <summary>Modele gidecek metnin tamamı; denetim kaydına ve "modele giden veri" paneline aynen yazılır.</summary>
    public string MaskeliGirdi(Hasta hasta, IReadOnlyList<SohbetKaydi> sohbet)
    {
        var yas = IstanbulSaati.Yas(hasta.DogumTarihi, zaman.GetUtcNow().UtcDateTime);
        var sb = new StringBuilder()
            .Append("hasta: ").Append(hasta.TakmaAd).Append(" · yaş: ").Append(YasAraligi(yas)).Append(" · cinsiyet: ").Append(hasta.Cinsiyet == Cinsiyet.K ? "kadın" : "erkek")
            // Aralık 18'i ayırmaz; çocuk olup olmadığını kod hesaplar, model tahmin etmez.
            .Append(" · çocuk: ").Append(yas < KuralTabanliYedek.CocukYasSiniri ? "evet" : "hayır")
            .Append('\n').Append("<hasta_metni>").Append('\n');
        foreach (var k in sohbet)
            sb.Append(k.Adim).Append(". ").Append(k.Soru).Append(" → ")
                .Append(VeriOlarak(Maskeleyici.Maskele(k.Cevap, hasta.Ad, hasta.Soyad))).Append('\n');
        return sb.Append("</hasta_metni>").ToString();
    }

    /// <summary>
    /// İstem enjeksiyonu (SPEC §9.8): hasta "&lt;/hasta_metni&gt;" yazıp bloğu kapatamasın, satır sonuyla sahte
    /// "7. ... →" adımı üretemesin. Açılı ayraçlar benzer işaretlere, satır sonları boşluğa çevrilir.
    /// </summary>
    public static string VeriOlarak(string cevap) =>
        string.Create(cevap.Length, cevap, static (hedef, kaynak) =>
        {
            for (var i = 0; i < kaynak.Length; i++)
                hedef[i] = kaynak[i] switch
                {
                    '<' or '＜' => '‹',
                    '>' or '＞' => '›',
                    // Tüm kontrol karakterleri (\r \n \v \f, U+0085) ve Unicode satır/paragraf ayırıcıları.
                    var c when char.IsControl(c) || c is '\u2028' or '\u2029' => ' ',
                    var c => c,
                };
        });

    public static string YasAraligi(int yas) => yas < 1 ? "0" : $"{yas / 10 * 10}-{yas / 10 * 10 + 9}";

    private static string Birlestir(string? once, string? sonra) =>
        string.Join(" · ", new[] { once, sonra }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>OpenAI uyumlu uçta kota aşımı HTTP 429 olarak gelir (Gemini: RESOURCE_EXHAUSTED).</summary>
    private static bool SaglayiciKotasi(Exception e) =>
        e is ClientResultException { Status: (int)HttpStatusCode.TooManyRequests }
        || e is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }
        || (e.InnerException is { } ic && SaglayiciKotasi(ic));

    private static string IstemOku(string surum)
    {
        using var akis = typeof(AiHatti).Assembly.GetManifestResourceStream($"Prompts.{surum}.md")
            ?? throw new InvalidOperationException($"İstem dosyası bulunamadı: {surum}.md");
        using var okuyucu = new StreamReader(akis, Encoding.UTF8);
        return okuyucu.ReadToEnd();
    }

    private KirmiziBayrak? Degerlendir(Hasta hasta, IReadOnlyList<SohbetKaydi> sohbet) =>
        KirmiziBayrakMotoru.Degerlendir(sohbet.Select(k => k.Cevap), YasAy(hasta.DogumTarihi));

    private int YasAy(DateOnly dogum)
    {
        var bugun = IstanbulSaati.Bugun(zaman.GetUtcNow().UtcDateTime);
        var ay = (bugun.Year - dogum.Year) * 12 + bugun.Month - dogum.Month;
        return bugun.Day < dogum.Day ? ay - 1 : ay;
    }

    private void KuralDenetimi(Hasta hasta, string maskeli, KirmiziBayrak bayrak) =>
        DenetimEkle(new Cagri(AiOzellik.OnAnamnez, OnAnamnezIstemSurumu, hasta.Id, maskeli, "Kural", "", Stopwatch.StartNew()),
            $$"""{"kirmiziBayrak":"{{bayrak.Kural}}","llmCagrildi":false}""",
            basarili: true, yedek: false, neden: bayrak.Kural.ToString());

    private void DenetimEkle(Cagri cagri, string? cikti, bool basarili, bool yedek, string? neden)
    {
        db.AiAuditLoglari.Add(new AiAuditLog
        {
            Id = Guid.NewGuid(),
            Zaman = zaman.GetUtcNow().UtcDateTime,
            Ozellik = cagri.Ozellik,
            HastaId = cagri.HastaId,
            Saglayici = cagri.Saglayici,
            Model = cagri.Model,
            IstemSurumu = cagri.IstemSurumu,
            MaskeliGirdi = cagri.Maskeli,
            // Hash maskeli metinden: ham kişisel veriden alınan hash sözlük saldırısıyla geri bulunabilir.
            GirdiHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(cagri.Maskeli))),
            CiktiJson = cikti,
            GecikmeMs = (int)cagri.Olcum.ElapsedMilliseconds,
            Basarili = basarili,
            YedegeDustu = yedek,
            Neden = neden,
        });
    }
}
