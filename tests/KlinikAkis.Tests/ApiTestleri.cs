using System.Net;
using System.Text.Json;
using KlinikAkis.Infrastructure.Seed;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.Extensions.DependencyInjection;

namespace KlinikAkis.Tests;

public sealed class ApiTestleri(KlinikApiFabrikasi fabrika) : IClassFixture<KlinikApiFabrikasi>
{
    private readonly HttpClient _istemci = fabrika.CreateClient();

    [Fact]
    public async Task Ayse_ozeti_alerji_ve_maskeli_TC_icerir()
    {
        var ozet = await Getir($"/api/hastalar/{SenaryoIdleri.Ayse}/ozet");

        Assert.Equal("Ayşe", ozet.GetProperty("ad").GetString());
        Assert.Equal(54, ozet.GetProperty("yas").GetInt32());
        Assert.Equal("K", ozet.GetProperty("cinsiyet").GetString());
        Assert.Equal("Penisilin", ozet.GetProperty("alerjiler")[0].GetString());
        Assert.Matches(@"^99\*{7}\d{2}$", ozet.GetProperty("tcMaskeli").GetString());
    }

    [Fact]
    public async Task Ayse_tahlilleri_HbA1c_trendi_ve_kod_isareti_ile_doner()
    {
        var satirlar = await Getir($"/api/hastalar/{SenaryoIdleri.Ayse}/tahliller");
        var hba1c = satirlar.EnumerateArray().Single(s => s.GetProperty("loincKodu").GetString() == "4548-4");

        var trend = hba1c.GetProperty("trend").EnumerateArray().Select(t => t.GetProperty("deger").GetDecimal()).ToArray();
        Assert.Equal([6.6m, 7.0m, 7.4m], trend);
        Assert.Equal("Yuksek", hba1c.GetProperty("son").GetProperty("isaret").GetString());
        Assert.EndsWith("Z", hba1c.GetProperty("son").GetProperty("zaman").GetString(), StringComparison.Ordinal);

        var hb = satirlar.EnumerateArray().Single(s => s.GetProperty("loincKodu").GetString() == "718-7");
        Assert.Equal(12.0m, hb.GetProperty("altSinir").GetDecimal()); // kadın aralığı
        Assert.Equal("Normal", hb.GetProperty("son").GetProperty("isaret").GetString());
    }

    [Fact]
    public async Task Slotlar_yalnizca_bos_ve_gelecekteki_saatleri_doner()
    {
        var slotlar = await Getir($"/api/slotlar?bransId={DemoVeriTohumu.BransId(Domain.BransKodu.Endokrinoloji)}");

        Assert.NotEmpty(slotlar.EnumerateArray());
        Assert.All(slotlar.EnumerateArray(), s =>
        {
            Assert.Equal(SenaryoIdleri.HekimEndokrinoloji, s.GetProperty("hekimId").GetGuid());
            Assert.True(s.GetProperty("baslangic").GetDateTime() >= KlinikApiFabrikasi.Simdi.UtcDateTime);
        });
    }

    [Fact]
    public async Task Slotlar_dolu_slotlari_icermez()
    {
        var hekimId = SenaryoIdleri.HekimEndokrinoloji;
        using var kapsam = fabrika.Services.CreateScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var dolular = db.Slotlar.Where(s => s.HekimId == hekimId && s.Dolu).Select(s => s.Id).ToHashSet();
        Assert.NotEmpty(dolular);

        var slotlar = await Getir($"/api/slotlar?bransId={DemoVeriTohumu.BransId(Domain.BransKodu.Endokrinoloji)}");

        Assert.DoesNotContain(slotlar.EnumerateArray(), s => dolular.Contains(s.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task Slotlar_tarih_filtresi_Istanbul_gunune_gore_calisir()
    {
        var endo = DemoVeriTohumu.BransId(Domain.BransKodu.Endokrinoloji);

        // Sabit saat 29.10.2026 14:00 İstanbul; 8 saatlik slotlar 22:00'de biter → hepsi 29.10'da.
        var bugun = await Getir($"/api/slotlar?bransId={endo}&tarih=2026-10-29");
        var yarin = await Getir($"/api/slotlar?bransId={endo}&tarih=2026-10-30");

        Assert.NotEmpty(bugun.EnumerateArray());
        Assert.Empty(yarin.EnumerateArray());
    }

    [Theory]
    [InlineData("10/29/2026")]
    [InlineData("29.10.2026")]
    [InlineData("2026-13-01")]
    public async Task Slotlar_yyyy_MM_dd_disindaki_tarihi_400_ile_reddeder(string tarih)
    {
        var endo = DemoVeriTohumu.BransId(Domain.BransKodu.Endokrinoloji);

        var yanit = await _istemci.GetAsync($"/api/slotlar?bransId={endo}&tarih={Uri.EscapeDataString(tarih)}");

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }

    [Fact]
    public async Task Hekimi_olmayan_bolumde_slot_listesi_bos()
    {
        var slotlar = await Getir($"/api/slotlar?bransId={DemoVeriTohumu.BransId(Domain.BransKodu.Dermatoloji)}");

        Assert.Empty(slotlar.EnumerateArray());
    }

    [Fact]
    public async Task Kimlikler_hasta_rolunde_6_senaryo_hastasini_Ayse_ilk_doner()
    {
        var kimlikler = await Getir("/api/demo/kimlikler?rol=hasta");

        Assert.Equal(6, kimlikler.GetArrayLength());
        Assert.Equal(SenaryoIdleri.Ayse, kimlikler[0].GetProperty("id").GetGuid());
        Assert.Equal("Ayşe K.", kimlikler[0].GetProperty("ad").GetString());
    }

    [Fact]
    public async Task Kimlikler_doktor_rolunde_6_hekim_doner()
    {
        var kimlikler = await Getir("/api/demo/kimlikler?rol=doktor");

        Assert.Equal(6, kimlikler.GetArrayLength());
    }

    [Theory]
    [InlineData("/api/hastalar/00000000-0000-0000-0000-000000000000/ozet")]
    [InlineData("/api/hastalar/00000000-0000-0000-0000-000000000000/tahliller")]
    [InlineData("/api/slotlar?bransId=00000000-0000-0000-0000-000000000000")]
    public async Task Bilinmeyen_kayit_404_ProblemDetails_doner(string yol)
    {
        var yanit = await _istemci.GetAsync(yol);

        Assert.Equal(HttpStatusCode.NotFound, yanit.StatusCode);
        Assert.Equal("application/problem+json", yanit.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Gecersiz_rol_400_doner()
    {
        var yanit = await _istemci.GetAsync("/api/demo/kimlikler?rol=yonetici");

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }

    private async Task<JsonElement> Getir(string yol)
    {
        var yanit = await _istemci.GetAsync(yol);
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        return JsonDocument.Parse(await yanit.Content.ReadAsStringAsync()).RootElement;
    }
}
