using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlinikAkis.Domain;
using KlinikAkis.Domain.Varliklar;
using KlinikAkis.Infrastructure.Ai;
using KlinikAkis.Infrastructure.Seed;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlinikAkis.Tests;

/// <summary>
/// Epikriz (SPEC §13.7a): maskeli girdi, onaysız ön anamnez girmez, alerji ve tahlil satırları kodda, geçersiz yanıtta
/// yedek, hekim kararı ve yetkiler.
/// </summary>
public sealed class EpikrizTestleri(KlinikApiFabrikasi fabrika) : IClassFixture<KlinikApiFabrikasi>
{
    private static readonly EpikrizGirdisi OrnekGirdi = new(
        "P-7F3A", "50-59", Cinsiyet.K, "Endokrinoloji", ["Penisilin"], ["Tip 2 diyabet"], ["Metformin"],
        "Şikayetler: Çok su içme, Sık idrara çıkma.", "Ağız kuruluğu var, genel durum iyi.",
        [new EpikrizTahlili("HbA1c", 7.4m, "%", 4.0m, 5.6m, Isaret.Yuksek, 1)]);

    [Fact]
    public async Task Epikriz_maskeli_girdiyle_uretilir_alerji_ve_tahlil_satiri_kodda()
    {
        var (doktor, muayeneId) = await MuayenedeAsync(SenaryoIdleri.Ayse);
        (await doktor.PutAsJsonAsync($"/api/muayeneler/{muayeneId}/not",
            new { metin = "Ayşe'nin ağız kuruluğu var, tel 0532 123 45 67. Metformin devam." })).EnsureSuccessStatusCode();

        var yanit = await doktor.PostAsync($"/api/muayeneler/{muayeneId}/epikriz", null);

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        var kart = (await Json(yanit)).GetProperty("epikriz");
        Assert.Equal("Llm", kart.GetProperty("kaynak").GetString());
        Assert.Equal("Taslak", kart.GetProperty("durum").GetString());
        var taslak = kart.GetProperty("taslak");
        Assert.Equal("Penisilin", taslak.GetProperty("alerji").GetString());
        Assert.Contains(taslak.GetProperty("tahliller").EnumerateArray(),
            t => t.GetString() == "HbA1c: 7,4 % (referans 4,0–5,6) ▲ Y");

        // Test saati sabit: kayıt zamana göre değil içeriğe göre bulunur.
        var kayit = (await DenetimlerAsync(SenaryoIdleri.Ayse)).Single(k => k.MaskeliGirdi.Contains("ağız kuruluğu", StringComparison.Ordinal));
        Assert.Equal(AiHatti.EpikrizIstemSurumu, kayit.IstemSurumu);
        Assert.Contains("[AD] ağız kuruluğu", kayit.MaskeliGirdi, StringComparison.Ordinal);
        Assert.Contains("[TELEFON]", kayit.MaskeliGirdi, StringComparison.Ordinal);
        Assert.Contains("<hekim_notu>", kayit.MaskeliGirdi, StringComparison.Ordinal);
        var ayse = await HastaAsync(SenaryoIdleri.Ayse);
        foreach (var kimlik in new[] { ayse.Ad, ayse.Soyad, ayse.TcKimlik, ayse.Telefon })
            Assert.DoesNotContain(kimlik, kayit.MaskeliGirdi, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Onaysiz_on_anamnez_epikrize_girmez_onaylaninca_girer_bekleyen_taslak_yeniden_uretilmez()
    {
        var (doktor, muayeneId, onAnamnezId) = await MuayenedeOnAnamnezliAsync(SenaryoIdleri.Elif);

        var ilk = (await Json(await doktor.PostAsync($"/api/muayeneler/{muayeneId}/epikriz", null))).GetProperty("epikriz");
        Assert.Contains("<on_anamnez>hekim onaylı ön anamnez yok</on_anamnez>",
            Assert.Single(await DenetimlerAsync(SenaryoIdleri.Elif)).MaskeliGirdi, StringComparison.Ordinal);

        // Bekleyen taslak varken model yeniden çağrılmaz: aynı taslak döner.
        var tekrar = (await Json(await doktor.PostAsync($"/api/muayeneler/{muayeneId}/epikriz", null))).GetProperty("epikriz");
        Assert.Equal(ilk.GetProperty("id").GetGuid(), tekrar.GetProperty("id").GetGuid());

        (await doktor.PostAsJsonAsync($"/api/epikriz/{ilk.GetProperty("id").GetGuid()}/karar", new { karar = "reddet" }))
            .EnsureSuccessStatusCode();
        (await doktor.PostAsJsonAsync($"/api/onanamnez/{onAnamnezId}/karar", new { karar = "onayla" })).EnsureSuccessStatusCode();

        var yeni = (await Json(await doktor.PostAsync($"/api/muayeneler/{muayeneId}/epikriz", null))).GetProperty("epikriz");
        Assert.NotEqual(ilk.GetProperty("id").GetGuid(), yeni.GetProperty("id").GetGuid());
        var kayitlar = await DenetimlerAsync(SenaryoIdleri.Elif);
        Assert.Equal(2, kayitlar.Count);
        Assert.Contains(kayitlar, k => k.MaskeliGirdi.Contains("<on_anamnez>Şikayetler:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{bozuk", EpikrizDogrulayici.GecersizJson)]
    [InlineData("""{"oyku":"","degerlendirme":"x","plan":"y"}""", EpikrizDogrulayici.BosAlan)]
    [InlineData("""{"oyku":"Öykü.","degerlendirme":"HbA1c 9,8 bulundu.","plan":"Kontrol."}""", EpikrizDogrulayici.UydurmaSayi)]
    [InlineData("uzun", EpikrizDogrulayici.Uzun)]
    [InlineData("""{"oyku":"Öykü.","degerlendirme":"Değerlendirme.","plan":"Metformin 1500mg 2x1."}""", EpikrizDogrulayici.UydurmaSayi)]
    [InlineData("""{"oyku":"Öykü.","degerlendirme":"Glukoz 9,1mmol.","plan":"Kontrol."}""", EpikrizDogrulayici.UydurmaSayi)]
    public async Task Gecersiz_yanit_yedege_duser_kod_satirlari_korunur(string yanit, string neden)
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var json = yanit == "uzun"
            ? JsonSerializer.Serialize(new { oyku = new string('a', EpikrizDogrulayici.AlanSiniri + 1), degerlendirme = "x", plan = "y" })
            : yanit;
        var hatti = AiHattiTestleri.Hatti(kapsam.ServiceProvider, new FakeChatClient { Gecikme = TimeSpan.Zero, Yanitla = _ => json });

        var sonuc = await hatti.EpikrizAsync(await HastaAsync(SenaryoIdleri.Ayse), OrnekGirdi, default);
        await kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>().SaveChangesAsync();

        Assert.Equal(AiKaynak.Yedek, sonuc.Kaynak);
        Assert.Null(sonuc.Model);
        // Yedek kayıttaki onaylı metinden kurulur; alerji ve tahlil satırı yine koddan.
        Assert.Equal(OrnekGirdi.OnAnamnez, sonuc.Taslak.Oyku);
        Assert.Equal(OrnekGirdi.HekimNotu, sonuc.Taslak.Degerlendirme);
        Assert.Equal("Penisilin", sonuc.Taslak.Alerji);
        Assert.Equal(["HbA1c: 7,4 % (referans 4,0–5,6) ▲ Y"], sonuc.Taslak.Tahliller);
        Assert.Contains(await DenetimlerAsync(SenaryoIdleri.Ayse), k => k.YedegeDustu && k.Neden == neden);
    }

    [Fact]
    public void Alerji_bossa_bilinen_alerji_yok_yazar()
    {
        var taslak = EpikrizTaslagi.Olustur(EpikrizYedekSablonu.Olustur(OrnekGirdi with { Alerjiler = [] }), OrnekGirdi with { Alerjiler = [] });
        Assert.Equal(EpikrizGirdisi.AlerjiYok, taslak.Alerji);
    }

    [Fact]
    public async Task Karar_onayla_erisim_kaydi_yazar_ikinci_karar_409_baska_hekim_403_duzenle_metinsiz_400()
    {
        var (doktor, muayeneId) = await MuayenedeAsync(SenaryoIdleri.Zeynep);
        var id = (await Json(await doktor.PostAsync($"/api/muayeneler/{muayeneId}/epikriz", null)))
            .GetProperty("epikriz").GetProperty("id").GetGuid();
        var baskaHekim = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimKardiyoloji);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await baskaHekim.PostAsJsonAsync($"/api/epikriz/{id}/karar", new { karar = "onayla" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await baskaHekim.PostAsync($"/api/muayeneler/{muayeneId}/epikriz", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await doktor.PostAsJsonAsync($"/api/epikriz/{id}/karar", new { karar = "duzenle" })).StatusCode);

        var yanit = await doktor.PostAsJsonAsync($"/api/epikriz/{id}/karar",
            new { karar = "duzenle", metin = "Öykü: susama. Plan: HbA1c kontrolü." });
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        var kart = (await Json(yanit)).GetProperty("epikriz");
        Assert.Equal("Onaylandi", kart.GetProperty("durum").GetString());
        Assert.Equal("Öykü: susama. Plan: HbA1c kontrolü.", kart.GetProperty("sonMetin").GetString());
        Assert.Contains("Selin Arslan", kart.GetProperty("karar").GetProperty("hekimAd").GetString(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.Conflict,
            (await doktor.PostAsJsonAsync($"/api/epikriz/{id}/karar", new { karar = "onayla" })).StatusCode);
        // Onaylı epikriz nottan üretildi: not artık değişmez (onaylı metin ve 103 paketi eski notla kalmasın).
        Assert.Equal(HttpStatusCode.Conflict,
            (await doktor.PutAsJsonAsync($"/api/muayeneler/{muayeneId}/not", new { metin = "onaydan sonra" })).StatusCode);
        var erisim = await Json(await doktor.GetAsync($"/api/hastalar/{SenaryoIdleri.Zeynep}/erisim"));
        Assert.Equal("Epikrizi düzenleyip onayladı", erisim[0].GetProperty("eylem").GetString());
    }

    [Fact]
    public async Task Hekim_notu_yalniz_randevunun_hekimi_yazar_kapanan_vizitte_not_taslak_karar_409()
    {
        var (doktor, muayeneId) = await MuayenedeAsync(SenaryoIdleri.Mehmet);
        var baskaHekim = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimKbb);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await baskaHekim.PutAsJsonAsync($"/api/muayeneler/{muayeneId}/not", new { metin = "x" })).StatusCode);
        var detay = await Json(await doktor.PutAsJsonAsync($"/api/muayeneler/{muayeneId}/not", new { metin = "  Boğaz hiperemik.  " }));
        Assert.Equal("Boğaz hiperemik.", detay.GetProperty("hekimNotu").GetString());

        var epikrizId = (await Json(await doktor.PostAsync($"/api/muayeneler/{muayeneId}/epikriz", null)))
            .GetProperty("epikriz").GetProperty("id").GetGuid();
        var randevuId = detay.GetProperty("randevuId").GetGuid();
        (await doktor.PostAsync($"/api/randevular/{randevuId}/kapat", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,
            (await doktor.PutAsJsonAsync($"/api/muayeneler/{muayeneId}/not", new { metin = "geç not" })).StatusCode);
        // 103 kapanışta gitti: sonradan taslak ya da karar kayıtla paketi ayırır.
        Assert.Equal(HttpStatusCode.Conflict, (await doktor.PostAsync($"/api/muayeneler/{muayeneId}/epikriz", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await doktor.PostAsJsonAsync($"/api/epikriz/{epikrizId}/karar", new { karar = "onayla" })).StatusCode);
    }

    [Fact]
    public async Task Randevu_satiri_muayene_idsini_gosterir()
    {
        var (doktor, muayeneId) = await MuayenedeAsync(SenaryoIdleri.Can);
        var liste = await Json(await doktor.GetAsync("/api/randevular"));
        Assert.Contains(liste.EnumerateArray(), r =>
            r.GetProperty("muayeneId").ValueKind == JsonValueKind.String && r.GetProperty("muayeneId").GetGuid() == muayeneId);
    }

    /// <summary>Şikayet sohbetsiz randevu → kabul → çağır → başla (Endokrinoloji hekimi).</summary>
    private async Task<(HttpClient Doktor, Guid MuayeneId)> MuayenedeAsync(Guid hastaId)
    {
        var hasta = await fabrika.GirisYapAsync("hasta", hastaId);
        var randevuId = (await Json(await hasta.PostAsJsonAsync("/api/randevular", new { slotId = await BosSlotAsync() })))
            .GetProperty("id").GetGuid();
        return await BaslatAsync(randevuId);
    }

    private async Task<(HttpClient Doktor, Guid MuayeneId, Guid OnAnamnezId)> MuayenedeOnAnamnezliAsync(Guid hastaId)
    {
        var hasta = await fabrika.GirisYapAsync("hasta", hastaId);
        var onAnamnezId = (await Json(await hasta.PostAsync("/api/onanamnez", null))).GetProperty("onAnamnezId").GetGuid();
        foreach (var k in AiHattiTestleri.AyseSohbeti)
            (await hasta.PostAsJsonAsync($"/api/onanamnez/{onAnamnezId}/cevap", new { secimler = k.Secimler, metin = k.Metin }))
                .EnsureSuccessStatusCode();
        (await hasta.PostAsync($"/api/onanamnez/{onAnamnezId}/ozet", null)).EnsureSuccessStatusCode();
        var randevuId = (await Json(await hasta.PostAsJsonAsync("/api/randevular", new { slotId = await BosSlotAsync(), onAnamnezId })))
            .GetProperty("id").GetGuid();
        var (doktor, muayeneId) = await BaslatAsync(randevuId);
        return (doktor, muayeneId, onAnamnezId);
    }

    private async Task<(HttpClient, Guid)> BaslatAsync(Guid randevuId)
    {
        var kabul = await fabrika.GirisYapAsync("kabul");
        (await kabul.PostAsync($"/api/randevular/{randevuId}/kabul", null)).EnsureSuccessStatusCode();
        var doktor = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimEndokrinoloji);
        (await doktor.PostAsync($"/api/randevular/{randevuId}/cagir", null)).EnsureSuccessStatusCode();
        (await doktor.PostAsync($"/api/randevular/{randevuId}/basla", null)).EnsureSuccessStatusCode();
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        return (doktor, await db.Muayeneler.Where(m => m.RandevuId == randevuId).Select(m => m.Id).SingleAsync());
    }

    private async Task<Guid> BosSlotAsync()
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var simdi = KlinikApiFabrikasi.Simdi.UtcDateTime;
        return await db.Slotlar.Where(s => s.HekimId == SenaryoIdleri.HekimEndokrinoloji && !s.Dolu && s.Baslangic > simdi)
            .OrderBy(s => s.Baslangic).Select(s => s.Id).FirstAsync();
    }

    private async Task<Hasta> HastaAsync(Guid id)
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        return await kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>().Hastalar.AsNoTracking().FirstAsync(h => h.Id == id);
    }

    private async Task<List<AiAuditLog>> DenetimlerAsync(Guid hastaId)
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        return await kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>().AiAuditLoglari.AsNoTracking()
            .Where(a => a.HastaId == hastaId && a.Ozellik == AiOzellik.Epikriz).ToListAsync();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage yanit) =>
        JsonDocument.Parse(await yanit.Content.ReadAsStringAsync()).RootElement;
}
