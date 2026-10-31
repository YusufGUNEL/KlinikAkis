using System.Security.Claims;
using System.Text.Json;
using KlinikAkis.Api.Gercekzaman;
using KlinikAkis.Api.Kimlik;
using KlinikAkis.Domain;
using KlinikAkis.Domain.Varliklar;
using KlinikAkis.Infrastructure.Ai;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KlinikAkis.Api.Endpointler;

/// <summary>LLM uçlarının hız sınırı politikası (SPEC §9.9); sınır <c>AI:DakikadaIstek</c> (varsayılan 10).</summary>
public static class AiHizSiniri
{
    public const string Politika = "ai";
}

/// <summary>
/// Şikayet sohbeti (SPEC §9.4) ve kırmızı bayrak (§9.3). Her cevapta kural çalışır; tetiklenirse akış durur
/// (sonraki cevap, özet ve bu ön anamnezle randevu 409). Özet yalnızca AiHatti üzerinden üretilir.
/// Taslak hasta onayından (taslak 2) ve hekim kararından (§10.2) geçer; AI çıktısı kendiliğinden kayda girmez.
/// </summary>
public static class OnAnamnezEndpointleri
{
    public static RouteGroupBuilder MapOnAnamnezEndpointleri(this RouteGroupBuilder api)
    {
        var grup = api.MapGroup("/onanamnez").WithTags("Ön anamnez").RequireAuthorization(DemoRol.Hasta);

        grup.MapPost("", Baslat).WithName("OnAnamnezBaslat")
            .WithSummary("Sohbet başlatır → ilk soru (soru ağacı kodda sabit; LLM soru üretmez)");

        grup.MapPost("/{id:guid}/cevap", Cevap).WithName("OnAnamnezCevap")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesValidationProblem()
            .WithSummary("Cevabı kaydeder; her adımda kırmızı bayrak kuralı çalışır → sonraki soru ya da 112 uyarısı");

        grup.MapPost("/{id:guid}/ozet", Ozet).WithName("OnAnamnezOzet")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(AiHizSiniri.Politika)
            .WithSummary("Sohbet bitti: kural kontrolü → (temizse) maskeli metinle LLM taslağı / yedek. Denetim kaydı yazar");

        grup.MapPost("/{id:guid}/hasta-onayi", HastaOnayi).WithName("OnAnamnezHastaOnayi")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesValidationProblem()
            .WithSummary("Hasta özeti doğrular ya da not ekler (Düzelt / Doğru); taslak değişmez, not hekime gider");

        var doktor = api.MapGroup("/onanamnez").WithTags("Ön anamnez").RequireAuthorization(DemoRol.Doktor);

        doktor.MapGet("/{id:guid}", Detay).WithName("OnAnamnezDetayi").ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Doktor kartı: taslak, kaynak sohbet adımları, hasta notu, hekim kararı");

        doktor.MapPost("/{id:guid}/karar", Karar).WithName("OnAnamnezKarar")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesValidationProblem()
            .WithSummary("Hekim kararı: onayla / duzenle (metinle) / reddet. Erişim kaydı yazar, OnAnamnezKarar olayı");

        api.MapGet("/ai/denetim", Denetim).WithTags("AI").WithName("AiDenetim").RequireAuthorization(DemoRol.Doktor)
            .WithSummary("Modele giden veri: maskeli girdi, sağlayıcı, istem sürümü, süre, yedek/kural nedeni (yeniden eskiye)");

        return api;
    }

    private static async Task<Ok<OnAnamnezBaslatYaniti>> Baslat(
        ClaimsPrincipal kullanici, KlinikDbContext db, CancellationToken ct)
    {
        var onAnamnez = new OnAnamnez { Id = Guid.NewGuid(), HastaId = HastaId(kullanici) };
        db.OnAnamnezler.Add(onAnamnez);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new OnAnamnezBaslatYaniti(onAnamnez.Id, SoruAgaci.Soru(1), SoruAgaci.ToplamAdim));
    }

    private static async Task<Results<Ok<CevapYaniti>, ProblemHttpResult, ValidationProblem>> Cevap(
        Guid id, CevapIstegi istek, ClaimsPrincipal kullanici, KlinikDbContext db, AiHatti hatti, CancellationToken ct)
    {
        var (onAnamnez, hasta, sorun) = await YukleAsync(id, kullanici, db, ct);
        if (sorun is not null) return sorun;
        if (onAnamnez!.Durum == OnAnamnezDurumu.KirmiziBayrak) return AkisDurdu();
        if (onAnamnez.Durum != OnAnamnezDurumu.Sohbette)
            return TypedResults.Problem(detail: "Bu sohbet tamamlandı.", statusCode: StatusCodes.Status409Conflict);

        var sohbet = Sohbet(onAnamnez);
        if (sohbet.Count >= SoruAgaci.ToplamAdim)
            return TypedResults.Problem(detail: "Tüm sorular cevaplandı; özet bekleniyor.", statusCode: StatusCodes.Status409Conflict);

        var soru = SoruAgaci.Soru(sohbet.Count + 1);
        var secimler = istek.Secimler ?? [];
        var metin = string.IsNullOrWhiteSpace(istek.Metin) ? null : istek.Metin.Trim();
        if (SoruAgaci.Denetle(soru, secimler, metin) is { } hata)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["cevap"] = [hata] });

        sohbet.Add(new SohbetKaydi(soru.Adim, soru.Metin, secimler, metin));
        onAnamnez.SohbetJson = JsonSerializer.Serialize(sohbet, JsonSerializerOptions.Web);

        var bayrak = hatti.KuralKontrolu(hasta!, sohbet);
        if (bayrak is not null)
            KirmiziBayragaAl(onAnamnez, bayrak);
        await db.SaveChangesAsync(ct);

        var sonraki = bayrak is null && sohbet.Count < SoruAgaci.ToplamAdim ? SoruAgaci.Soru(sohbet.Count + 1) : null;
        return TypedResults.Ok(new CevapYaniti(sonraki, sohbet.Count, SoruAgaci.ToplamAdim, bayrak));
    }

    private static async Task<Results<Ok<OzetYaniti>, ProblemHttpResult>> Ozet(
        Guid id, ClaimsPrincipal kullanici, KlinikDbContext db, AiHatti hatti, TimeProvider zaman, CancellationToken ct)
    {
        var (onAnamnez, hasta, sorun) = await YukleAsync(id, kullanici, db, ct);
        if (sorun is not null) return sorun;
        if (onAnamnez!.Durum == OnAnamnezDurumu.KirmiziBayrak) return AkisDurdu();
        // Tekrar istek modeli yeniden çağırmaz (maliyet/kota): mevcut taslak döner.
        if (onAnamnez.Durum != OnAnamnezDurumu.Sohbette)
            return TypedResults.Ok(new OzetYaniti(onAnamnez.Id, onAnamnez.Durum, onAnamnez.Kaynak, null,
                HastaKarti(await KartAsync(onAnamnez, db, ct), Sohbet(onAnamnez))));

        var sohbet = Sohbet(onAnamnez);
        if (sohbet.Count < SoruAgaci.ToplamAdim)
            return TypedResults.Problem(detail: "Sohbet henüz tamamlanmadı.", statusCode: StatusCodes.Status409Conflict);

        var sonuc = await hatti.OnAnamnezOzetiAsync(hasta!, sohbet, ct);
        if (sonuc.KirmiziBayrak is { } bayrak)
            KirmiziBayragaAl(onAnamnez, bayrak);
        else
        {
            onAnamnez.Durum = OnAnamnezDurumu.Taslak;
            onAnamnez.Kaynak = sonuc.Kaynak;
            onAnamnez.AiTaslakJson = sonuc.Taslak?.JsonOlarak();
            onAnamnez.AiModel = sonuc.Model;
            onAnamnez.AiZamani = zaman.GetUtcNow().UtcDateTime;
            onAnamnez.KotaDoldu = sonuc.KotaDoldu;
        }
        // Model çağrıldıktan sonra iptal edilmez: denetim kaydı ve taslak kaybolmasın.
        await db.SaveChangesAsync(CancellationToken.None);
        return TypedResults.Ok(new OzetYaniti(onAnamnez.Id, onAnamnez.Durum, onAnamnez.Kaynak, sonuc.KirmiziBayrak,
            HastaKarti(await KartAsync(onAnamnez, db, ct), sohbet)));
    }

    private static async Task<Results<Ok<HastaOzetKarti>, ProblemHttpResult, ValidationProblem>> HastaOnayi(
        Guid id, HastaOnayiIstegi istek, ClaimsPrincipal kullanici, KlinikDbContext db, IOlayYayinci yayinci, CancellationToken ct)
    {
        var (onAnamnez, _, sorun) = await YukleAsync(id, kullanici, db, ct);
        if (sorun is not null) return sorun;
        if (onAnamnez!.Durum == OnAnamnezDurumu.KirmiziBayrak) return AkisDurdu();
        if (onAnamnez.Durum == OnAnamnezDurumu.Sohbette || onAnamnez.AiTaslakJson is null)
            return TypedResults.Problem(detail: "Özet henüz hazır değil.", statusCode: StatusCodes.Status409Conflict);
        // Hekim karar verdikten sonra hastanın notu değişmez.
        if (onAnamnez.Durum != OnAnamnezDurumu.Taslak)
            return TypedResults.Problem(detail: "Doktorunuz özeti değerlendirdi; not eklenemez.", statusCode: StatusCodes.Status409Conflict);

        var not = string.IsNullOrWhiteSpace(istek.Not) ? null : istek.Not.Trim();
        if (not is { Length: > SoruAgaci.MetinSiniri })
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["not"] = [$"Not en fazla {SoruAgaci.MetinSiniri} karakter olabilir."],
            });
        if (!istek.Dogru && not is null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["not"] = ["Neyin düzeltilmesi gerektiğini kısaca yazın."],
            });

        onAnamnez.HastaOnayi = istek.Dogru;
        onAnamnez.HastaNotu = not;
        await db.SaveChangesAsync(ct);

        // Randevu alındıktan sonra not eklenirse hekimin kartı yenilensin.
        if (await RandevuHekimiAsync(onAnamnez, db, ct) is { } hekimId)
            await yayinci.YayinlaAsync(OlayTuru.OnAnamnezHazir, onAnamnez.Id, [Grup.Doktor(hekimId)], ct);
        return TypedResults.Ok(HastaKarti(await KartAsync(onAnamnez, db, ct), Sohbet(onAnamnez))!);
    }

    private static async Task<Results<Ok<OnAnamnezDetayi>, ProblemHttpResult>> Detay(
        Guid id, KlinikDbContext db, CancellationToken ct)
    {
        var onAnamnez = await db.OnAnamnezler.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (onAnamnez is null)
            return TypedResults.Problem(detail: "Ön anamnez bulunamadı.", statusCode: StatusCodes.Status404NotFound);
        return TypedResults.Ok(await DetayAsync(onAnamnez, db, ct));
    }

    private static async Task<Results<Ok<OnAnamnezDetayi>, ProblemHttpResult, ValidationProblem>> Karar(
        Guid id, KararIstegi istek, ClaimsPrincipal kullanici, KlinikDbContext db, TimeProvider zaman,
        IOlayYayinci yayinci, CancellationToken ct)
    {
        var onAnamnez = await db.OnAnamnezler.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (onAnamnez is null)
            return TypedResults.Problem(detail: "Ön anamnez bulunamadı.", statusCode: StatusCodes.Status404NotFound);
        if (onAnamnez.Durum != OnAnamnezDurumu.Taslak)
            return TypedResults.Problem(detail: "Bu taslak için karar zaten verildi ya da taslak hazır değil.",
                statusCode: StatusCodes.Status409Conflict);

        var metin = string.IsNullOrWhiteSpace(istek.Metin) ? null : istek.Metin.Trim();
        (OnAnamnezDurumu Durum, string Eylem)? sonuc = istek.Karar switch
        {
            "onayla" => (OnAnamnezDurumu.Onaylandi, "Ön anamnezi onayladı"),
            "duzenle" => (OnAnamnezDurumu.Duzenlendi, "Ön anamnezi düzenledi"),
            "reddet" => (OnAnamnezDurumu.Reddedildi, "Ön anamnezi reddetti"),
            _ => null,
        };
        if (sonuc is not { } k)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["karar"] = ["Karar 'onayla', 'duzenle' ya da 'reddet' olmalı."],
            });
        if (k.Durum == OnAnamnezDurumu.Duzenlendi && metin is null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["metin"] = ["Düzenlenmiş özeti yazın."] });
        if (metin is { Length: > HekimMetniSiniri })
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["metin"] = [$"Metin en fazla {HekimMetniSiniri} karakter olabilir."],
            });

        var simdi = zaman.GetUtcNow().UtcDateTime;
        var hekimId = Guid.Parse(kullanici.FindFirstValue(DemoClaim.HekimId)!);
        onAnamnez.Durum = k.Durum;
        onAnamnez.HekimDuzeltmesi = k.Durum == OnAnamnezDurumu.Duzenlendi ? metin : null;
        onAnamnez.OnaylayanHekimId = hekimId;
        onAnamnez.OnayZamani = simdi;
        db.ErisimKayitlari.Add(new ErisimKaydi
        {
            Id = Guid.NewGuid(),
            Zaman = simdi,
            KullaniciRol = kullanici.FindFirstValue(DemoClaim.Rol) ?? "",
            KullaniciAd = kullanici.FindFirstValue(DemoClaim.Ad) ?? "",
            HastaId = onAnamnez.HastaId,
            Eylem = k.Eylem,
        });
        await db.SaveChangesAsync(ct);

        List<string> gruplar = [Grup.Hasta(onAnamnez.HastaId), Grup.Doktor(hekimId)];
        if (await RandevuHekimiAsync(onAnamnez, db, ct) is { } randevuHekimi)
            gruplar.Add(Grup.Doktor(randevuHekimi));
        await yayinci.YayinlaAsync(OlayTuru.OnAnamnezKarar, onAnamnez.Id, gruplar, ct);
        return TypedResults.Ok(await DetayAsync(onAnamnez, db, ct));
    }

    private const int HekimMetniSiniri = 2000;

    private static async Task<Ok<List<DenetimSatiri>>> Denetim(Guid? hastaId, KlinikDbContext db, CancellationToken ct)
    {
        var sorgu = db.AiAuditLoglari.AsNoTracking();
        if (hastaId is { } h)
            sorgu = sorgu.Where(x => x.HastaId == h);
        var satirlar = await sorgu.OrderByDescending(x => x.Zaman).Take(50)
            .Select(x => new DenetimSatiri(x.Id, x.Zaman, x.Ozellik, x.HastaId, x.Saglayici, x.Model, x.IstemSurumu,
                x.MaskeliGirdi, x.GirdiHash, x.CiktiJson, x.GecikmeMs, x.Basarili, x.YedegeDustu, x.Neden))
            .ToListAsync(ct);
        return TypedResults.Ok(satirlar);
    }

    private static async Task<Guid?> RandevuHekimiAsync(OnAnamnez o, KlinikDbContext db, CancellationToken ct) =>
        o.RandevuId is { } r
            ? await db.Randevular.Where(x => x.Id == r).Select(x => (Guid?)x.HekimId).FirstOrDefaultAsync(ct)
            : null;

    /// <summary>
    /// Hastaya giden kart: modelin serbest metni (özet, gerekçe, sorular, aciliyet) hekim onayı olmadan hastaya
    /// gösterilmez. Hasta yalnızca kendi cevaplarını ve kodda doğrulanmış bölüm önerisini görür (CLAUDE.md).
    /// </summary>
    private static HastaOzetKarti? HastaKarti(OnAnamnezKarti? k, IReadOnlyList<SohbetKaydi> sohbet) =>
        k is null
            ? null
            : new HastaOzetKarti(k.Id, k.Durum, k.Kaynak, k.Zaman, k.KotaDoldu,
                sohbet.Where(s => !string.IsNullOrWhiteSpace(s.Cevap) && s.Cevap != SoruAgaci.Hicbiri)
                    .Select(s => new HastaCevabi(s.Adim, s.Soru, s.Cevap)).ToList(),
                k.Onerilen, k.Alternatif);

    /// <summary>Taslak + bölüm adları. Taslak yoksa (sohbette / kırmızı bayrak) null.</summary>
    private static async Task<OnAnamnezKarti?> KartAsync(OnAnamnez o, KlinikDbContext db, CancellationToken ct)
    {
        if (o.AiTaslakJson is null) return null;
        var taslak = JsonSerializer.Deserialize<OnAnamnezTaslagi>(o.AiTaslakJson, OnAnamnezTaslagi.Json)!;
        var kodlar = new List<BransKodu> { taslak.OnerilenBrans };
        if (taslak.AlternatifBrans is { } a) kodlar.Add(a);
        var branslar = await db.Branslar.AsNoTracking().Where(b => kodlar.Contains(b.Kod))
            .ToDictionaryAsync(b => b.Kod, b => new BolumOnerisi(b.Id, b.Kod, b.Ad), ct);
        return new OnAnamnezKarti(o.Id, o.Durum, o.Kaynak!.Value, o.AiModel, o.AiZamani!.Value, o.KotaDoldu, taslak,
            branslar[taslak.OnerilenBrans],
            taslak.AlternatifBrans is { } alt ? branslar.GetValueOrDefault(alt) : null);
    }

    private static async Task<OnAnamnezDetayi> DetayAsync(OnAnamnez o, KlinikDbContext db, CancellationToken ct)
    {
        HekimKarari? karar = null;
        if (o.OnaylayanHekimId is { } hekimId)
        {
            var hekim = await db.Hekimler.AsNoTracking().Where(h => h.Id == hekimId)
                .Select(h => h.Unvan + " " + h.Ad).FirstAsync(ct);
            karar = new HekimKarari(o.Durum, hekim, o.OnayZamani!.Value, o.HekimDuzeltmesi);
        }
        return new OnAnamnezDetayi(o.Id, o.HastaId, o.Durum, o.KirmiziBayrakKurali, await KartAsync(o, db, ct), Sohbet(o),
            o.HastaOnayi, o.HastaNotu, karar);
    }

    private static void KirmiziBayragaAl(OnAnamnez onAnamnez, KirmiziBayrak bayrak)
    {
        onAnamnez.Durum = OnAnamnezDurumu.KirmiziBayrak;
        onAnamnez.Kaynak = AiKaynak.Kural;
        onAnamnez.KirmiziBayrakKurali = bayrak.Kural.ToString();
    }

    private static ProblemHttpResult AkisDurdu() =>
        TypedResults.Problem(detail: KirmiziBayrakMotoru.AcilMesaji, statusCode: StatusCodes.Status409Conflict);

    private static Guid HastaId(ClaimsPrincipal kullanici) => Guid.Parse(kullanici.FindFirstValue(DemoClaim.HastaId)!);

    private static List<SohbetKaydi> Sohbet(OnAnamnez o) =>
        JsonSerializer.Deserialize<List<SohbetKaydi>>(o.SohbetJson, JsonSerializerOptions.Web) ?? [];

    private static async Task<(OnAnamnez?, Hasta?, ProblemHttpResult?)> YukleAsync(
        Guid id, ClaimsPrincipal kullanici, KlinikDbContext db, CancellationToken ct)
    {
        var onAnamnez = await db.OnAnamnezler.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (onAnamnez is null)
            return (null, null, TypedResults.Problem(detail: "Sohbet bulunamadı.", statusCode: StatusCodes.Status404NotFound));
        if (onAnamnez.HastaId != HastaId(kullanici))
            return (null, null, TypedResults.Problem(detail: "Bu işlem için yetkiniz yok.", statusCode: StatusCodes.Status403Forbidden));
        var hasta = await db.Hastalar.AsNoTracking().FirstAsync(h => h.Id == onAnamnez.HastaId, ct);
        return (onAnamnez, hasta, null);
    }
}

public sealed record OnAnamnezBaslatYaniti(Guid OnAnamnezId, SohbetSorusu IlkSoru, int ToplamAdim);

public sealed record CevapIstegi(string? Metin, List<string>? Secimler);

public sealed record CevapYaniti(SohbetSorusu? SonrakiSoru, int Adim, int ToplamAdim, KirmiziBayrak? KirmiziBayrak);

public sealed record OzetYaniti(
    Guid OnAnamnezId, OnAnamnezDurumu Durum, AiKaynak? Kaynak, KirmiziBayrak? KirmiziBayrak, HastaOzetKarti? Kart);

public sealed record HastaOnayiIstegi(bool Dogru, string? Not);

public sealed record KararIstegi(string Karar, string? Metin);

public sealed record BolumOnerisi(Guid BransId, BransKodu Kod, string Ad);

/// <param name="Zaman">Taslağın üretildiği an (UTC); kart başlığında saat.</param>
/// <param name="KotaDoldu">Kota doldu, taslak kural tabanlı: kartta "demo kotası doldu" rozeti.</param>
public sealed record OnAnamnezKarti(
    Guid Id, OnAnamnezDurumu Durum, AiKaynak Kaynak, string? Model, DateTime Zaman, bool KotaDoldu,
    OnAnamnezTaslagi Taslak, BolumOnerisi Onerilen, BolumOnerisi? Alternatif);

public sealed record HastaCevabi(int Adim, string Soru, string Cevap);

/// <summary>Hastanın gördüğü özet (taslak 2): kendi cevapları + bölüm önerisi; model metni yok.</summary>
public sealed record HastaOzetKarti(
    Guid Id, OnAnamnezDurumu Durum, AiKaynak Kaynak, DateTime Zaman, bool KotaDoldu,
    IReadOnlyList<HastaCevabi> Cevaplar, BolumOnerisi Onerilen, BolumOnerisi? Alternatif);

public sealed record HekimKarari(OnAnamnezDurumu Durum, string HekimAd, DateTime Zaman, string? Duzeltme);

public sealed record OnAnamnezDetayi(
    Guid Id, Guid HastaId, OnAnamnezDurumu Durum, string? KirmiziBayrakKurali, OnAnamnezKarti? Kart,
    IReadOnlyList<SohbetKaydi> Sohbet, bool HastaOnayi, string? HastaNotu, HekimKarari? Karar);

public sealed record DenetimSatiri(
    Guid Id, DateTime Zaman, AiOzellik Ozellik, Guid? HastaId, string Saglayici, string Model, string IstemSurumu,
    string MaskeliGirdi, string GirdiHash, string? CiktiJson, int GecikmeMs, bool Basarili, bool YedegeDustu, string? Neden);
