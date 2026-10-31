using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlinikAkis.Infrastructure.Seed;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace KlinikAkis.Tests;

/// <summary>Demo giriş (JWT) ve rol politikaları (SPEC §7).</summary>
public sealed class GirisTestleri(KlinikApiFabrikasi fabrika) : IClassFixture<KlinikApiFabrikasi>
{
    private readonly HttpClient _istemci = fabrika.CreateClient();

    [Fact]
    public async Task Hasta_girisi_hastaId_claimli_8_saatlik_token_doner()
    {
        var (yanit, token) = await Giris("hasta", SenaryoIdleri.Ayse);

        Assert.Equal("Ayşe K.", yanit.GetProperty("ad").GetString());
        Assert.Equal("hasta", token.GetClaim("rol").Value);
        Assert.Equal(SenaryoIdleri.Ayse.ToString(), token.GetClaim("hastaId").Value);
        Assert.False(token.TryGetClaim("hekimId", out _));
        Assert.Equal(KlinikApiFabrikasi.Simdi.UtcDateTime.AddHours(8), token.ValidTo);
    }

    [Fact]
    public async Task Doktor_girisi_hekimId_ve_bransId_claimli_token_doner()
    {
        var (yanit, token) = await Giris("doktor", SenaryoIdleri.HekimEndokrinoloji);

        Assert.StartsWith("Uzm. Dr. ", yanit.GetProperty("ad").GetString(), StringComparison.Ordinal);
        Assert.Equal(SenaryoIdleri.HekimEndokrinoloji.ToString(), token.GetClaim("hekimId").Value);
        Assert.Equal(DemoVeriTohumu.BransId(Domain.BransKodu.Endokrinoloji).ToString(), token.GetClaim("bransId").Value);
    }

    [Fact]
    public async Task Kabul_girisi_kimlik_istemez()
    {
        var (yanit, token) = await Giris("kabul", null);

        Assert.Equal("kabul", token.GetClaim("rol").Value);
        Assert.Equal(JsonValueKind.Null, yanit.GetProperty("kimlikId").ValueKind);
    }

    [Fact]
    public async Task Tv_girisi_bransId_claimli_token_doner()
    {
        var endo = DemoVeriTohumu.BransId(Domain.BransKodu.Endokrinoloji);

        var (_, token) = await Giris("tv", endo);

        Assert.Equal(endo.ToString(), token.GetClaim("bransId").Value);
    }

    [Theory]
    [InlineData("yonetici", "a1000000-0000-0000-0000-000000000001", HttpStatusCode.BadRequest)]
    [InlineData("hasta", null, HttpStatusCode.BadRequest)]
    [InlineData("hasta", "00000000-0000-0000-0000-000000000000", HttpStatusCode.NotFound)]
    // Hekim Id'si hasta rolüyle verilirse bulunamaz: kimlik rolün kendi tablosunda aranır.
    [InlineData("hasta", "b1000000-0000-0000-0000-000000000001", HttpStatusCode.NotFound)]
    public async Task Gecersiz_giris_reddedilir(string rol, string? kimlikId, HttpStatusCode beklenen)
    {
        var yanit = await _istemci.PostAsJsonAsync("/api/demo/login",
            new { rol, kimlikId = kimlikId is null ? (Guid?)null : Guid.Parse(kimlikId) });

        Assert.Equal(beklenen, yanit.StatusCode);
        Assert.Equal("application/problem+json", yanit.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Hekimi_olmayan_bolum_icin_tv_girisi_404()
    {
        var yanit = await _istemci.PostAsJsonAsync("/api/demo/login",
            new { rol = "tv", kimlikId = DemoVeriTohumu.BransId(Domain.BransKodu.Dermatoloji) });

        Assert.Equal(HttpStatusCode.NotFound, yanit.StatusCode);
    }

    [Fact]
    public async Task Doktor_ucu_tokensiz_401_hasta_tokeniyla_403_doner()
    {
        var yol = $"/api/hastalar/{SenaryoIdleri.Ayse}/ozet";

        Assert.Equal(HttpStatusCode.Unauthorized, (await _istemci.GetAsync(yol)).StatusCode);
        var hasta = await fabrika.GirisYapAsync("hasta", SenaryoIdleri.Ayse);
        Assert.Equal(HttpStatusCode.Forbidden, (await hasta.GetAsync(yol)).StatusCode);
    }

    [Fact]
    public async Task Slotlar_yalnizca_hastaya_acik()
    {
        var yol = $"/api/slotlar?bransId={DemoVeriTohumu.BransId(Domain.BransKodu.Endokrinoloji)}";
        var doktor = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimEndokrinoloji);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _istemci.GetAsync(yol)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doktor.GetAsync(yol)).StatusCode);
    }

    [Fact]
    public async Task Baska_anahtarla_imzalanmis_token_401()
    {
        var sahte = new JsonWebTokenHandler().CreateToken(new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Issuer = "klinikakis-demo",
            Audience = "klinikakis-demo",
            Claims = new Dictionary<string, object> { ["rol"] = "doktor" },
            // Süre geçerli: 401'in tek nedeni imza olsun.
            IssuedAt = KlinikApiFabrikasi.Simdi.UtcDateTime,
            NotBefore = KlinikApiFabrikasi.Simdi.UtcDateTime,
            Expires = KlinikApiFabrikasi.Simdi.UtcDateTime.AddHours(1),
            SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(new byte[32]),
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256),
        });
        using var istek = new HttpRequestMessage(HttpMethod.Get, $"/api/hastalar/{SenaryoIdleri.Ayse}/ozet");
        istek.Headers.Authorization = new("Bearer", sahte);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _istemci.SendAsync(istek)).StatusCode);
    }

    [Fact]
    public async Task Hasta_ozeti_goruntulenince_erisim_kaydi_yazilir()
    {
        var doktor = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimEndokrinoloji);

        Assert.Equal(HttpStatusCode.OK, (await doktor.GetAsync($"/api/hastalar/{SenaryoIdleri.Zeynep}/ozet")).StatusCode);

        using var kapsam = fabrika.Services.CreateScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var kayit = await db.ErisimKayitlari.SingleAsync(k => k.HastaId == SenaryoIdleri.Zeynep);
        Assert.Equal("doktor", kayit.KullaniciRol);
        Assert.StartsWith("Uzm. Dr. ", kayit.KullaniciAd, StringComparison.Ordinal);
        Assert.Equal("Hasta kaydını görüntüledi", kayit.Eylem);
        Assert.Equal(KlinikApiFabrikasi.Simdi.UtcDateTime, kayit.Zaman);
    }

    private async Task<(JsonElement Yanit, JsonWebToken Token)> Giris(string rol, Guid? kimlikId)
    {
        var yanit = await _istemci.PostAsJsonAsync("/api/demo/login", new { rol, kimlikId });
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        var govde = JsonDocument.Parse(await yanit.Content.ReadAsStringAsync()).RootElement;
        return (govde, new JsonWebToken(govde.GetProperty("token").GetString()));
    }
}
