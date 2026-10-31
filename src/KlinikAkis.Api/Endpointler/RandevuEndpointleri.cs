using System.Security.Claims;
using KlinikAkis.Api.Gercekzaman;
using KlinikAkis.Api.Kimlik;
using KlinikAkis.Domain;
using KlinikAkis.Domain.Varliklar;
using KlinikAkis.Infrastructure.Outbox;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KlinikAkis.Api.Endpointler;

/// <summary>Randevu alma, listeler ve durum geçişleri (SPEC §5.1, §7). Her değişiklik bir SignalR olayı yayınlar.</summary>
public static class RandevuEndpointleri
{
    private static readonly Dictionary<string, RandevuEylemi> Eylemler = new(StringComparer.Ordinal)
    {
        ["kabul"] = RandevuEylemi.Kabul,
        ["cagir"] = RandevuEylemi.Cagir,
        ["basla"] = RandevuEylemi.Basla,
        ["kapat"] = RandevuEylemi.Kapat,
        ["iptal"] = RandevuEylemi.Iptal,
        ["gelmedi"] = RandevuEylemi.Gelmedi,
    };

    public static RouteGroupBuilder MapRandevuEndpointleri(this RouteGroupBuilder api)
    {
        var randevular = api.MapGroup("/randevular").WithTags("Randevu");

        randevular.MapPost("", RandevuAl).WithName("RandevuAl").RequireAuthorization(DemoRol.Hasta)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Hasta boş bir slottan randevu alır; sıra no branş + gün içinde artar");

        randevular.MapGet("", Liste).WithName("RandevuListesi").RequireAuthorization(DemoPolitika.Personel)
            .ProducesValidationProblem()
            .WithSummary("Doktor: yalnızca kendi listesi (\"Ayşe K.\"). Kabul: tümü ya da hekimId ile süzülmüş (maskeli ad). Tarih yoksa bugün başından sonraki 24 saat");

        randevular.MapPost("/{id:guid}/{eylem}", DurumDegistir).WithName("RandevuDurumDegistir")
            .RequireAuthorization(DemoPolitika.Personel)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("kabul | iptal | gelmedi (kabul masası) · cagir | basla | kapat (randevunun hekimi). Geçersiz geçiş 409");

        api.MapGet("/hasta/me/randevular", HastaRandevulari).WithTags("Randevu").WithName("HastaRandevulari")
            .RequireAuthorization(DemoRol.Hasta)
            .WithSummary("Oturumdaki hastanın randevuları (çağrıldıysa oda bilgisiyle)");

        return api;
    }

    private static async Task<Results<Created<HastaRandevusu>, ProblemHttpResult, ValidationProblem>> RandevuAl(
        RandevuIstegi istek, ClaimsPrincipal kullanici, KlinikDbContext db, TimeProvider zaman,
        IOlayYayinci yayinci, CancellationToken ct)
    {
        var hastaId = Guid.Parse(kullanici.FindFirstValue(DemoClaim.HastaId)!);
        var simdi = zaman.GetUtcNow().UtcDateTime;

        var slot = await db.Slotlar.Include(s => s.Hekim).ThenInclude(h => h!.Brans)
            .FirstOrDefaultAsync(s => s.Id == istek.SlotId, ct);
        if (slot is null)
            return TypedResults.Problem(detail: "Seçilen saat bulunamadı.", statusCode: StatusCodes.Status404NotFound);
        if (slot.Dolu || slot.Baslangic <= simdi)
            return SaatDoldu();

        OnAnamnez? onAnamnez = null;
        if (istek.OnAnamnezId is { } onAnamnezId)
        {
            onAnamnez = await db.OnAnamnezler.FirstOrDefaultAsync(o => o.Id == onAnamnezId && o.HastaId == hastaId, ct);
            if (onAnamnez is null)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["onAnamnezId"] = ["Bu şikayet kaydı size ait değil."],
                });
            // Kırmızı bayrakta akış sunucuda da durur: arayüz atlatılsa bile randevu verilmez, 112 yönlendirmesi geçerli.
            if (onAnamnez.Durum == OnAnamnezDurumu.KirmiziBayrak)
                return TypedResults.Problem(detail: KirmiziBayrakMotoru.AcilMesaji, statusCode: StatusCodes.Status409Conflict);
            // Yarım sohbetle randevu alınıp tarama adımı sonradan atlanmasın; bir ön anamnez tek randevuya bağlanır.
            if (onAnamnez.Durum == OnAnamnezDurumu.Sohbette)
                return TypedResults.Problem(detail: "Şikayet sohbeti tamamlanmadan randevu alınamaz.",
                    statusCode: StatusCodes.Status409Conflict);
            if (onAnamnez.RandevuId is not null)
                return TypedResults.Problem(detail: "Bu şikayet kaydıyla zaten randevu alındı.",
                    statusCode: StatusCodes.Status409Conflict);
        }

        var hekim = slot.Hekim!;
        var (gunBasi, gunSonu) = IstanbulSaati.GunAraligi(IstanbulSaati.Bugun(slot.Baslangic));
        var gunIciSiralar = await db.Randevular
            .Where(r => r.Hekim!.BransId == hekim.BransId && r.Slot!.Baslangic >= gunBasi && r.Slot.Baslangic < gunSonu)
            .Select(r => r.SiraNo).ToListAsync(ct);

        var randevu = new Randevu
        {
            Id = Guid.NewGuid(),
            HastaId = hastaId,
            HekimId = hekim.Id,
            SlotId = slot.Id,
            SiraNo = SiraNo.Olustur(gunIciSiralar.Select(SiraNo.Sira).DefaultIfEmpty(0).Max() + 1),
            OnAnamnezId = istek.OnAnamnezId,
        };
        slot.Dolu = true;
        onAnamnez?.RandevuId = randevu.Id;
        db.Randevular.Add(randevu);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Aynı slota eşzamanlı istek: SlotId benzersiz indeksi ikinciyi reddeder. Başka bir kısıt hatası
            // "saat doldu" diye örtülmesin: slotta gerçekten etkin randevu yoksa hata olduğu gibi yükselir.
            db.ChangeTracker.Clear();
            if (await db.Randevular.AnyAsync(r => r.SlotId == slot.Id && r.Durum != RandevuDurumu.Iptal, ct))
                return SaatDoldu();
            throw;
        }

        await yayinci.YayinlaAsync(OlayTuru.RandevuAlindi, randevu.Id,
            [Grup.Doktor(hekim.Id), Grup.Kabul, Grup.Hasta(hastaId)], ct);

        return TypedResults.Created($"/api/hasta/me/randevular",
            new HastaRandevusu(randevu.Id, slot.Baslangic, randevu.SiraNo, randevu.Durum,
                hekim.Brans!.Ad, $"{hekim.Unvan} {hekim.Ad}", hekim.Oda));
    }

    private static ProblemHttpResult SaatDoldu() =>
        TypedResults.Problem(detail: "Bu saat artık uygun değil; başka bir saat seçin.", statusCode: StatusCodes.Status409Conflict);

    private static async Task<Results<Ok<List<RandevuSatiri>>, ValidationProblem>> Liste(
        string? tarih, Guid? hekimId, ClaimsPrincipal kullanici, KlinikDbContext db, TimeProvider zaman, CancellationToken ct)
    {
        var simdi = zaman.GetUtcNow().UtcDateTime;
        DateTime bas, bit;
        if (string.IsNullOrEmpty(tarih))
        {
            // Gece demosunda gece yarısını geçen slotlar da listede kalsın: bugün başı → şimdi + 24 saat.
            bas = IstanbulSaati.GunAraligi(IstanbulSaati.Bugun(simdi)).Baslangic;
            bit = simdi.AddHours(24);
        }
        else if (DateOnly.TryParseExact(tarih, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                     System.Globalization.DateTimeStyles.None, out var gun))
        {
            (bas, bit) = IstanbulSaati.GunAraligi(gun);
        }
        else
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["tarih"] = ["Tarih yyyy-MM-dd biçiminde olmalı (ör. 2026-10-29)."],
            });
        }

        var sorgu = SatirSorgusu(db).Where(r => r.Slot!.Baslangic >= bas && r.Slot.Baslangic < bit);
        var rol = kullanici.FindFirstValue(DemoClaim.Rol)!;
        // Doktor için hekim token'dan gelir (sorgudaki hekimId yok sayılır); kabul isterse süzer.
        var suzulecekHekim = rol == DemoRol.Doktor ? Guid.Parse(kullanici.FindFirstValue(DemoClaim.HekimId)!) : hekimId;
        if (suzulecekHekim is { } hid)
            sorgu = sorgu.Where(r => r.HekimId == hid);

        var liste = await sorgu.OrderBy(r => r.Slot!.Baslangic).ThenBy(r => r.SiraNo).ToListAsync(ct);
        var takipNolari = await TakipNolari(db, liste.Select(r => r.Id), ct);
        var muayeneler = await MuayeneIdleri(db, liste.Select(r => r.Id), ct);
        return TypedResults.Ok(liste.Select(r =>
            Satir(r, rol, takipNolari.GetValueOrDefault(r.Id), muayeneler.GetValueOrDefault(r.Id))).ToList());
    }

    private static async Task<Results<Ok<RandevuSatiri>, ProblemHttpResult>> DurumDegistir(
        Guid id, string eylem, ClaimsPrincipal kullanici, KlinikDbContext db, TimeProvider zaman,
        IOlayYayinci yayinci, CancellationToken ct)
    {
        if (!Eylemler.TryGetValue(eylem, out var e))
            return TypedResults.Problem(detail: "Bilinmeyen işlem.", statusCode: StatusCodes.Status404NotFound);

        var rol = kullanici.FindFirstValue(DemoClaim.Rol)!;
        var randevu = await SatirSorgusu(db).AsTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (randevu is null)
            return TypedResults.Problem(detail: "Randevu bulunamadı.", statusCode: StatusCodes.Status404NotFound);

        // Kim yapabilir: kabul eylemleri kabul masası, muayene eylemleri yalnızca randevunun hekimi.
        var yetkili = RandevuDurumMakinesi.DoktorEylemiMi(e)
            ? rol == DemoRol.Doktor && kullanici.FindFirstValue(DemoClaim.HekimId) == randevu.HekimId.ToString()
            : rol == DemoRol.Kabul;
        if (!yetkili)
            return TypedResults.Problem(detail: "Bu işlem için yetkiniz yok.", statusCode: StatusCodes.Status403Forbidden);

        try
        {
            randevu.Durum = RandevuDurumMakinesi.Gecis(randevu.Durum, e);
        }
        catch (GecersizDurumGecisiException hata)
        {
            return TypedResults.Problem(detail: hata.Message, statusCode: StatusCodes.Status409Conflict);
        }

        var simdi = zaman.GetUtcNow().UtcDateTime;
        switch (e)
        {
            case RandevuEylemi.Kabul:
                var kabul = new Kabul
                {
                    Id = Guid.NewGuid(),
                    RandevuId = randevu.Id,
                    KabulZamani = simdi,
                    TakipNo = TakipNo.Olustur(await db.Kabuller.CountAsync(ct) + 1),
                    KabulEden = kullanici.FindFirstValue(DemoClaim.Ad) ?? "",
                };
                db.Kabuller.Add(kabul);
                // e-Nabız 101 Hasta Kayıt (simülasyon, SPEC §5.2): kabulle aynı kayıtta; kimlik yerine takma ad.
                db.OutboxMesajlari.Add(OutboxGonderici.Paket(PaketTipi.HastaKayit, randevu.Hasta!.TakmaAd,
                    new { takipNo = kabul.TakipNo, brans = randevu.Hekim!.Brans!.Kod.ToString(), kabulZamani = simdi }, simdi));
                break;
            case RandevuEylemi.Cagir:
                randevu.CagrilmaZamani = simdi;
                break;
            case RandevuEylemi.Basla:
                db.Muayeneler.Add(new Muayene { Id = Guid.NewGuid(), RandevuId = randevu.Id, BaslangicZamani = simdi });
                break;
            case RandevuEylemi.Kapat:
                var muayene = await db.Muayeneler.FirstAsync(m => m.RandevuId == randevu.Id, ct);
                muayene.Durum = MuayeneDurumu.Kapali;
                // e-Nabız 103 Muayene (simülasyon): epikriz onaylı mı bilgisi taşınır, epikriz metni gitmez.
                var epikrizOnayli = await db.Epikrizler.AnyAsync(x => x.MuayeneId == muayene.Id && x.Durum == EpikrizDurumu.Onaylandi, ct);
                var takipNo = await db.Kabuller.Where(k => k.RandevuId == randevu.Id).Select(k => k.TakipNo).FirstOrDefaultAsync(ct);
                db.OutboxMesajlari.Add(OutboxGonderici.Paket(PaketTipi.Muayene, randevu.Hasta!.TakmaAd,
                    new { takipNo, brans = randevu.Hekim!.Brans!.Kod.ToString(), muayeneZamani = muayene.BaslangicZamani, epikrizOnayli, tanilar = muayene.Tanilar },
                    simdi));
                break;
            case RandevuEylemi.Iptal:
                randevu.Slot!.Dolu = false; // iptal edilen saat yeniden alınabilir
                break;
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Durum eşzamanlılık belirteci: aynı randevuya iki sekmeden aynı anda işlem (çift kabul, kabul+iptal)
            // ya da eşzamanlı iki kabulde aynı takip no. İkincisi reddedilir; liste yenilenince güncel durum görünür.
            return TypedResults.Problem(detail: "Randevu bu arada değişti; liste yenilendi, yeniden deneyin.",
                statusCode: StatusCodes.Status409Conflict);
        }

        List<string> gruplar = [Grup.Doktor(randevu.HekimId), Grup.Kabul, Grup.Hasta(randevu.HastaId)];
        var tur = e switch
        {
            RandevuEylemi.Kabul => OlayTuru.KabulYapildi,
            RandevuEylemi.Cagir => OlayTuru.HastaCagrildi,
            _ => OlayTuru.RandevuDurumDegisti,
        };
        if (e == RandevuEylemi.Cagir)
            gruplar.Add(Grup.Tv(randevu.Hekim!.BransId));
        await yayinci.YayinlaAsync(tur, randevu.Id, gruplar, ct);

        var takipNolari = await TakipNolari(db, [randevu.Id], ct);
        var muayeneIdleri = await MuayeneIdleri(db, [randevu.Id], ct);
        return TypedResults.Ok(Satir(randevu, rol, takipNolari.GetValueOrDefault(randevu.Id), muayeneIdleri.GetValueOrDefault(randevu.Id)));
    }

    private static async Task<Ok<List<HastaRandevusu>>> HastaRandevulari(
        ClaimsPrincipal kullanici, KlinikDbContext db, TimeProvider zaman, CancellationToken ct)
    {
        var hastaId = Guid.Parse(kullanici.FindFirstValue(DemoClaim.HastaId)!);
        var bugunBasi = IstanbulSaati.GunAraligi(IstanbulSaati.Bugun(zaman.GetUtcNow().UtcDateTime)).Baslangic;

        var liste = await db.Randevular.AsNoTracking()
            .Where(r => r.HastaId == hastaId && r.Slot!.Baslangic >= bugunBasi)
            .OrderBy(r => r.Slot!.Baslangic)
            .Select(r => new HastaRandevusu(r.Id, r.Slot!.Baslangic, r.SiraNo, r.Durum,
                r.Hekim!.Brans!.Ad, r.Hekim.Unvan + " " + r.Hekim.Ad, r.Hekim.Oda))
            .ToListAsync(ct);
        return TypedResults.Ok(liste);
    }

    private static IQueryable<Randevu> SatirSorgusu(KlinikDbContext db) =>
        db.Randevular.AsNoTracking()
            .Include(r => r.Hasta).Include(r => r.Slot).Include(r => r.Hekim).ThenInclude(h => h!.Brans);

    private static async Task<Dictionary<Guid, string>> TakipNolari(KlinikDbContext db, IEnumerable<Guid> randevuIdleri, CancellationToken ct)
    {
        var idler = randevuIdleri.ToList();
        return await db.Kabuller.AsNoTracking().Where(k => idler.Contains(k.RandevuId))
            .ToDictionaryAsync(k => k.RandevuId, k => k.TakipNo, ct);
    }

    private static async Task<Dictionary<Guid, Guid?>> MuayeneIdleri(KlinikDbContext db, IEnumerable<Guid> randevuIdleri, CancellationToken ct)
    {
        var idler = randevuIdleri.ToList();
        return await db.Muayeneler.AsNoTracking().Where(m => idler.Contains(m.RandevuId))
            .ToDictionaryAsync(m => m.RandevuId, m => (Guid?)m.Id, ct);
    }

    private static RandevuSatiri Satir(Randevu r, string rol, string? takipNo, Guid? muayeneId)
    {
        var h = r.Hasta!;
        // Kabul masası tam adı görmez (taslak 7): maskeleme sunucuda, tam ad istemciye hiç gitmez.
        var ad = rol == DemoRol.Kabul ? AdGosterimi.Maskeli(h.Ad, h.Soyad) : AdGosterimi.Kisa(h.Ad, h.Soyad);
        var doktorMu = rol == DemoRol.Doktor;
        var izinli = RandevuDurumMakinesi.IzinliEylemler(r.Durum)
            .Where(e => RandevuDurumMakinesi.DoktorEylemiMi(e) == doktorMu)
            .ToList();
        return new RandevuSatiri(r.Id, r.Slot!.Baslangic, r.SiraNo, r.Durum, r.HastaId, ad,
            r.Hekim!.Brans!.Ad, $"{r.Hekim.Unvan} {r.Hekim.Ad}", r.Hekim.Oda, h.AcikRizaAlindi, takipNo, izinli, r.OnAnamnezId, muayeneId);
    }
}

public sealed record RandevuIstegi(Guid SlotId, Guid? OnAnamnezId);

public sealed record HastaRandevusu(
    Guid Id, DateTime Baslangic, string SiraNo, RandevuDurumu Durum, string BransAd, string HekimAd, string Oda);

public sealed record RandevuSatiri(
    Guid Id, DateTime Baslangic, string SiraNo, RandevuDurumu Durum, Guid HastaId, string HastaAd,
    string BransAd, string HekimAd, string Oda, bool AcikRizaAlindi, string? TakipNo,
    IReadOnlyList<RandevuEylemi> IzinliEylemler, Guid? OnAnamnezId, Guid? MuayeneId);
