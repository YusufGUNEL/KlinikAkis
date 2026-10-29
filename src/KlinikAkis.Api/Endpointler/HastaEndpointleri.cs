using System.Security.Claims;
using KlinikAkis.Api.Kimlik;
using KlinikAkis.Domain;
using KlinikAkis.Domain.Varliklar;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KlinikAkis.Api.Endpointler;

public static class HastaEndpointleri
{
    public static RouteGroupBuilder MapHastaEndpointleri(this RouteGroupBuilder api)
    {
        var hastalar = api.MapGroup("/hastalar/{id:guid}").WithTags("Hasta")
            .RequireAuthorization(DemoRol.Doktor);

        hastalar.MapGet("/ozet", Ozet).WithName("HastaOzeti").ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Hasta bandı: kimlik (maskeli TC), yaş, alerjiler, kronik hastalıklar, ilaçlar; erişim kaydı yazar");

        hastalar.MapGet("/tahliller", Tahliller).WithName("HastaTahlilleri").ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Teste göre gruplu sonuçlar: hastanın cinsiyetine göre referans, son değer, kodla verilen işaret, trend");

        return api;
    }

    private static async Task<Results<Ok<HastaOzeti>, ProblemHttpResult>> Ozet(
        Guid id, ClaimsPrincipal kullanici, KlinikDbContext db, TimeProvider zaman, CancellationToken ct)
    {
        var h = await db.Hastalar.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (h is null)
            return TypedResults.Problem(detail: "Hasta bulunamadı.", statusCode: StatusCodes.Status404NotFound);

        db.ErisimKayitlari.Add(new ErisimKaydi
        {
            Id = Guid.NewGuid(),
            Zaman = zaman.GetUtcNow().UtcDateTime,
            KullaniciRol = kullanici.FindFirstValue(DemoClaim.Rol) ?? "",
            KullaniciAd = kullanici.FindFirstValue(DemoClaim.Ad) ?? "",
            HastaId = id,
            Eylem = "Görüntüledi",
        });
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new HastaOzeti(
            h.Id, h.Ad, h.Soyad, IstanbulSaati.Yas(h.DogumTarihi, zaman.GetUtcNow().UtcDateTime), h.Cinsiyet,
            TcMaskele(h.TcKimlik), h.TakmaAd, h.Alerjiler, h.KronikHastaliklar, h.KullandigiIlaclar,
            h.AcikRizaAlindi, h.Sentetik));
    }

    private static async Task<Results<Ok<List<TahlilSatiri>>, ProblemHttpResult>> Tahliller(
        Guid id, KlinikDbContext db, CancellationToken ct)
    {
        var cinsiyet = await db.Hastalar.Where(x => x.Id == id).Select(x => (Cinsiyet?)x.Cinsiyet).FirstOrDefaultAsync(ct);
        if (cinsiyet is null)
            return TypedResults.Problem(detail: "Hasta bulunamadı.", statusCode: StatusCodes.Status404NotFound);

        var sonuclar = await db.TahlilSonuclari.AsNoTracking()
            .Include(s => s.TahlilTanimi)
            .Where(s => s.HastaId == id)
            .ToListAsync(ct);

        var satirlar = sonuclar
            .GroupBy(s => s.TahlilTanimiId)
            .Select(g =>
            {
                var t = g.First().TahlilTanimi!;
                var (alt, ust) = TahlilDegerlendirici.Aralik(t, cinsiyet.Value);
                var trend = g.OrderBy(s => s.Zaman)
                    .Select(s => new TahlilDegeri(s.Id, s.Deger, s.Zaman, s.Isaret, s.HastayaYayinlandi))
                    .ToList();
                return new TahlilSatiri(t.Id, t.Ad, t.HastaDostuAd, t.LoincKodu, t.Birim, alt, ust,
                    t.KritikAlt, t.KritikUst, t.Not, trend[^1], trend);
            })
            .OrderByDescending(s => s.Son.Zaman).ThenBy(s => s.LoincKodu, StringComparer.Ordinal)
            .ToList();

        return TypedResults.Ok(satirlar);
    }

    /// <summary>"99*******78": ilk 2 ve son 2 hane görünür.</summary>
    private static string TcMaskele(string tc) => tc.Length == 11 ? tc[..2] + new string('*', 7) + tc[^2..] : "***";
}

public sealed record HastaOzeti(
    Guid Id, string Ad, string Soyad, int Yas, Cinsiyet Cinsiyet, string TcMaskeli, string TakmaAd,
    IReadOnlyList<string> Alerjiler, IReadOnlyList<string> KronikHastaliklar, IReadOnlyList<string> KullandigiIlaclar,
    bool AcikRizaAlindi, bool Sentetik);

public sealed record TahlilSatiri(
    Guid TahlilTanimiId, string Ad, string HastaDostuAd, string LoincKodu, string Birim,
    decimal AltSinir, decimal UstSinir, decimal? KritikAlt, decimal? KritikUst, string Not,
    TahlilDegeri Son, IReadOnlyList<TahlilDegeri> Trend);

public sealed record TahlilDegeri(Guid Id, decimal Deger, DateTime Zaman, Isaret Isaret, bool HastayaYayinlandi);
