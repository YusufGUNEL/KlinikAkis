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

/// <summary>
/// Tahliller (SPEC §9.6, §10.3). İşaret kodda hesaplanmıştır; AI yalnızca hastaya açıklama cümlesi taslağı kurar.
/// Taslak hastaya gitmez: hasta yalnızca hekimin yayınladığı metni (<c>YayinlananAciklama</c>) görür.
/// </summary>
public static class TahlilEndpointleri
{
    public const int AciklamaSiniri = 1000;

    public static RouteGroupBuilder MapTahlilEndpointleri(this RouteGroupBuilder api)
    {
        var doktor = api.MapGroup("/tahliller").WithTags("Tahlil").RequireAuthorization(DemoRol.Doktor);

        doktor.MapPost("/{id:guid}/aciklama", Aciklama).WithName("TahlilAciklamaTaslagi")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(AiHizSiniri.Politika)
            .WithSummary("AI açıklama taslağı: işaret koddan, model yalnızca cümle kurar (kimlik gitmez) / yedek şablon. Denetim kaydı yazar");

        doktor.MapPost("/yayinla", Yayinla).WithName("TahlilYayinla")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesValidationProblem()
            .WithSummary("Hekimin onayladığı/düzenlediği açıklamayla hastaya yayınlar. Kritik değerde bilgilendirme seçimi zorunlu; " +
                "erişim kaydı, 105 outbox kaydı, TahlilYayinlandi olayı");

        api.MapGet("/hasta/me/sonuclar", HastaSonuclari).WithTags("Tahlil").WithName("HastaSonuclari")
            .RequireAuthorization(DemoRol.Hasta)
            .WithSummary("Hastanın kendi sonuçları: yalnızca yayınlanmış değerler ve hekimin yayınladığı açıklama (AI taslağı asla)");

        return api;
    }

    private static async Task<Results<Ok<SonucAciklamasi>, ProblemHttpResult>> Aciklama(
        Guid id, KlinikDbContext db, AiHatti hatti, TimeProvider zaman, CancellationToken ct)
    {
        var sonuc = await db.TahlilSonuclari.Include(s => s.TahlilTanimi).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sonuc is null)
            return TypedResults.Problem(detail: "Tahlil sonucu bulunamadı.", statusCode: StatusCodes.Status404NotFound);
        if (sonuc.HastayaYayinlandi)
            return TypedResults.Problem(detail: "Bu sonuç hastaya yayınlandı; yeni taslak üretilmez.", statusCode: StatusCodes.Status409Conflict);

        var cinsiyet = await db.Hastalar.Where(h => h.Id == sonuc.HastaId).Select(h => h.Cinsiyet).FirstAsync(ct);
        var t = sonuc.TahlilTanimi!;
        // Son 3 değer, açıklanan sonuç dahil, eskiden yeniye (§9.6).
        var trend = (await db.TahlilSonuclari.AsNoTracking()
                .Where(s => s.HastaId == sonuc.HastaId && s.TahlilTanimiId == sonuc.TahlilTanimiId)
                .Select(s => new { s.Zaman, s.Deger }).ToListAsync(ct))
            .Where(s => s.Zaman <= sonuc.Zaman).OrderBy(s => s.Zaman).TakeLast(3).Select(s => s.Deger).ToList();
        var (alt, ust) = TahlilDegerlendirici.Aralik(t, cinsiyet);
        var girdi = new TahlilGirdisi(t.Ad, t.HastaDostuAd, t.Birim, alt, ust, sonuc.Isaret, sonuc.Deger, trend, t.Ondalik);

        var ai = await hatti.TahlilAciklamasiAsync(sonuc.HastaId, girdi, ct);
        sonuc.AiAciklamaTaslak = ai.Aciklama;
        sonuc.AciklamaKaynak = ai.Kaynak;
        sonuc.AiModel = ai.Model;
        sonuc.AiZamani = zaman.GetUtcNow().UtcDateTime;
        sonuc.KotaDoldu = ai.KotaDoldu;
        // Model çağrıldıktan sonra iptal edilmez: denetim kaydı ve taslak kaybolmasın.
        await db.SaveChangesAsync(CancellationToken.None);
        return TypedResults.Ok(SonucAciklamasi.Olustur(sonuc, null));
    }

    private static async Task<Results<Ok<YayinYaniti>, ProblemHttpResult, ValidationProblem>> Yayinla(
        YayinIstegi istek, ClaimsPrincipal kullanici, KlinikDbContext db, TimeProvider zaman, IOlayYayinci yayinci,
        CancellationToken ct)
    {
        var ogeler = istek.Sonuclar ?? [];
        if (ogeler.Count == 0 || ogeler.Select(o => o.SonucId).Distinct().Count() != ogeler.Count)
            return Gecersiz("sonuclar", "Yayınlanacak sonuçları birer kez seçin.");
        if (ogeler.Any(o => string.IsNullOrWhiteSpace(o.Aciklama)))
            return Gecersiz("aciklama", "Her sonuç için hastaya gidecek açıklamayı yazın.");
        if (ogeler.Any(o => o.Aciklama!.Trim().Length > AciklamaSiniri))
            return Gecersiz("aciklama", $"Açıklama en fazla {AciklamaSiniri} karakter olabilir.");

        var idler = ogeler.Select(o => o.SonucId).ToList();
        var sonuclar = await db.TahlilSonuclari.Include(s => s.TahlilTanimi).Where(s => idler.Contains(s.Id)).ToListAsync(ct);
        if (sonuclar.Count != ogeler.Count)
            return TypedResults.Problem(detail: "Tahlil sonucu bulunamadı.", statusCode: StatusCodes.Status404NotFound);
        if (sonuclar.Select(s => s.HastaId).Distinct().Count() != 1)
            return Gecersiz("sonuclar", "Tek seferde yalnızca bir hastanın sonuçları yayınlanabilir.");
        if (sonuclar.Any(s => s.HastayaYayinlandi))
            return TypedResults.Problem(detail: "Sonuç hastaya zaten yayınlandı.", statusCode: StatusCodes.Status409Conflict);

        // §10.3: kritik değer hastaya yayınlanırken bilgilendirme seçimi zorunlu; seçim erişim kaydına yazılır.
        var kritikler = sonuclar.Where(s => TahlilGirdisi.Kritik(s.Isaret)).ToList();
        if (kritikler.Count > 0 && istek.Bilgilendirme is null)
            return Gecersiz("bilgilendirme", "Kritik değer: hastanın telefonla bilgilendirildiğini ya da bilgilendirileceğini seçin.");

        var simdi = zaman.GetUtcNow().UtcDateTime;
        var hekimId = Guid.Parse(kullanici.FindFirstValue(DemoClaim.HekimId)!);
        var hastaId = sonuclar[0].HastaId;
        var takmaAd = await db.Hastalar.Where(h => h.Id == hastaId).Select(h => h.TakmaAd).FirstAsync(ct);
        var metin = ogeler.ToDictionary(o => o.SonucId, o => o.Aciklama!.Trim());

        foreach (var s in sonuclar)
        {
            s.YayinlananAciklama = metin[s.Id];
            s.HastayaYayinlandi = true;
            s.YayinZamani = simdi;
            s.YayinlayanHekimId = hekimId;
            // e-Nabız 105 Lab Sonuç (simülasyon, SPEC §5.2): kimlik yerine takma ad; gönderen işleyici Aşama 7'de.
            db.OutboxMesajlari.Add(new OutboxMesaji
            {
                Id = Guid.NewGuid(),
                PaketTipi = PaketTipi.LabSonuc,
                HastaTakmaAdi = takmaAd,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    loinc = s.TahlilTanimi!.LoincKodu, deger = s.Deger, birim = s.TahlilTanimi.Birim,
                    isaret = s.Isaret.ToString(), zaman = s.Zaman,
                }, JsonSerializerOptions.Web),
                Zaman = simdi,
            });
        }

        var testler = string.Join(", ", sonuclar.Select(s => s.TahlilTanimi!.Ad));
        Erisim(sonuclar.Count == 1 ? $"Tahlil sonucunu hastaya yayınladı: {testler}" : $"Tahlil sonuçlarını hastaya yayınladı: {testler}");
        if (kritikler.Count > 0)
            Erisim($"Kritik değer ({string.Join(", ", kritikler.Select(k => $"{k.TahlilTanimi!.Ad} {DoktorEtiketi(k.Isaret)}"))}): " +
                (istek.Bilgilendirme == BilgilendirmeSecimi.TelefonlaBilgilendirildi
                    ? "hasta telefonla bilgilendirildi"
                    : "hasta bilgilendirilecek"));
        await db.SaveChangesAsync(ct);

        await yayinci.YayinlaAsync(OlayTuru.TahlilYayinlandi, hastaId, [Grup.Hasta(hastaId), Grup.Doktor(hekimId)], ct);
        return TypedResults.Ok(new YayinYaniti(sonuclar.Select(s => s.Id).ToList(), simdi));

        void Erisim(string eylem) => db.ErisimKayitlari.Add(new ErisimKaydi
        {
            Id = Guid.NewGuid(),
            Zaman = simdi,
            KullaniciRol = kullanici.FindFirstValue(DemoClaim.Rol) ?? "",
            KullaniciAd = kullanici.FindFirstValue(DemoClaim.Ad) ?? "",
            HastaId = hastaId,
            Eylem = eylem,
        });
    }

    private static async Task<Ok<List<HastaSonucu>>> HastaSonuclari(ClaimsPrincipal kullanici, KlinikDbContext db, CancellationToken ct)
    {
        var hastaId = Guid.Parse(kullanici.FindFirstValue(DemoClaim.HastaId)!);
        var cinsiyet = await db.Hastalar.Where(h => h.Id == hastaId).Select(h => h.Cinsiyet).FirstAsync(ct);
        // Yalnızca yayınlanmış satırlar sorgulanır: taslak ve yayınlanmamış değer bu uçtan hiç çıkmaz (§11 test 8).
        var yayinlanan = await db.TahlilSonuclari.AsNoTracking().Include(s => s.TahlilTanimi)
            .Where(s => s.HastaId == hastaId && s.HastayaYayinlandi)
            .ToListAsync(ct);
        var hekimler = await HekimAdlariAsync(db, yayinlanan.Select(s => s.YayinlayanHekimId), ct);

        var liste = yayinlanan.GroupBy(s => s.TahlilTanimiId)
            .Select(g =>
            {
                var sirali = g.OrderBy(s => s.Zaman).ToList();
                var son = sirali[^1];
                var t = son.TahlilTanimi!;
                var (alt, ust) = TahlilDegerlendirici.Aralik(t, cinsiyet);
                return new HastaSonucu(son.Id, t.Ad, t.HastaDostuAd, t.LoincKodu, t.Birim, alt, ust, t.Ondalik, t.Not,
                    son.Deger, son.Zaman, son.Isaret, son.YayinlananAciklama, son.YayinZamani!.Value,
                    son.YayinlayanHekimId is { } h ? hekimler.GetValueOrDefault(h) : null,
                    sirali.Select(s => new TrendNoktasi(s.Deger, s.Zaman)).ToList());
            })
            .OrderByDescending(s => s.YayinZamani).ThenBy(s => s.LoincKodu, StringComparer.Ordinal)
            .ToList();
        return TypedResults.Ok(liste);
    }

    internal static async Task<Dictionary<Guid, string>> HekimAdlariAsync(
        KlinikDbContext db, IEnumerable<Guid?> idler, CancellationToken ct)
    {
        var liste = idler.OfType<Guid>().Distinct().ToList();
        return liste.Count == 0
            ? []
            : await db.Hekimler.AsNoTracking().Where(h => liste.Contains(h.Id))
                .ToDictionaryAsync(h => h.Id, h => h.Unvan + " " + h.Ad, ct);
    }

    /// <summary>Doktor etiketi (SPEC §10.1): erişim kaydı metninde kullanılır.</summary>
    private static string DoktorEtiketi(Isaret i) => i switch
    {
        Isaret.KritikYuksek => "‼ KY",
        Isaret.KritikDusuk => "‼ KD",
        Isaret.Yuksek => "▲ Y",
        Isaret.Dusuk => "▼ D",
        _ => "✓",
    };

    private static ValidationProblem Gecersiz(string alan, string mesaj) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [alan] = [mesaj] });
}

/// <summary>Bir sonucun açıklama durumu: AI taslağı (kart başlığı bilgisiyle) ve varsa yayınlanan metin.</summary>
public sealed record SonucAciklamasi(
    AiKaynak? Kaynak, string? Taslak, string? Model, DateTime? Zaman, bool KotaDoldu,
    string? Yayinlanan, DateTime? YayinZamani, string? YayinlayanHekim)
{
    public static SonucAciklamasi Olustur(TahlilSonucu s, string? hekim) =>
        new(s.AciklamaKaynak, s.AiAciklamaTaslak, s.AiModel, s.AiZamani, s.KotaDoldu,
            s.YayinlananAciklama, s.HastayaYayinlandi ? s.YayinZamani : null, hekim);
}

public sealed record YayinOgesi(Guid SonucId, string? Aciklama);

/// <param name="Bilgilendirme">Kritik değer varsa zorunlu (SPEC §10.3).</param>
public sealed record YayinIstegi(List<YayinOgesi>? Sonuclar, BilgilendirmeSecimi? Bilgilendirme);

public sealed record YayinYaniti(IReadOnlyList<Guid> SonucIdleri, DateTime YayinZamani);

public sealed record TrendNoktasi(decimal Deger, DateTime Zaman);

/// <summary>Hastanın gördüğü sonuç (taslak 3): son yayınlanmış değer, hekimin yayınladığı metin, yayınlanmış trend.</summary>
public sealed record HastaSonucu(
    Guid SonucId, string Ad, string HastaDostuAd, string LoincKodu, string Birim, decimal AltSinir, decimal UstSinir,
    int Ondalik, string Not, decimal Deger, DateTime Zaman, Isaret Isaret, string? Aciklama, DateTime YayinZamani,
    string? YayinlayanHekim, IReadOnlyList<TrendNoktasi> Trend);
