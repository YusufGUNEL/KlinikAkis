using System.Net;
using System.Net.Http.Json;

namespace KlinikAkis.Tests;

public sealed class SaglikTestleri(KlinikApiFabrikasi fabrika) : IClassFixture<KlinikApiFabrikasi>
{
    [Fact]
    public async Task Health_200_ve_ok_doner()
    {
        var istemci = fabrika.CreateClient();

        var yanit = await istemci.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        var govde = await yanit.Content.ReadFromJsonAsync<SaglikYaniti>();
        Assert.NotNull(govde);
        Assert.Equal("ok", govde.Durum);
        Assert.Equal(DateTimeKind.Utc, govde.Zaman.Kind);
        // Test, geçici veritabanına bağlanmış olmalı (geliştirme .db dosyasına değil).
        Assert.True(File.Exists(fabrika.DbYolu));
    }
}
