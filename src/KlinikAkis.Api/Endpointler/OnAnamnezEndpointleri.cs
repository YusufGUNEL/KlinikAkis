using System.Security.Claims;
using System.Text.Json;
using KlinikAkis.Api.Kimlik;
using KlinikAkis.Domain;
using KlinikAkis.Domain.Varliklar;
using KlinikAkis.Infrastructure.Ai;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace KlinikAkis.Api.Endpointler;

/// <summary>
/// Şikayet sohbeti (SPEC §9.4) ve kırmızı bayrak (§9.3). Her cevapta kural çalışır; tetiklenirse akış durur
/// (sonraki cevap, özet ve bu ön anamnezle randevu 409). Özet yalnızca AiHatti üzerinden üretilir.
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
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Sohbet bitti: kural kontrolü → (temizse) maskeli metinle LLM taslağı. Denetim kaydı yazar");

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
        Guid id, ClaimsPrincipal kullanici, KlinikDbContext db, AiHatti hatti, CancellationToken ct)
    {
        var (onAnamnez, hasta, sorun) = await YukleAsync(id, kullanici, db, ct);
        if (sorun is not null) return sorun;
        if (onAnamnez!.Durum == OnAnamnezDurumu.KirmiziBayrak) return AkisDurdu();
        // Tekrar istek modeli yeniden çağırmaz (maliyet): mevcut taslak döner.
        if (onAnamnez.Durum != OnAnamnezDurumu.Sohbette)
            return TypedResults.Ok(new OzetYaniti(onAnamnez.Id, onAnamnez.Durum, onAnamnez.Kaynak, null));

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
            onAnamnez.AiTaslakJson = sonuc.CiktiJson;
        }
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new OzetYaniti(onAnamnez.Id, onAnamnez.Durum, onAnamnez.Kaynak, sonuc.KirmiziBayrak));
    }

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

public sealed record OzetYaniti(Guid OnAnamnezId, OnAnamnezDurumu Durum, AiKaynak? Kaynak, KirmiziBayrak? KirmiziBayrak);

public sealed record DenetimSatiri(
    Guid Id, DateTime Zaman, AiOzellik Ozellik, Guid? HastaId, string Saglayici, string Model, string IstemSurumu,
    string MaskeliGirdi, string GirdiHash, string? CiktiJson, int GecikmeMs, bool Basarili, bool YedegeDustu, string? Neden);
