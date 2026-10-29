using KlinikAkis.Domain;
using KlinikAkis.Infrastructure.Seed;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KlinikAkis.Api.Endpointler;

public static class DemoEndpointleri
{
    private static readonly string[] Roller = ["hasta", "doktor", "kabul", "tv"];

    public static RouteGroupBuilder MapDemoEndpointleri(this RouteGroupBuilder api)
    {
        var demo = api.MapGroup("/demo").WithTags("Demo");

        demo.MapGet("/kimlikler", Kimlikler).WithName("DemoKimlikler")
            .WithSummary("Rol seçicide listelenecek kimlikler (6 senaryo hasta / hekimler / TV için bölümler)");

        demo.MapPost("/reset", async (DemoSifirlayici sifirlayici, CancellationToken ct) =>
                TypedResults.Ok(await sifirlayici.SifirlaAsync(ct)))
            .WithName("DemoSifirla")
            .WithSummary("Tüm veriyi seed durumuna döndürür; slotlar şimdiye göre yeniden üretilir");

        return api;
    }

    private static async Task<Results<Ok<List<KimlikOgesi>>, ValidationProblem>> Kimlikler(
        string rol, KlinikDbContext db, TimeProvider zaman, CancellationToken ct)
    {
        if (!Roller.Contains(rol, StringComparer.Ordinal))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["rol"] = [$"Rol şunlardan biri olmalı: {string.Join(", ", Roller)}."],
            });

        var simdi = zaman.GetUtcNow().UtcDateTime;
        List<KimlikOgesi> liste = rol switch
        {
            "hasta" => await SenaryoHastalari(db, simdi, ct),
            "doktor" => await db.Hekimler.AsNoTracking()
                .OrderBy(h => h.Ad)
                .Select(h => new KimlikOgesi(h.Id, h.Unvan + " " + h.Ad, h.Brans!.Ad + " · Oda " + h.Oda))
                .ToListAsync(ct),
            "tv" => await TvBranslari(db, ct),
            _ => [], // kabul: kimlik seçimi yok, tek kabul ekranı
        };
        return TypedResults.Ok(liste);
    }

    private static async Task<List<KimlikOgesi>> SenaryoHastalari(KlinikDbContext db, DateTime simdi, CancellationToken ct)
    {
        var hastalar = await db.Hastalar.AsNoTracking()
            .Where(h => SenaryoIdleri.Hastalar.Contains(h.Id))
            .ToListAsync(ct);
        // Senaryo sırasını koru (Ayşe ilk).
        return
        [
            .. SenaryoIdleri.Hastalar
                .Select(id => hastalar.FirstOrDefault(h => h.Id == id))
                .OfType<Domain.Varliklar.Hasta>()
                .Select(h => new KimlikOgesi(h.Id, $"{h.Ad} {h.Soyad[..1]}.",
                    $"{IstanbulSaati.Yas(h.DogumTarihi, simdi)} · {h.Cinsiyet}")),
        ];
    }

    private static async Task<List<KimlikOgesi>> TvBranslari(KlinikDbContext db, CancellationToken ct)
    {
        var branslar = await db.Branslar.AsNoTracking()
            .Where(b => db.Hekimler.Any(h => h.BransId == b.Id))
            .ToListAsync(ct);
        return [.. branslar.OrderBy(b => b.Kod).Select(b => new KimlikOgesi(b.Id, b.Ad, ""))];
    }
}

public sealed record KimlikOgesi(Guid Id, string Ad, string Aciklama);
