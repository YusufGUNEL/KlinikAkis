using System.Net;
using System.Text.Json;
using KlinikAkis.Infrastructure.Seed;

namespace KlinikAkis.Tests;

/// <summary>Hasta ekranındaki ortak özet ve alerji bandının rol sınırları.</summary>
public sealed class HastaOzetiTestleri(KlinikApiFabrikasi fabrika) : IClassFixture<KlinikApiFabrikasi>
{
    [Fact]
    public async Task Hasta_kendi_ozetinde_alerjisini_gorur()
    {
        var hasta = await fabrika.GirisYapAsync("hasta", SenaryoIdleri.Ayse);

        var yanit = await hasta.GetAsync("/api/hasta/me/ozet");

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        using var json = JsonDocument.Parse(await yanit.Content.ReadAsStringAsync());
        var alerjiler = json.RootElement.GetProperty("alerjiler");
        Assert.Contains("Penisilin", alerjiler.EnumerateArray().Select(x => x.GetString()));
        Assert.True(json.RootElement.GetProperty("sentetik").GetBoolean());
    }

    [Fact]
    public async Task Hasta_ozeti_baska_rollerden_korunur()
    {
        const string yol = "/api/hasta/me/ozet";

        Assert.Equal(HttpStatusCode.Unauthorized, (await fabrika.CreateClient().GetAsync(yol)).StatusCode);

        var doktor = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimEndokrinoloji);
        Assert.Equal(HttpStatusCode.Forbidden, (await doktor.GetAsync(yol)).StatusCode);
    }
}
