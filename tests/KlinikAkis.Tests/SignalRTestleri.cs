using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using KlinikAkis.Infrastructure.Seed;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlinikAkis.Tests;

/// <summary>SPEC §11 test 11: olay doğru gruba gider, başka hekime gitmez; payload yalnızca {tur, id}.</summary>
public sealed class SignalRTestleri(KlinikApiFabrikasi fabrika) : IClassFixture<KlinikApiFabrikasi>
{
    private static readonly TimeSpan Bekleme = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Randevu_alininca_hekimin_grubuna_RandevuAlindi_gider_baska_hekime_gitmez()
    {
        var (hekimBaglantisi, hekimOlaylari) = await BaglanAsync("doktor", SenaryoIdleri.HekimEndokrinoloji);
        var (baskaBaglanti, baskaOlaylar) = await BaglanAsync("doktor", SenaryoIdleri.HekimKardiyoloji);
        await using var _1 = hekimBaglantisi;
        await using var _2 = baskaBaglanti;

        var hasta = await fabrika.GirisYapAsync("hasta", SenaryoIdleri.Ayse);
        var yanit = await hasta.PostAsJsonAsync("/api/randevular", new { slotId = await BosSlotAsync(SenaryoIdleri.HekimEndokrinoloji) });
        yanit.EnsureSuccessStatusCode();
        var randevuId = JsonDocument.Parse(await yanit.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        using var zaman = new CancellationTokenSource(Bekleme);
        var olay = await hekimOlaylari.Reader.ReadAsync(zaman.Token);
        Assert.Equal("RandevuAlindi", olay.GetProperty("tur").GetString());
        Assert.Equal(randevuId, olay.GetProperty("id").GetGuid());
        Assert.Equal(["tur", "id"], olay.EnumerateObject().Select(p => p.Name));

        // Kardiyoloji hekimine hiçbir olay gitmemeli: kısa bir süre bekleyip boş olduğunu doğrula.
        await Task.Delay(500);
        Assert.False(baskaOlaylar.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Hasta_cagrilinca_tv_ve_hasta_gruplarina_HastaCagrildi_gider()
    {
        var bransId = DemoVeriTohumu.BransId(Domain.BransKodu.Endokrinoloji);
        var (tvBaglantisi, tvOlaylari) = await BaglanAsync("tv", bransId);
        var (hastaBaglantisi, hastaOlaylari) = await BaglanAsync("hasta", SenaryoIdleri.Mehmet);
        await using var _1 = tvBaglantisi;
        await using var _2 = hastaBaglantisi;

        var hasta = await fabrika.GirisYapAsync("hasta", SenaryoIdleri.Mehmet);
        var alinan = await hasta.PostAsJsonAsync("/api/randevular", new { slotId = await BosSlotAsync(SenaryoIdleri.HekimEndokrinoloji) });
        var randevuId = JsonDocument.Parse(await alinan.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        (await (await fabrika.GirisYapAsync("kabul")).PostAsync($"/api/randevular/{randevuId}/kabul", null)).EnsureSuccessStatusCode();
        var hekim = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimEndokrinoloji);
        (await hekim.PostAsync($"/api/randevular/{randevuId}/cagir", null)).EnsureSuccessStatusCode();

        using var zaman = new CancellationTokenSource(Bekleme);
        Assert.Equal("HastaCagrildi", (await tvOlaylari.Reader.ReadAsync(zaman.Token)).GetProperty("tur").GetString());
        // Hasta sırayla RandevuAlindi → KabulYapildi → HastaCagrildi alır.
        var hastaTurleri = new List<string>();
        for (var i = 0; i < 3; i++)
            hastaTurleri.Add((await hastaOlaylari.Reader.ReadAsync(zaman.Token)).GetProperty("tur").GetString()!);
        Assert.Equal(["RandevuAlindi", "KabulYapildi", "HastaCagrildi"], hastaTurleri);
    }

    [Fact]
    public async Task Tahlil_yayinlaninca_hastanin_grubuna_TahlilYayinlandi_gider_baska_hastaya_gitmez()
    {
        var (hastaBaglantisi, hastaOlaylari) = await BaglanAsync("hasta", SenaryoIdleri.Elif);
        var (baskaBaglanti, baskaOlaylar) = await BaglanAsync("hasta", SenaryoIdleri.Ayse);
        await using var _1 = hastaBaglantisi;
        await using var _2 = baskaBaglanti;

        var hekim = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimEndokrinoloji);
        var satirlar = JsonDocument.Parse(await hekim.GetStringAsync($"/api/hastalar/{SenaryoIdleri.Elif}/tahliller")).RootElement;
        var tsh = satirlar.EnumerateArray().Single(s => s.GetProperty("loincKodu").GetString() == "3016-3")
            .GetProperty("son").GetProperty("id").GetGuid();
        (await hekim.PostAsJsonAsync("/api/tahliller/yayinla",
            new { sonuclar = new[] { new { sonucId = tsh, aciklama = "Tiroid hormonunuz aralığın üstünde." } } }))
            .EnsureSuccessStatusCode();

        using var zaman = new CancellationTokenSource(Bekleme);
        var olay = await hastaOlaylari.Reader.ReadAsync(zaman.Token);
        Assert.Equal("TahlilYayinlandi", olay.GetProperty("tur").GetString());
        Assert.Equal(["tur", "id"], olay.EnumerateObject().Select(p => p.Name));
        await Task.Delay(500);
        Assert.False(baskaOlaylar.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Tokensiz_baglanti_reddedilir()
    {
        var baglanti = new HubConnectionBuilder()
            .WithUrl(new Uri(fabrika.Server.BaseAddress, "/hubs/klinik"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => fabrika.Server.CreateHandler();
            })
            .Build();
        await using var _ = baglanti;

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => baglanti.StartAsync());
    }

    private async Task<(HubConnection, Channel<JsonElement>)> BaglanAsync(string rol, Guid kimlikId)
    {
        var token = await fabrika.TokenAlAsync(rol, kimlikId);
        var baglanti = new HubConnectionBuilder()
            .WithUrl(new Uri(fabrika.Server.BaseAddress, "/hubs/klinik"), o =>
            {
                // TestServer WebSocket'i bu istemciyle taşımaz; LongPolling aynı hub/grup mantığını sınar.
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => fabrika.Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        var olaylar = Channel.CreateUnbounded<JsonElement>();
        baglanti.On<JsonElement>("olay", o => olaylar.Writer.TryWrite(o));
        await baglanti.StartAsync();
        return (baglanti, olaylar);
    }

    private async Task<Guid> BosSlotAsync(Guid hekimId)
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var simdi = KlinikApiFabrikasi.Simdi.UtcDateTime;
        return await db.Slotlar.AsNoTracking()
            .Where(s => s.HekimId == hekimId && !s.Dolu && s.Baslangic > simdi)
            .OrderBy(s => s.Baslangic).Select(s => s.Id).FirstAsync();
    }
}
