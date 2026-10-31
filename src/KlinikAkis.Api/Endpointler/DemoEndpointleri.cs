using System.Security.Claims;
using KlinikAkis.Api.Gercekzaman;
using KlinikAkis.Api.Kimlik;
using KlinikAkis.Domain;
using KlinikAkis.Infrastructure.Seed;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KlinikAkis.Api.Endpointler;

public static class DemoEndpointleri
{
    public static RouteGroupBuilder MapDemoEndpointleri(this RouteGroupBuilder api)
    {
        var demo = api.MapGroup("/demo").WithTags("Demo");

        demo.MapGet("/kimlikler", Kimlikler).WithName("DemoKimlikler")
            .WithSummary("Rol seçicide listelenecek kimlikler (6 senaryo hasta / hekimler / TV için bölümler)");

        demo.MapPost("/login", Giris).WithName("DemoGiris")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Rol + kimlik → 8 saatlik demo JWT (gerçek kimlik doğrulama yok, SPEC §14)");

        demo.MapPost("/reset", async (DemoSifirlayici sifirlayici, IOlayYayinci yayinci, CancellationToken ct) =>
            {
                var sonuc = await sifirlayici.SifirlaAsync(ct);
                // Açık tüm ekranlar eski id'leri tutuyor; yeniden yüklensinler.
                await yayinci.YayinlaAsync(OlayTuru.DemoSifirlandi, null, [Grup.Demo], ct);
                return TypedResults.Ok(sonuc);
            })
            .WithName("DemoSifirla")
            .WithSummary("Tüm veriyi seed durumuna döndürür; slotlar şimdiye göre yeniden üretilir");

        return api;
    }

    private static async Task<Results<Ok<List<KimlikOgesi>>, ValidationProblem>> Kimlikler(
        string rol, KlinikDbContext db, TimeProvider zaman, CancellationToken ct)
    {
        if (!DemoRol.Hepsi.Contains(rol, StringComparer.Ordinal))
            return RolHatasi();

        var simdi = zaman.GetUtcNow().UtcDateTime;
        List<KimlikOgesi> liste = rol switch
        {
            "hasta" => await SenaryoHastalari(db, simdi, ct),
            "doktor" => (await db.Hekimler.AsNoTracking()
                .Select(h => new { h.Ad, Oge = new KimlikOgesi(h.Id, h.Unvan + " " + h.Ad, h.Brans!.Ad + " · Oda " + h.Oda) })
                .ToListAsync(ct))
                .OrderBy(h => h.Ad, TurkceMetin.Siralama).Select(h => h.Oge).ToList(),
            "tv" => await TvBranslari(db, ct),
            _ => [], // kabul: kimlik seçimi yok, tek kabul ekranı
        };
        return TypedResults.Ok(liste);
    }

    private static async Task<Results<Ok<GirisYaniti>, ProblemHttpResult, ValidationProblem>> Giris(
        GirisIstegi istek, KlinikDbContext db, DemoTokenUretici uretici, CancellationToken ct)
    {
        if (!DemoRol.Hepsi.Contains(istek.Rol, StringComparer.Ordinal))
            return RolHatasi();
        if (istek.Rol != DemoRol.Kabul && istek.KimlikId is null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["kimlikId"] = ["Bu rol için kimlik seçilmeli."],
            });

        var id = istek.KimlikId.GetValueOrDefault();
        (string Ad, Claim[] Claimler)? kimlik = istek.Rol switch
        {
            DemoRol.Hasta => await db.Hastalar.Where(h => h.Id == id)
                .Select(h => new { h.Ad, h.Soyad }).FirstOrDefaultAsync(ct) is { } h
                ? (AdGosterimi.Kisa(h.Ad, h.Soyad), [new Claim(DemoClaim.HastaId, id.ToString())])
                : null,
            DemoRol.Doktor => await db.Hekimler.Where(x => x.Id == id)
                .Select(x => new { x.Unvan, x.Ad, x.BransId }).FirstOrDefaultAsync(ct) is { } d
                ? ($"{d.Unvan} {d.Ad}",
                    [new Claim(DemoClaim.HekimId, id.ToString()), new Claim(DemoClaim.BransId, d.BransId.ToString())])
                : null,
            // TV yalnızca hekimi olan bölümler için açılır (kimlikler listesiyle aynı kural).
            DemoRol.Tv => await db.Branslar.Where(b => b.Id == id && db.Hekimler.Any(x => x.BransId == b.Id))
                .Select(b => b.Ad).FirstOrDefaultAsync(ct) is { } bransAd
                ? ($"{bransAd} TV", [new Claim(DemoClaim.BransId, id.ToString())])
                : null,
            _ => ("Kabul görevlisi", []),
        };
        if (kimlik is not { } k)
            return TypedResults.Problem(detail: "Seçilen kimlik bulunamadı.", statusCode: StatusCodes.Status404NotFound);

        Claim[] claimler = [new(DemoClaim.Rol, istek.Rol), new(DemoClaim.Ad, k.Ad), .. k.Claimler];
        var (token, bitis) = uretici.Uret(claimler);
        var kimlikId = istek.Rol == DemoRol.Kabul ? (Guid?)null : id;
        return TypedResults.Ok(new GirisYaniti(token, istek.Rol, kimlikId, k.Ad, bitis));
    }

    private static ValidationProblem RolHatasi() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            ["rol"] = [$"Rol şunlardan biri olmalı: {string.Join(", ", DemoRol.Hepsi)}."],
        });

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
                .Select(h => new KimlikOgesi(h.Id, AdGosterimi.Kisa(h.Ad, h.Soyad),
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

public sealed record GirisIstegi(string Rol, Guid? KimlikId);

public sealed record GirisYaniti(string Token, string Rol, Guid? KimlikId, string Ad, DateTime Bitis);
