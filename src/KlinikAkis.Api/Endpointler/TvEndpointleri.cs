using System.Security.Claims;
using KlinikAkis.Api.Kimlik;
using KlinikAkis.Domain;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KlinikAkis.Api.Endpointler;

/// <summary>
/// TV sıra ekranı (SPEC taslak 8, §13.7b). Branş token'daki claim'den gelir, sorgudan değil: bir TV yalnızca kendi
/// bölümünün çağrılarını görür. Ad sunucuda baş harfe indirgenir ("A.K."); tam ad TV'ye hiç gitmez.
/// Canlılık: "cagir" geçişi HastaCagrildi olayını tv:{bransId} grubuna yayınlar, ekran bu ucu yeniden çeker.
/// </summary>
public static class TvEndpointleri
{
    public const int CagriSayisi = 3;

    public static RouteGroupBuilder MapTvEndpointleri(this RouteGroupBuilder api)
    {
        api.MapGet("/tv/cagrilar", Cagrilar).WithTags("TV").WithName("TvCagrilari").RequireAuthorization(DemoRol.Tv)
            .WithSummary("Bugünkü (İstanbul) son 3 çağrı, en yenisi ilk; branş token'dan");
        return api;
    }

    private static async Task<Ok<List<TvCagrisi>>> Cagrilar(
        ClaimsPrincipal kullanici, KlinikDbContext db, TimeProvider zaman, CancellationToken ct)
    {
        var bransId = Guid.Parse(kullanici.FindFirstValue(DemoClaim.BransId)!);
        var bugunBasi = IstanbulSaati.GunAraligi(IstanbulSaati.Bugun(zaman.GetUtcNow().UtcDateTime)).Baslangic;

        var satirlar = await db.Randevular.AsNoTracking()
            .Where(r => r.Hekim!.BransId == bransId && r.CagrilmaZamani >= bugunBasi)
            .OrderByDescending(r => r.CagrilmaZamani)
            .Take(CagriSayisi)
            .Select(r => new { r.SiraNo, r.Hekim!.Oda, r.Hasta!.Ad, r.Hasta.Soyad, Zaman = r.CagrilmaZamani!.Value })
            .ToListAsync(ct);
        return TypedResults.Ok(satirlar
            .Select(s => new TvCagrisi(s.SiraNo, s.Oda, AdGosterimi.BasHarfler(s.Ad, s.Soyad), s.Zaman))
            .ToList());
    }
}

/// <summary>TV'deki bir çağrı: sıra no, oda, baş harfler ("A.K."), çağrı anı (UTC).</summary>
public sealed record TvCagrisi(string SiraNo, string Oda, string BasHarfler, DateTime Zaman);
