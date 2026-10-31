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
    public const string KotaNedeni = "kota";
    private const int OnAnamnezMaxToken = 1500;

    private static readonly string OnAnamnezIstemSablonu = IstemOku(OnAnamnezIstemSurumu);

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

        var saglayici = istemci.GetService<FakeChatClient>() is not null ? FakeChatClient.Saglayici : ayar.Saglayici;
        var model = istemci.GetService<ChatClientMetadata>()?.DefaultModelId ?? ayar.Model;
        var olcum = Stopwatch.StartNew();

        // Kota dolunca model çağrılmaz; taslak hastanın kendi cevaplarından (kural tabanlı) üretilir (SPEC §9.9).
        // Hazır örnek metin kullanılmaz: başka bir hastanın öyküsü yanlışlıkla onaylanıp kayda geçmesin.
        // Kota yalnızca gerçek sağlayıcı çağrılarını sayar; Fake modda rozet çıkmaz.
        if (ayar.Saglayici != FakeChatClient.Saglayici && !kota.Ayir())
            return Yedek(hasta, sohbet, hekimli, saglayici, model, maskeli, null, olcum, KotaNedeni, kotaDoldu: true);

        using var sure = CancellationTokenSource.CreateLinkedTokenSource(ct);
        sure.CancelAfter(ayar.ZamanAsimi);
        ChatResponse yanit;
        try
        {
            yanit = await istemci.GetResponseAsync(mesajlar, secenekler, sure.Token);
        }
        catch (Exception e) when (SaglayiciKotasi(e))
        {
            // Ücretsiz katman doldu (429): günün kalanında sağlayıcı çağrılmaz.
            kota.SaglayiciDoldu();
            return Yedek(hasta, sohbet, hekimli, saglayici, model, maskeli, null, olcum,
                Birlestir(KotaNedeni, "sağlayıcı 429"), kotaDoldu: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Hasta sayfadan ayrıldı: maskeli metin sağlayıcıya gitmiş olabilir, iz kalsın (istek iptal olsa da).
            DenetimEkle(hasta, saglayici, model, maskeli, null, olcum, basarili: false, yedek: false, "istemci iptal etti");
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception e)
        {
            var hata = e is OperationCanceledException ? "zaman aşımı" : "sağlayıcı hatası";
            return Yedek(hasta, sohbet, hekimli, saglayici, model, maskeli, null, olcum, hata, kotaDoldu: false);
        }

        var modelAdi = yanit.ModelId ?? model;
        var dogrulama = OnAnamnezDogrulayici.Dogrula(yanit.Text, sohbet.Count, hekimli);
        if (dogrulama.Taslak is { } aday)
            dogrulama = OnAnamnezDogrulayici.KodKurallari(aday, sohbet,
                IstanbulSaati.Yas(hasta.DogumTarihi, zaman.GetUtcNow().UtcDateTime), hekimli);
        if (dogrulama.Taslak is null)
            return Yedek(hasta, sohbet, hekimli, saglayici, modelAdi, maskeli, yanit.Text, olcum, dogrulama.Neden!, kotaDoldu: false);

        DenetimEkle(hasta, saglayici, modelAdi, maskeli, dogrulama.Taslak.JsonOlarak(), olcum, basarili: true, yedek: false, null);
        return new AiSonucu(AiKaynak.Llm, dogrulama.Taslak, null, modelAdi);
    }

    /// <summary>Yedek dalı: modelin ham çıktısı (varsa) denetime yazılır, taslak kural tabanlı üretilir.</summary>
    private AiSonucu Yedek(Hasta hasta, IReadOnlyList<SohbetKaydi> sohbet, IReadOnlySet<BransKodu> hekimli, string saglayici,
        string model, string maskeli, string? cikti, Stopwatch olcum, string neden, bool kotaDoldu)
    {
        DenetimEkle(hasta, saglayici, model, maskeli, cikti, olcum, basarili: false, yedek: true, neden);
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
        DenetimEkle(hasta, "Kural", "", maskeli,
            $$"""{"kirmiziBayrak":"{{bayrak.Kural}}","llmCagrildi":false}""", Stopwatch.StartNew(),
            basarili: true, yedek: false, neden: bayrak.Kural.ToString());

    private void DenetimEkle(Hasta hasta, string saglayici, string model, string maskeli, string? cikti,
        Stopwatch olcum, bool basarili, bool yedek, string? neden)
    {
        db.AiAuditLoglari.Add(new AiAuditLog
        {
            Id = Guid.NewGuid(),
            Zaman = zaman.GetUtcNow().UtcDateTime,
            Ozellik = AiOzellik.OnAnamnez,
            HastaId = hasta.Id,
            Saglayici = saglayici,
            Model = model,
            IstemSurumu = OnAnamnezIstemSurumu,
            MaskeliGirdi = maskeli,
            // Hash maskeli metinden: ham kişisel veriden alınan hash sözlük saldırısıyla geri bulunabilir.
            GirdiHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(maskeli))),
            CiktiJson = cikti,
            GecikmeMs = (int)olcum.ElapsedMilliseconds,
            Basarili = basarili,
            YedegeDustu = yedek,
            Neden = neden,
        });
    }
}
