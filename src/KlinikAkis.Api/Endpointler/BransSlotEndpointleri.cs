using System.Globalization;
using KlinikAkis.Domain;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KlinikAkis.Api.Endpointler;

public static class BransSlotEndpointleri
{
    public static RouteGroupBuilder MapBransSlotEndpointleri(this RouteGroupBuilder api)
    {
        api.MapGet("/branslar", Branslar).WithTags("Bölüm ve slot").WithName("Branslar")
            .WithSummary("Bölüm listesi (hekimi olmayan bölümler de döner, hekimSayisi = 0)");

        api.MapGet("/slotlar", Slotlar).WithTags("Bölüm ve slot").WithName("Slotlar")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesValidationProblem()
            .WithSummary("Bölümdeki boş ve gelecekteki saatler; tarih (yyyy-MM-dd, İstanbul) verilmezse sonraki tüm slotlar");

        return api;
    }

    private static async Task<Ok<List<BransOgesi>>> Branslar(KlinikDbContext db, CancellationToken ct)
    {
        var branslar = await db.Branslar.AsNoTracking()
            .Select(b => new BransOgesi(b.Id, b.Kod, b.Ad, db.Hekimler.Count(h => h.BransId == b.Id)))
            .ToListAsync(ct);
        // Sıra: BransKodu enum sırası (sık kullanılan bölümler önde); sunucuda kültüre bağlı sıralama yok.
        return TypedResults.Ok(branslar.OrderBy(b => b.Kod).ToList());
    }

    private static async Task<Results<Ok<List<SlotOgesi>>, ProblemHttpResult, ValidationProblem>> Slotlar(
        Guid bransId, string? tarih, KlinikDbContext db, TimeProvider zaman, CancellationToken ct)
    {
        // Yalnızca yyyy-MM-dd: "10/11/2026" gibi kültüre göre iki anlamlı biçimler kabul edilmez.
        DateOnly? gun = null;
        if (!string.IsNullOrEmpty(tarih))
        {
            if (!DateOnly.TryParseExact(tarih, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ayristirilan))
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["tarih"] = ["Tarih yyyy-MM-dd biçiminde olmalı (ör. 2026-10-29)."],
                });
            gun = ayristirilan;
        }

        if (!await db.Branslar.AnyAsync(b => b.Id == bransId, ct))
            return TypedResults.Problem(detail: "Bölüm bulunamadı.", statusCode: StatusCodes.Status404NotFound);

        var simdi = zaman.GetUtcNow().UtcDateTime;
        var sorgu = db.Slotlar.AsNoTracking()
            .Where(s => s.Hekim!.BransId == bransId && !s.Dolu && s.Baslangic > simdi);

        if (gun is { } secilenGun)
        {
            var (baslangic, bitis) = IstanbulSaati.GunAraligi(secilenGun);
            sorgu = sorgu.Where(s => s.Baslangic >= baslangic && s.Baslangic < bitis);
        }

        var slotlar = await sorgu
            .OrderBy(s => s.Baslangic).ThenBy(s => s.Hekim!.Ad)
            .Select(s => new SlotOgesi(s.Id, s.Baslangic, s.SureDk, s.HekimId, s.Hekim!.Unvan + " " + s.Hekim.Ad, s.Hekim.Oda))
            .ToListAsync(ct);
        return TypedResults.Ok(slotlar);
    }
}

public sealed record BransOgesi(Guid Id, BransKodu Kod, string Ad, int HekimSayisi);

public sealed record SlotOgesi(Guid Id, DateTime Baslangic, int SureDk, Guid HekimId, string HekimAd, string Oda);
