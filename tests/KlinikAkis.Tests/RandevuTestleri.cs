using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlinikAkis.Domain;
using KlinikAkis.Infrastructure.Seed;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlinikAkis.Tests;

/// <summary>Randevu alma, listeler ve durum geçişleri (SPEC §7, §11 test 9 entegrasyon kısmı).</summary>
public sealed class RandevuTestleri(KlinikApiFabrikasi fabrika) : IClassFixture<KlinikApiFabrikasi>
{
    [Fact]
    public async Task Randevu_alinca_slot_dolar_ve_sira_no_brans_gununde_artar()
    {
        var hasta = await fabrika.GirisYapAsync("hasta", SenaryoIdleri.Mehmet);
        var slot = await BosSlotAsync(SenaryoIdleri.HekimKbb);
        var oncekiEnBuyuk = await EnBuyukSiraAsync(SenaryoIdleri.HekimKbb, slot.Baslangic);

        var yanit = await hasta.PostAsJsonAsync("/api/randevular", new { slotId = slot.Id });

        Assert.Equal(HttpStatusCode.Created, yanit.StatusCode);
        var govde = await Json(yanit);
        Assert.Equal(SiraNo.Olustur(oncekiEnBuyuk + 1), govde.GetProperty("siraNo").GetString());
        Assert.Equal("Planlandi", govde.GetProperty("durum").GetString());
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        Assert.True((await db.Slotlar.FindAsync(slot.Id))!.Dolu);
    }

    [Fact]
    public async Task Dolu_slota_randevu_409()
    {
        var hasta = await fabrika.GirisYapAsync("hasta", SenaryoIdleri.Zeynep);
        var slot = await BosSlotAsync(SenaryoIdleri.HekimDahiliye1);
        (await hasta.PostAsJsonAsync("/api/randevular", new { slotId = slot.Id })).EnsureSuccessStatusCode();

        var ikinci = await hasta.PostAsJsonAsync("/api/randevular", new { slotId = slot.Id });

        Assert.Equal(HttpStatusCode.Conflict, ikinci.StatusCode);
    }

    [Fact]
    public async Task Gecmis_slota_randevu_409()
    {
        var hasta = await fabrika.GirisYapAsync("hasta", SenaryoIdleri.Hasan);
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var slot = await db.Slotlar.AsNoTracking().FirstAsync(s => !s.Dolu);
        await db.Slotlar.Where(s => s.Id == slot.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.Baslangic, KlinikApiFabrikasi.Simdi.UtcDateTime.AddMinutes(-10)));

        var yanit = await hasta.PostAsJsonAsync("/api/randevular", new { slotId = slot.Id });

        Assert.Equal(HttpStatusCode.Conflict, yanit.StatusCode);
    }

    [Fact]
    public async Task Hasta_yalnizca_kendi_randevularini_gorur()
    {
        var elif = await fabrika.GirisYapAsync("hasta", SenaryoIdleri.Elif);
        var can = await fabrika.GirisYapAsync("hasta", SenaryoIdleri.Can);
        var slot = await BosSlotAsync(SenaryoIdleri.HekimCocukSagligi);
        var alinan = await Json(await elif.PostAsJsonAsync("/api/randevular", new { slotId = slot.Id }));

        var elifinki = await elif.GetFromJsonAsync<JsonElement>("/api/hasta/me/randevular");
        var caninki = await can.GetFromJsonAsync<JsonElement>("/api/hasta/me/randevular");

        Assert.Contains(elifinki.EnumerateArray(), r => r.GetProperty("id").GetString() == alinan.GetProperty("id").GetString());
        Assert.DoesNotContain(caninki.EnumerateArray(), r => r.GetProperty("id").GetString() == alinan.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Tam_akis_kabul_cagir_basla_kapat()
    {
        var (randevuId, hekim) = await RandevuAlAsync(SenaryoIdleri.Ayse, SenaryoIdleri.HekimEndokrinoloji);
        var kabul = await fabrika.GirisYapAsync("kabul");

        var kabulYaniti = await Json(await kabul.PostAsync($"/api/randevular/{randevuId}/kabul", null));
        Assert.Equal("KabulEdildi", kabulYaniti.GetProperty("durum").GetString());
        Assert.StartsWith("SIM-", kabulYaniti.GetProperty("takipNo").GetString(), StringComparison.Ordinal);

        foreach (var (eylem, durum) in new[] { ("cagir", "Cagrildi"), ("basla", "Muayenede"), ("kapat", "Tamamlandi") })
        {
            var yanit = await hekim.PostAsync($"/api/randevular/{randevuId}/{eylem}", null);
            Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
            Assert.Equal(durum, (await Json(yanit)).GetProperty("durum").GetString());
        }

        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        Assert.Equal(MuayeneDurumu.Kapali, (await db.Muayeneler.SingleAsync(m => m.RandevuId == randevuId)).Durum);
    }

    [Fact]
    public async Task Gecersiz_gecis_409_ve_Turkce_mesaj()
    {
        var (randevuId, hekim) = await RandevuAlAsync(SenaryoIdleri.Mehmet, SenaryoIdleri.HekimEndokrinoloji);

        var yanit = await hekim.PostAsync($"/api/randevular/{randevuId}/cagir", null);

        Assert.Equal(HttpStatusCode.Conflict, yanit.StatusCode);
        Assert.Equal("Randevu 'Planlandı' durumundayken çağrılamaz.", (await Json(yanit)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Baska_hekimin_randevusu_403()
    {
        var (randevuId, _) = await RandevuAlAsync(SenaryoIdleri.Zeynep, SenaryoIdleri.HekimEndokrinoloji);
        var kabul = await fabrika.GirisYapAsync("kabul");
        (await kabul.PostAsync($"/api/randevular/{randevuId}/kabul", null)).EnsureSuccessStatusCode();
        var baskaHekim = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimKardiyoloji);

        var yanit = await baskaHekim.PostAsync($"/api/randevular/{randevuId}/cagir", null);

        Assert.Equal(HttpStatusCode.Forbidden, yanit.StatusCode);
    }

    [Fact]
    public async Task Doktor_kabul_eylemi_yapamaz_kabul_doktor_eylemi_yapamaz()
    {
        var (randevuId, hekim) = await RandevuAlAsync(SenaryoIdleri.Hasan, SenaryoIdleri.HekimEndokrinoloji);
        var kabul = await fabrika.GirisYapAsync("kabul");

        Assert.Equal(HttpStatusCode.Forbidden, (await hekim.PostAsync($"/api/randevular/{randevuId}/kabul", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kabul.PostAsync($"/api/randevular/{randevuId}/cagir", null)).StatusCode);
    }

    [Fact]
    public async Task Hasta_randevu_listesine_ve_eylemlere_erisemez()
    {
        var hasta = await fabrika.GirisYapAsync("hasta", SenaryoIdleri.Ayse);

        Assert.Equal(HttpStatusCode.Forbidden, (await hasta.GetAsync("/api/randevular")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await hasta.PostAsync($"/api/randevular/{Guid.NewGuid()}/kabul", null)).StatusCode);
    }

    [Fact]
    public async Task Iptal_slotu_bosaltir()
    {
        var (randevuId, _) = await RandevuAlAsync(SenaryoIdleri.Elif, SenaryoIdleri.HekimDahiliye2);
        var kabul = await fabrika.GirisYapAsync("kabul");

        (await kabul.PostAsync($"/api/randevular/{randevuId}/iptal", null)).EnsureSuccessStatusCode();

        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var slotId = (await db.Randevular.FindAsync(randevuId))!.SlotId;
        Assert.False((await db.Slotlar.FindAsync(slotId))!.Dolu);

        // Boşalan saat gerçekten yeniden alınabilmeli (iptal edilen randevu benzersiz indekse takılmaz).
        var baskaHasta = await fabrika.GirisYapAsync("hasta", SenaryoIdleri.Mehmet);
        var yeniden = await baskaHasta.PostAsJsonAsync("/api/randevular", new { slotId });
        Assert.Equal(HttpStatusCode.Created, yeniden.StatusCode);
    }

    [Theory]
    [InlineData("Ayşe", "Kaya", "Ayşe K.", "A*** K***", "A.K.")]
    [InlineData("İsmail", "Işık", "İsmail I.", "İ*** I***", "İ.I.")]
    [InlineData("Ömer", "Ünal", "Ömer Ü.", "Ö*** Ü***", "Ö.Ü.")]
    [InlineData("Özge", "Çelik", "Özge Ç.", "Ö*** Ç***", "Ö.Ç.")] // ayrışık (NFD) harf bölünmez
    public void Ad_gosterimi_bas_harfi_metin_ogesine_gore_alir(string ad, string soyad, string kisa, string maskeli, string basHarfler)
    {
        Assert.Equal(kisa, AdGosterimi.Kisa(ad, soyad));
        Assert.Equal(maskeli, AdGosterimi.Maskeli(ad, soyad));
        Assert.Equal(basHarfler, AdGosterimi.BasHarfler(ad, soyad));
    }

    [Fact]
    public async Task Doktor_listesi_yalnizca_kendi_hastalari_ve_doktor_eylemleri()
    {
        var (randevuId, hekim) = await RandevuAlAsync(SenaryoIdleri.Can, SenaryoIdleri.HekimKbb);

        var liste = (await hekim.GetFromJsonAsync<JsonElement>("/api/randevular")).EnumerateArray().ToList();

        Assert.NotEmpty(liste);
        Assert.All(liste, r => Assert.EndsWith("Kerem Aydın", r.GetProperty("hekimAd").GetString(), StringComparison.Ordinal));
        var satir = liste.Single(r => r.GetProperty("id").GetString() == randevuId.ToString());
        Assert.Equal("Can D.", satir.GetProperty("hastaAd").GetString());
        // Planlandı durumunda doktorun yapabileceği eylem yok (kabul önce).
        Assert.Empty(satir.GetProperty("izinliEylemler").EnumerateArray());
    }

    [Fact]
    public async Task Kabul_listesinde_tam_ad_yok_maskeli_ad_var()
    {
        var (randevuId, _) = await RandevuAlAsync(SenaryoIdleri.Ayse, SenaryoIdleri.HekimDahiliye1);
        var kabul = await fabrika.GirisYapAsync("kabul");

        var ham = await kabul.GetStringAsync("/api/randevular");
        var satir = JsonDocument.Parse(ham).RootElement.EnumerateArray()
            .Single(r => r.GetProperty("id").GetString() == randevuId.ToString());

        Assert.Equal("A*** K***", satir.GetProperty("hastaAd").GetString());
        Assert.DoesNotContain("Ayşe", ham, StringComparison.Ordinal);
        Assert.Equal(["Kabul", "Iptal"], satir.GetProperty("izinliEylemler").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Kabul_hekimId_ile_suzer_doktor_baska_hekimi_isteyemez()
    {
        var kabul = await fabrika.GirisYapAsync("kabul");
        var kbb = (await kabul.GetFromJsonAsync<JsonElement>($"/api/randevular?hekimId={SenaryoIdleri.HekimKbb}")).EnumerateArray().ToList();
        Assert.NotEmpty(kbb);
        Assert.All(kbb, r => Assert.EndsWith("Kerem Aydın", r.GetProperty("hekimAd").GetString(), StringComparison.Ordinal));

        // Doktorun hekimi token'dan gelir; sorgudaki başka hekimId yok sayılır.
        var endo = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimEndokrinoloji);
        var liste = (await endo.GetFromJsonAsync<JsonElement>($"/api/randevular?hekimId={SenaryoIdleri.HekimKbb}")).EnumerateArray();
        Assert.All(liste, r => Assert.EndsWith("Selin Arslan", r.GetProperty("hekimAd").GetString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ayni_randevuya_ikinci_kabul_409_500_degil()
    {
        var (randevuId, _) = await RandevuAlAsync(SenaryoIdleri.Mehmet, SenaryoIdleri.HekimKardiyoloji);
        var kabul = await fabrika.GirisYapAsync("kabul");

        var istekler = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => kabul.PostAsync($"/api/randevular/{randevuId}/kabul", null)));

        Assert.Single(istekler, y => y.StatusCode == HttpStatusCode.OK);
        Assert.Single(istekler, y => y.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Gecersiz_tarih_400()
    {
        var kabul = await fabrika.GirisYapAsync("kabul");

        Assert.Equal(HttpStatusCode.BadRequest, (await kabul.GetAsync("/api/randevular?tarih=29.10.2026")).StatusCode);
    }

    private async Task<(Guid RandevuId, HttpClient Hekim)> RandevuAlAsync(Guid hastaId, Guid hekimId)
    {
        var hasta = await fabrika.GirisYapAsync("hasta", hastaId);
        var slot = await BosSlotAsync(hekimId);
        var yanit = await hasta.PostAsJsonAsync("/api/randevular", new { slotId = slot.Id });
        yanit.EnsureSuccessStatusCode();
        var id = (await Json(yanit)).GetProperty("id").GetGuid();
        return (id, await fabrika.GirisYapAsync("doktor", hekimId));
    }

    private async Task<Domain.Varliklar.Slot> BosSlotAsync(Guid hekimId)
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var simdi = KlinikApiFabrikasi.Simdi.UtcDateTime;
        return await db.Slotlar.AsNoTracking()
            .Where(s => s.HekimId == hekimId && !s.Dolu && s.Baslangic > simdi)
            .OrderBy(s => s.Baslangic).FirstAsync();
    }

    private async Task<int> EnBuyukSiraAsync(Guid hekimId, DateTime slotBaslangic)
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var bransId = (await db.Hekimler.FindAsync(hekimId))!.BransId;
        var (bas, bit) = IstanbulSaati.GunAraligi(IstanbulSaati.Bugun(slotBaslangic));
        var siralar = await db.Randevular
            .Where(r => r.Hekim!.BransId == bransId && r.Slot!.Baslangic >= bas && r.Slot.Baslangic < bit)
            .Select(r => r.SiraNo).ToListAsync();
        return siralar.Select(SiraNo.Sira).DefaultIfEmpty(0).Max();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage yanit) =>
        JsonDocument.Parse(await yanit.Content.ReadAsStringAsync()).RootElement;
}
