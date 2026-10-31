using KlinikAkis.Api.Gercekzaman;
using KlinikAkis.Api.Kimlik;
using KlinikAkis.Domain;
using KlinikAkis.Infrastructure.Outbox;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KlinikAkis.Api.Endpointler;

/// <summary>
/// e-Nabız gönderim kutusu (SPEC §5.2, taslak 9; simülasyon). Liste doktor ve kabul masasına açık; paketlerde kimlik yok,
/// yalnızca takma ad. "Yeniden dene" yalnızca iş kuralı hatasındaki pakete: Bekliyor'a döner, işleyici bir sonraki tikte gönderir.
/// </summary>
public static class OutboxEndpointleri
{
    public const int ListeSiniri = 50;

    public static RouteGroupBuilder MapOutboxEndpointleri(this RouteGroupBuilder api)
    {
        var outbox = api.MapGroup("/outbox").WithTags("Outbox").RequireAuthorization(DemoPolitika.Personel);

        outbox.MapGet("/", Liste).WithName("OutboxListesi")
            .WithSummary("Son paketler (en yeni ilk); takmaAd verilirse yalnızca o hastanın paketleri");

        outbox.MapPost("/{id:guid}/yeniden-dene", YenidenDene).WithName("OutboxYenidenDene")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("İş kuralı hatasındaki paketi yeniden kuyruğa alır; başka durumda 409");
        return api;
    }

    private static async Task<Ok<List<OutboxSatiri>>> Liste(string? takmaAd, KlinikDbContext db, CancellationToken ct)
    {
        var sorgu = db.OutboxMesajlari.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(takmaAd)) sorgu = sorgu.Where(o => o.HastaTakmaAdi == takmaAd);
        var liste = await sorgu.OrderByDescending(o => o.Zaman).ThenByDescending(o => o.PaketTipi).Take(ListeSiniri)
            .Select(o => new OutboxSatiri(o.Id, o.PaketTipi, o.HastaTakmaAdi, o.Durum, o.Deneme, o.Zaman, o.Hata))
            .ToListAsync(ct);
        return TypedResults.Ok(liste);
    }

    private static async Task<Results<Ok<OutboxSatiri>, ProblemHttpResult>> YenidenDene(
        Guid id, KlinikDbContext db, IOlayYayinci yayinci, TimeProvider zaman, CancellationToken ct)
    {
        var paket = await db.OutboxMesajlari.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (paket is null)
            return TypedResults.Problem(detail: "Paket bulunamadı.", statusCode: StatusCodes.Status404NotFound);
        if (paket.Durum != OutboxDurumu.IsKuraliHatasi)
            return TypedResults.Problem(detail: "Yalnızca iş kuralı hatası veren paket yeniden denenebilir.",
                statusCode: StatusCodes.Status409Conflict);

        paket.Durum = OutboxDurumu.Bekliyor;
        paket.Hata = null;
        await db.SaveChangesAsync(ct);
        OutboxGonderici.Uyandir(zaman.GetUtcNow().UtcDateTime);
        await yayinci.YayinlaAsync(OlayTuru.OutboxGuncellendi, paket.Id, [Grup.Demo], ct);
        return TypedResults.Ok(new OutboxSatiri(paket.Id, paket.PaketTipi, paket.HastaTakmaAdi, paket.Durum, paket.Deneme, paket.Zaman, paket.Hata));
    }
}

/// <summary>Gönderim kutusu satırı (taslak 9). PaketTipi adıyla döner (HastaKayit/Muayene/LabSonuc); kodu istemci gösterir.</summary>
public sealed record OutboxSatiri(
    Guid Id, PaketTipi PaketTipi, string HastaTakmaAdi, OutboxDurumu Durum, int Deneme, DateTime Zaman, string? Hata);
