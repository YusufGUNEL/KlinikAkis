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
/// Muayene: hekim notu, epikriz (SPEC §13.7a) ve ICD-10 önerisi (§9.10). Epikriz yalnızca AiHatti üzerinden üretilir; AI cümleleri taslaktır,
/// alerji ve tahlil satırları kodda kurulur. Onaysız AI çıktısı (ön anamnez taslağı) epikrize girmez; epikriz de hekim
/// onaylamadan kayıt sayılmaz. Yazma işlemlerini yalnızca randevunun hekimi yapar.
/// </summary>
public static class MuayeneEndpointleri
{
    public const int MetinSiniri = 2000;
    public const int EnFazlaTani = 5;
    private const string TaniKapaliMetni = "Vizit kapandı; tanı kodları değiştirilemez.";

    public static RouteGroupBuilder MapMuayeneEndpointleri(this RouteGroupBuilder api)
    {
        var muayeneler = api.MapGroup("/muayeneler").WithTags("Muayene").RequireAuthorization(DemoRol.Doktor);

        muayeneler.MapGet("/{id:guid}", Detay).WithName("MuayeneDetayi").ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Muayene durumu, hekim notu ve son epikriz taslağı (kart verisi)");

        muayeneler.MapPut("/{id:guid}/not", NotKaydet).WithName("MuayeneNotu")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesValidationProblem()
            .WithSummary("Hekim notunu kaydeder (randevunun hekimi, açık muayene)");

        muayeneler.MapPost("/{id:guid}/epikriz", EpikrizUret).WithName("EpikrizTaslagi")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(AiHizSiniri.Politika)
            .WithSummary("Epikriz taslağı: hekim notu + onaylı ön anamnez + tahliller (maskeli) → model / yedek. " +
                "Bekleyen taslak varsa model yeniden çağrılmaz. Denetim kaydı yazar");

        api.MapPost("/epikriz/{id:guid}/karar", Karar).WithTags("Muayene").WithName("EpikrizKarar")
            .RequireAuthorization(DemoRol.Doktor)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesValidationProblem()
            .WithSummary("Hekim kararı: onayla / duzenle (metinle) / reddet. Erişim kaydı, EpikrizOnaylandi olayı");

        muayeneler.MapPost("/{id:guid}/tani-onerisi", TaniOnerisiUret).WithName("TaniOnerisi")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(AiHizSiniri.Politika)
            .WithSummary("ICD-10 önerisi: epikrizle aynı maskeli girdi → model kapalı listeden 1-3 kod seçer / yedek. " +
                "Bekleyen öneri varsa model yeniden çağrılmaz. Denetim kaydı yazar");

        api.MapPost("/tani-onerisi/{id:guid}/karar", TaniKarar).WithTags("Muayene").WithName("TaniOnerisiKarar")
            .RequireAuthorization(DemoRol.Doktor)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesValidationProblem()
            .WithSummary("Hekim kararı: onayla (önerilen kodlar) / duzenle (kodlar, kapalı listeden) / reddet. " +
                "Onaylanan kodlar muayenenin tanılarına yazılır; erişim kaydı");

        api.MapGet("/icd10", () => TypedResults.Ok(Icd10Listesi.Tum)).WithTags("Muayene").WithName("Icd10Listesi")
            .RequireAuthorization(DemoRol.Doktor)
            .WithSummary("ICD-10 kapalı listesi (DSÖ ICD-10 2019); Düzenle seçimi ve öneri doğrulaması aynı listeyi kullanır");

        return api;
    }

    private static async Task<Results<Ok<MuayeneDetayi>, ProblemHttpResult>> Detay(Guid id, KlinikDbContext db, CancellationToken ct)
    {
        var muayene = await db.Muayeneler.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        return muayene is null ? Bulunamadi() : TypedResults.Ok(await DetayAsync(muayene, db, ct));
    }

    private static async Task<Results<Ok<MuayeneDetayi>, ProblemHttpResult, ValidationProblem>> NotKaydet(
        Guid id, NotIstegi istek, ClaimsPrincipal kullanici, KlinikDbContext db, CancellationToken ct)
    {
        var (muayene, _, sorun) = await YukleAsync(id, kullanici, db, ct);
        if (sorun is not null) return sorun;
        if (muayene!.Durum == MuayeneDurumu.Kapali)
            return VizitKapali("Vizit kapandı; not değiştirilemez.");

        var metin = string.IsNullOrWhiteSpace(istek.Metin) ? null : istek.Metin.Trim();
        if (metin is { Length: > MetinSiniri })
            return Gecersiz("metin", $"Not en fazla {MetinSiniri} karakter olabilir.");
        muayene.HekimNotu = metin;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return VizitKapali("Vizit kapandı; not değiştirilemez.");
        }
        return TypedResults.Ok(await DetayAsync(muayene, db, ct));
    }

    private static async Task<Results<Ok<MuayeneDetayi>, ProblemHttpResult>> EpikrizUret(
        Guid id, ClaimsPrincipal kullanici, KlinikDbContext db, AiHatti hatti, TimeProvider zaman, CancellationToken ct)
    {
        var (muayene, randevu, sorun) = await YukleAsync(id, kullanici, db, ct);
        if (sorun is not null) return sorun;
        if (muayene!.Durum == MuayeneDurumu.Kapali) return VizitKapali();

        // Bekleyen ya da onaylanmış taslak varken model yeniden çağrılmaz (maliyet/kota); ret sonrası yenisi üretilir.
        var son = await SonEpikrizAsync(db, muayene!.Id, ct);
        if (son is not null && son.Durum != EpikrizDurumu.Reddedildi)
            return TypedResults.Ok(await DetayAsync(muayene, db, ct));

        var hasta = await db.Hastalar.AsNoTracking().FirstAsync(h => h.Id == randevu!.HastaId, ct);
        var girdi = await GirdiAsync(db, hasta, randevu!, muayene, zaman.GetUtcNow().UtcDateTime, ct);
        var ai = await hatti.EpikrizAsync(hasta, girdi, ct);
        var yeni = db.Epikrizler.Add(new Epikriz
        {
            Id = Guid.NewGuid(),
            MuayeneId = muayene.Id,
            AiTaslak = ai.Taslak.JsonOlarak(),
            Kaynak = ai.Kaynak,
            AiModel = ai.Model,
            AiZamani = zaman.GetUtcNow().UtcDateTime,
            KotaDoldu = ai.KotaDoldu,
        });
        // Model çağrıldıktan sonra iptal edilmez: denetim kaydı ve taslak kaybolmasın.
        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException)
        {
            // Eşzamanlı başka bir istek taslağı yazdı (muayene başına tek reddedilmemiş epikriz indeksi):
            // bu taslak atılır, denetim kaydı yine yazılır, mevcut taslak döner.
            yeni.State = EntityState.Detached;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        return TypedResults.Ok(await DetayAsync(muayene, db, ct));
    }

    private static async Task<Results<Ok<MuayeneDetayi>, ProblemHttpResult, ValidationProblem>> Karar(
        Guid id, KararIstegi istek, ClaimsPrincipal kullanici, KlinikDbContext db, TimeProvider zaman,
        IOlayYayinci yayinci, CancellationToken ct)
    {
        var epikriz = await db.Epikrizler.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (epikriz is null)
            return TypedResults.Problem(detail: "Epikriz bulunamadı.", statusCode: StatusCodes.Status404NotFound);
        var (muayene, randevu, sorun) = await YukleAsync(epikriz.MuayeneId, kullanici, db, ct);
        if (sorun is not null) return sorun;
        // 103 paketi kapanışta epikrizOnayli ile gider; sonradan verilen karar pakete yansımaz, o yüzden kapalı vizitte karar yok.
        if (muayene!.Durum == MuayeneDurumu.Kapali) return VizitKapali();
        if (epikriz.Durum != EpikrizDurumu.Taslak)
            return TypedResults.Problem(detail: "Bu epikriz için karar zaten verildi.", statusCode: StatusCodes.Status409Conflict);

        var metin = string.IsNullOrWhiteSpace(istek.Metin) ? null : istek.Metin.Trim();
        (EpikrizDurumu Durum, string Eylem)? sonuc = istek.Karar switch
        {
            "onayla" => (EpikrizDurumu.Onaylandi, "Epikrizi onayladı"),
            "duzenle" => (EpikrizDurumu.Onaylandi, "Epikrizi düzenleyip onayladı"),
            "reddet" => (EpikrizDurumu.Reddedildi, "Epikrizi reddetti"),
            _ => null,
        };
        if (sonuc is not { } k)
            return Gecersiz("karar", "Karar 'onayla', 'duzenle' ya da 'reddet' olmalı.");
        if (istek.Karar == "duzenle" && metin is null)
            return Gecersiz("metin", "Düzenlenmiş epikrizi yazın.");
        if (metin is { Length: > MetinSiniri * 2 })
            return Gecersiz("metin", $"Metin en fazla {MetinSiniri * 2} karakter olabilir.");

        var simdi = zaman.GetUtcNow().UtcDateTime;
        var hekimId = Guid.Parse(kullanici.FindFirstValue(DemoClaim.HekimId)!);
        epikriz.Durum = k.Durum;
        epikriz.SonMetin = istek.Karar == "duzenle" ? metin : null;
        epikriz.OnaylayanHekimId = hekimId;
        epikriz.OnayZamani = simdi;
        db.ErisimKayitlari.Add(new ErisimKaydi
        {
            Id = Guid.NewGuid(),
            Zaman = simdi,
            KullaniciRol = kullanici.FindFirstValue(DemoClaim.Rol) ?? "",
            KullaniciAd = kullanici.FindFirstValue(DemoClaim.Ad) ?? "",
            HastaId = randevu!.HastaId,
            Eylem = k.Eylem,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.Problem(detail: "Bu epikriz için karar zaten verildi.", statusCode: StatusCodes.Status409Conflict);
        }

        if (k.Durum == EpikrizDurumu.Onaylandi)
            await yayinci.YayinlaAsync(OlayTuru.EpikrizOnaylandi, epikriz.Id, [Grup.Doktor(hekimId)], ct);
        return TypedResults.Ok(await DetayAsync(muayene!, db, ct));
    }

    private static async Task<Results<Ok<MuayeneDetayi>, ProblemHttpResult>> TaniOnerisiUret(
        Guid id, ClaimsPrincipal kullanici, KlinikDbContext db, AiHatti hatti, TimeProvider zaman, CancellationToken ct)
    {
        var (muayene, randevu, sorun) = await YukleAsync(id, kullanici, db, ct);
        if (sorun is not null) return sorun;
        if (muayene!.Durum == MuayeneDurumu.Kapali) return VizitKapali(TaniKapaliMetni);

        // Bekleyen ya da onaylanmış öneri varken model yeniden çağrılmaz; ret sonrası yenisi üretilir.
        var son = await SonTaniOnerisiAsync(db, muayene.Id, ct);
        if (son is not null && son.Durum != TaniOnerisiDurumu.Reddedildi)
            return TypedResults.Ok(await DetayAsync(muayene, db, ct));

        var hasta = await db.Hastalar.AsNoTracking().FirstAsync(h => h.Id == randevu!.HastaId, ct);
        var girdi = await GirdiAsync(db, hasta, randevu!, muayene, zaman.GetUtcNow().UtcDateTime, ct);
        var ai = await hatti.TaniOnerisiAsync(hasta, girdi, ct);
        var yeni = db.TaniOnerileri.Add(new TaniOnerisi
        {
            Id = Guid.NewGuid(),
            MuayeneId = muayene.Id,
            OneriJson = ai.Oneri.JsonOlarak(),
            Kaynak = ai.Kaynak,
            AiModel = ai.Model,
            AiZamani = zaman.GetUtcNow().UtcDateTime,
            KotaDoldu = ai.KotaDoldu,
        });
        // Epikrizle aynı: model çağrıldıktan sonra iptal edilmez; eşzamanlı ikinci öneri atılır, denetim kaydı kalır.
        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException)
        {
            yeni.State = EntityState.Detached;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        return TypedResults.Ok(await DetayAsync(muayene, db, ct));
    }

    private static async Task<Results<Ok<MuayeneDetayi>, ProblemHttpResult, ValidationProblem>> TaniKarar(
        Guid id, TaniKararIstegi istek, ClaimsPrincipal kullanici, KlinikDbContext db, TimeProvider zaman, CancellationToken ct)
    {
        var oneri = await db.TaniOnerileri.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (oneri is null)
            return TypedResults.Problem(detail: "ICD-10 önerisi bulunamadı.", statusCode: StatusCodes.Status404NotFound);
        var (muayene, randevu, sorun) = await YukleAsync(oneri.MuayeneId, kullanici, db, ct);
        if (sorun is not null) return sorun;
        // 103 paketi kapanışta tanılarla gider; sonradan verilen karar pakete yansımaz.
        if (muayene!.Durum == MuayeneDurumu.Kapali) return VizitKapali(TaniKapaliMetni);
        if (oneri.Durum != TaniOnerisiDurumu.Taslak)
            return TypedResults.Problem(detail: "Bu öneri için karar zaten verildi.", statusCode: StatusCodes.Status409Conflict);

        var onerilen = TaniOnerisiYaniti.Oku(oneri.OneriJson).Oneriler.Select(m => m.Kod).ToList();
        List<string> kodlar;
        string eylem;
        switch (istek.Karar)
        {
            case "onayla":
                if (onerilen.Count == 0)
                    return Gecersiz("karar", "Önerilen kod yok; Düzenle ile listeden seçin.");
                (kodlar, eylem) = (onerilen, "ICD-10 önerisini onayladı");
                break;
            case "duzenle":
                var secilen = (istek.Kodlar ?? []).Select(k => k?.Trim() ?? "").Distinct(StringComparer.Ordinal).ToList();
                if (secilen.Count == 0)
                    return Gecersiz("kodlar", "En az bir kod seçin.");
                if (secilen.Count > EnFazlaTani)
                    return Gecersiz("kodlar", $"En fazla {EnFazlaTani} kod seçilebilir.");
                if (secilen.FirstOrDefault(k => !Icd10Listesi.Var(k)) is { } yok)
                    return Gecersiz("kodlar", $"'{yok}' kapalı listede yok.");
                // Önerinin aynısı seçildiyse düzenleme yoktur; arayüzdeki "onayladı / düzenleyip onayladı" ile aynı ayrım.
                var ayni = secilen.Order(StringComparer.Ordinal).SequenceEqual(onerilen.Order(StringComparer.Ordinal));
                (kodlar, eylem) = (secilen, ayni ? "ICD-10 önerisini onayladı" : "ICD-10 önerisini düzenleyip onayladı");
                break;
            case "reddet":
                (kodlar, eylem) = ([], "ICD-10 önerisini reddetti");
                break;
            default:
                return Gecersiz("karar", "Karar 'onayla', 'duzenle' ya da 'reddet' olmalı.");
        }

        var simdi = zaman.GetUtcNow().UtcDateTime;
        var reddedildi = istek.Karar == "reddet";
        oneri.Durum = reddedildi ? TaniOnerisiDurumu.Reddedildi : TaniOnerisiDurumu.Onaylandi;
        oneri.OnaylayanHekimId = Guid.Parse(kullanici.FindFirstValue(DemoClaim.HekimId)!);
        oneri.OnayZamani = simdi;
        // Ret mevcut tanılara dokunmaz; onayda tanı listesi hekimin kabul ettiği kodlarla değişir.
        if (!reddedildi) muayene.Tanilar = kodlar;
        db.ErisimKayitlari.Add(new ErisimKaydi
        {
            Id = Guid.NewGuid(),
            Zaman = simdi,
            KullaniciRol = kullanici.FindFirstValue(DemoClaim.Rol) ?? "",
            KullaniciAd = kullanici.FindFirstValue(DemoClaim.Ad) ?? "",
            HastaId = randevu!.HastaId,
            Eylem = reddedildi ? eylem : $"{eylem} ({string.Join(", ", kodlar)})",
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Öneri durumu ya da muayene durumu (eşzamanlı kapanış) bu arada değişti.
            return TypedResults.Problem(detail: "Bu öneri ya da vizit bu arada değişti; sayfa yenilenince güncel durum görünür.",
                statusCode: StatusCodes.Status409Conflict);
        }
        return TypedResults.Ok(await DetayAsync(muayene, db, ct));
    }

    /// <summary>
    /// Epikriz girdisi: kayıttaki bilgiler + yalnızca hekimin onayladığı/düzenlediği ön anamnez + her testin son değeri
    /// (işaret kodda hesaplanmış). Maskeleme AiHatti'de yapılır.
    /// </summary>
    private static async Task<EpikrizGirdisi> GirdiAsync(
        KlinikDbContext db, Hasta hasta, Randevu randevu, Muayene muayene, DateTime simdi, CancellationToken ct)
    {
        string? onAnamnez = null;
        if (randevu.OnAnamnezId is { } oid
            && await db.OnAnamnezler.AsNoTracking().FirstOrDefaultAsync(o => o.Id == oid, ct) is { } o)
        {
            onAnamnez = o.Durum switch
            {
                OnAnamnezDurumu.Duzenlendi => o.HekimDuzeltmesi,
                OnAnamnezDurumu.Onaylandi when o.AiTaslakJson is { } j =>
                    JsonSerializer.Deserialize<OnAnamnezTaslagi>(j, OnAnamnezTaslagi.Json)!.Ozet,
                _ => null, // taslak / reddedilmiş: hekim onayı yok, epikrize girmez
            };
        }

        var sonuclar = await db.TahlilSonuclari.AsNoTracking().Include(s => s.TahlilTanimi)
            .Where(s => s.HastaId == hasta.Id).ToListAsync(ct);
        var tahliller = sonuclar.GroupBy(s => s.TahlilTanimiId)
            .Select(g => g.OrderBy(s => s.Zaman).Last())
            .OrderBy(s => s.TahlilTanimi!.LoincKodu, StringComparer.Ordinal)
            .Select(s =>
            {
                var t = s.TahlilTanimi!;
                var (alt, ust) = TahlilDegerlendirici.Aralik(t, hasta.Cinsiyet);
                return new EpikrizTahlili(t.Ad, s.Deger, t.Birim, alt, ust, s.Isaret, t.Ondalik, t.LoincKodu);
            })
            .ToList();

        var brans = await db.Hekimler.AsNoTracking().Where(h => h.Id == randevu.HekimId).Select(h => h.Brans!.Ad).FirstAsync(ct);
        return new EpikrizGirdisi(hasta.TakmaAd, AiHatti.YasAraligi(IstanbulSaati.Yas(hasta.DogumTarihi, simdi)),
            hasta.Cinsiyet, brans, hasta.Alerjiler, hasta.KronikHastaliklar, hasta.KullandigiIlaclar,
            onAnamnez, muayene.HekimNotu, tahliller);
    }

    /// <summary>Geçerli epikriz: reddedilmemiş olan (en fazla bir tane); yoksa en son reddedilen.</summary>
    private static Task<Epikriz?> SonEpikrizAsync(KlinikDbContext db, Guid muayeneId, CancellationToken ct) =>
        db.Epikrizler.AsNoTracking().Where(e => e.MuayeneId == muayeneId)
            .OrderBy(e => e.Durum == EpikrizDurumu.Reddedildi).ThenByDescending(e => e.AiZamani).FirstOrDefaultAsync(ct);

    /// <summary>Geçerli öneri: reddedilmemiş olan (en fazla bir tane); yoksa en son reddedilen.</summary>
    private static Task<TaniOnerisi?> SonTaniOnerisiAsync(KlinikDbContext db, Guid muayeneId, CancellationToken ct) =>
        db.TaniOnerileri.AsNoTracking().Where(o => o.MuayeneId == muayeneId)
            .OrderBy(o => o.Durum == TaniOnerisiDurumu.Reddedildi).ThenByDescending(o => o.AiZamani).FirstOrDefaultAsync(ct);

    private static async Task<MuayeneDetayi> DetayAsync(Muayene m, KlinikDbContext db, CancellationToken ct)
    {
        EpikrizKarti? kart = null;
        if (await SonEpikrizAsync(db, m.Id, ct) is { } e)
        {
            EpikrizKarari? karar = null;
            if (e.OnaylayanHekimId is { } hekimId)
                karar = new EpikrizKarari(e.Durum, await HekimAdiAsync(db, hekimId, ct), e.OnayZamani!.Value);
            kart = new EpikrizKarti(e.Id, e.Durum, e.Kaynak, e.AiModel, e.AiZamani, e.KotaDoldu,
                EpikrizTaslagi.Oku(e.AiTaslak), e.SonMetin, karar);
        }

        TaniOnerisiKarti? tani = null;
        if (await SonTaniOnerisiAsync(db, m.Id, ct) is { } o)
        {
            TaniKarari? karar = null;
            if (o.OnaylayanHekimId is { } hekimId)
                karar = new TaniKarari(o.Durum, await HekimAdiAsync(db, hekimId, ct), o.OnayZamani!.Value);
            var oneriler = TaniOnerisiYaniti.Oku(o.OneriJson).Oneriler
                .Select(x => new TaniOnerisiSatiri(x.Kod, Icd10Listesi.Bul(x.Kod)?.Ad ?? "", x.Gerekce)).ToList();
            tani = new TaniOnerisiKarti(o.Id, o.Durum, o.Kaynak, o.AiModel, o.AiZamani, o.KotaDoldu, oneriler, karar);
        }

        var tanilar = m.Tanilar.Select(k => Icd10Listesi.Bul(k) ?? new Icd10Kodu(k, "")).ToList();
        return new MuayeneDetayi(m.Id, m.RandevuId, m.Durum, m.HekimNotu, kart, tanilar, tani);
    }

    private static Task<string> HekimAdiAsync(KlinikDbContext db, Guid hekimId, CancellationToken ct) =>
        db.Hekimler.AsNoTracking().Where(h => h.Id == hekimId).Select(h => h.Unvan + " " + h.Ad).FirstAsync(ct);

    /// <summary>Muayene + randevu; yazma işlemleri yalnızca randevunun hekimine açıktır.</summary>
    private static async Task<(Muayene?, Randevu?, ProblemHttpResult?)> YukleAsync(
        Guid muayeneId, ClaimsPrincipal kullanici, KlinikDbContext db, CancellationToken ct)
    {
        var muayene = await db.Muayeneler.FirstOrDefaultAsync(m => m.Id == muayeneId, ct);
        if (muayene is null) return (null, null, Bulunamadi());
        var randevu = await db.Randevular.AsNoTracking().FirstAsync(r => r.Id == muayene.RandevuId, ct);
        if (kullanici.FindFirstValue(DemoClaim.HekimId) != randevu.HekimId.ToString())
            return (null, null, TypedResults.Problem(detail: "Bu işlem için yetkiniz yok.", statusCode: StatusCodes.Status403Forbidden));
        return (muayene, randevu, null);
    }

    private static ProblemHttpResult VizitKapali(string detay = "Vizit kapandı; epikriz değiştirilemez.") =>
        TypedResults.Problem(detail: detay, statusCode: StatusCodes.Status409Conflict);

    private static ProblemHttpResult Bulunamadi() =>
        TypedResults.Problem(detail: "Muayene bulunamadı.", statusCode: StatusCodes.Status404NotFound);

    private static ValidationProblem Gecersiz(string alan, string mesaj) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [alan] = [mesaj] });
}

public sealed record NotIstegi(string? Metin);

public sealed record EpikrizKarari(EpikrizDurumu Durum, string HekimAd, DateTime Zaman);

/// <param name="Zaman">Taslağın üretildiği an (UTC); kart başlığında saat.</param>
/// <param name="SonMetin">Hekim düzenleyip onayladıysa öykü/değerlendirme/plan metni; alerji ve tahliller yine Taslak'tan.</param>
public sealed record EpikrizKarti(
    Guid Id, EpikrizDurumu Durum, AiKaynak Kaynak, string? Model, DateTime Zaman, bool KotaDoldu,
    EpikrizTaslagi Taslak, string? SonMetin, EpikrizKarari? Karar);

/// <param name="Tanilar">Hekimin onayladığı ICD-10 kodları (kapalı listeden, adıyla).</param>
public sealed record MuayeneDetayi(
    Guid Id, Guid RandevuId, MuayeneDurumu Durum, string? HekimNotu, EpikrizKarti? Epikriz,
    IReadOnlyList<Icd10Kodu> Tanilar, TaniOnerisiKarti? TaniOnerisi);

/// <param name="Kodlar">Yalnız "duzenle"de: kapalı listeden 1-5 kod.</param>
public sealed record TaniKararIstegi(string Karar, IReadOnlyList<string?>? Kodlar);

public sealed record TaniKarari(TaniOnerisiDurumu Durum, string HekimAd, DateTime Zaman);

public sealed record TaniOnerisiSatiri(string Kod, string Ad, string Gerekce);

/// <param name="Zaman">Önerinin üretildiği an (UTC); kart başlığında saat.</param>
public sealed record TaniOnerisiKarti(
    Guid Id, TaniOnerisiDurumu Durum, AiKaynak Kaynak, string? Model, DateTime Zaman, bool KotaDoldu,
    IReadOnlyList<TaniOnerisiSatiri> Oneriler, TaniKarari? Karar);
