using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlinikAkis.Domain;
using KlinikAkis.Infrastructure.Seed;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlinikAkis.Tests;

/// <summary>Hasta özet onayı (taslak 2), hekim kararı (§10.2), erişim kaydı ve yetkiler.</summary>
public sealed class OnAnamnezKararTestleri(KlinikApiFabrikasi fabrika) : IClassFixture<KlinikApiFabrikasi>
{
    [Fact]
    public async Task Hasta_duzelt_derse_not_zorunlu_not_hekime_gider()
    {
        var (hasta, id) = await OzetHazirAsync(SenaryoIdleri.Ayse);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await hasta.PostAsJsonAsync($"/api/onanamnez/{id}/hasta-onayi", new { dogru = false })).StatusCode);
        (await hasta.PostAsJsonAsync($"/api/onanamnez/{id}/hasta-onayi", new { dogru = false, not = "Halsizlik yok, sadece susama" }))
            .EnsureSuccessStatusCode();

        var doktor = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimEndokrinoloji);
        var detay = await Json(await doktor.GetAsync($"/api/onanamnez/{id}"));
        Assert.Equal("Halsizlik yok, sadece susama", detay.GetProperty("hastaNotu").GetString());
        Assert.False(detay.GetProperty("hastaOnayi").GetBoolean());
        Assert.Equal(6, detay.GetProperty("sohbet").GetArrayLength());
        Assert.Equal("Taslak", detay.GetProperty("kart").GetProperty("durum").GetString());
    }

    [Fact]
    public async Task Baska_hasta_onaylayamaz_hasta_karar_veremez()
    {
        var (_, id) = await OzetHazirAsync(SenaryoIdleri.Mehmet);
        var zeynep = await fabrika.GirisYapAsync("hasta", SenaryoIdleri.Zeynep);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await zeynep.PostAsJsonAsync($"/api/onanamnez/{id}/hasta-onayi", new { dogru = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await zeynep.PostAsJsonAsync($"/api/onanamnez/{id}/karar", new { karar = "onayla" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await zeynep.GetAsync($"/api/onanamnez/{id}")).StatusCode);
    }

    [Fact]
    public async Task Hekim_duzenleyip_onaylar_erisim_kaydi_yazilir_ikinci_karar_409()
    {
        var (_, id) = await OzetHazirAsync(SenaryoIdleri.Elif);
        var doktor = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimEndokrinoloji);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await doktor.PostAsJsonAsync($"/api/onanamnez/{id}/karar", new { karar = "duzenle" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await doktor.PostAsJsonAsync($"/api/onanamnez/{id}/karar", new { karar = "sil" })).StatusCode);

        var yanit = await doktor.PostAsJsonAsync($"/api/onanamnez/{id}/karar",
            new { karar = "duzenle", metin = "Kilo alma ve üşüme; TSH yüksekliği öyküsü." });
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        var detay = await Json(yanit);
        Assert.Equal("Duzenlendi", detay.GetProperty("durum").GetString());
        var karar = detay.GetProperty("karar");
        Assert.Equal("Kilo alma ve üşüme; TSH yüksekliği öyküsü.", karar.GetProperty("duzeltme").GetString());
        Assert.Contains("Selin Arslan", karar.GetProperty("hekimAd").GetString(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.Conflict,
            (await doktor.PostAsJsonAsync($"/api/onanamnez/{id}/karar", new { karar = "onayla" })).StatusCode);

        var erisim = await Json(await doktor.GetAsync($"/api/hastalar/{SenaryoIdleri.Elif}/erisim"));
        Assert.Equal("Ön anamnezi düzenledi", erisim[0].GetProperty("eylem").GetString());
        Assert.Equal("doktor", erisim[0].GetProperty("kullaniciRol").GetString());
    }

    [Fact]
    public async Task Randevu_satiri_on_anamnezi_gosterir()
    {
        var (hasta, id) = await OzetHazirAsync(SenaryoIdleri.Zeynep);
        await using (var kapsam = fabrika.Services.CreateAsyncScope())
        {
            var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
            var simdi = KlinikApiFabrikasi.Simdi.UtcDateTime;
            var slotId = await db.Slotlar.Where(s => s.HekimId == SenaryoIdleri.HekimDahiliye1 && !s.Dolu && s.Baslangic > simdi)
                .OrderBy(s => s.Baslangic).Select(s => s.Id).FirstAsync();
            (await hasta.PostAsJsonAsync("/api/randevular", new { slotId, onAnamnezId = id })).EnsureSuccessStatusCode();
        }

        var doktor = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimDahiliye1);
        var liste = await Json(await doktor.GetAsync("/api/randevular"));
        Assert.Contains(liste.EnumerateArray(), r =>
            r.GetProperty("onAnamnezId").ValueKind == JsonValueKind.String && r.GetProperty("onAnamnezId").GetGuid() == id);
    }

    [Fact]
    public async Task Iptal_edilen_randevunun_on_anamneziyle_yeniden_randevu_alinir()
    {
        var (hasta, id) = await OzetHazirAsync(SenaryoIdleri.Ayse);
        Guid[] slotlar;
        await using (var kapsam = fabrika.Services.CreateAsyncScope())
        {
            var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
            var simdi = KlinikApiFabrikasi.Simdi.UtcDateTime;
            slotlar = await db.Slotlar.Where(s => s.HekimId == SenaryoIdleri.HekimDahiliye1 && !s.Dolu && s.Baslangic > simdi)
                .OrderBy(s => s.Baslangic).Select(s => s.Id).Take(2).ToArrayAsync();
        }
        var ilk = await Json(await hasta.PostAsJsonAsync("/api/randevular", new { slotId = slotlar[0], onAnamnezId = id }));
        var kabul = await fabrika.GirisYapAsync("kabul");
        (await kabul.PostAsync($"/api/randevular/{ilk.GetProperty("id").GetGuid()}/iptal", null)).EnsureSuccessStatusCode();

        var yeniden = await hasta.PostAsJsonAsync("/api/randevular", new { slotId = slotlar[1], onAnamnezId = id });

        Assert.Equal(HttpStatusCode.Created, yeniden.StatusCode);
    }

    // Senaryo 2 (Fake): boğaz ağrısı + 38 °C → KBB önerisi, alternatif Dahiliye, aciliyet kodda Öncelikli.
    [Fact]
    public async Task Senaryo_2_bogaz_agrisi_KBB_onerir()
    {
        var (_, kart) = await OzetHazirAsync(SenaryoIdleri.Mehmet, AiHattiTestleri.MehmetSohbeti);

        Assert.Equal("Kbb", kart.GetProperty("onerilen").GetProperty("kod").GetString());
        Assert.Equal("Dahiliye", kart.GetProperty("alternatif").GetProperty("kod").GetString());
        var doktor = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimKbb);
        var detay = await Json(await doktor.GetAsync($"/api/onanamnez/{kart.GetProperty("id").GetGuid()}"));
        Assert.Equal("Oncelikli", detay.GetProperty("kart").GetProperty("taslak").GetProperty("aciliyet").GetString());
    }

    private async Task<(HttpClient Hasta, Guid Id)> OzetHazirAsync(Guid hastaId)
    {
        var (hasta, kart) = await OzetHazirAsync(hastaId, AiHattiTestleri.AyseSohbeti);
        return (hasta, kart.GetProperty("id").GetGuid());
    }

    private async Task<(HttpClient Hasta, JsonElement Kart)> OzetHazirAsync(Guid hastaId, IReadOnlyList<SohbetKaydi> sohbet)
    {
        var hasta = await fabrika.GirisYapAsync("hasta", hastaId);
        var baslat = await Json(await hasta.PostAsync("/api/onanamnez", null));
        var id = baslat.GetProperty("onAnamnezId").GetGuid();
        foreach (var k in sohbet)
            (await hasta.PostAsJsonAsync($"/api/onanamnez/{id}/cevap", new { secimler = k.Secimler, metin = k.Metin }))
                .EnsureSuccessStatusCode();
        var ozet = await Json(await hasta.PostAsync($"/api/onanamnez/{id}/ozet", null));
        Assert.Equal(nameof(OnAnamnezDurumu.Taslak), ozet.GetProperty("durum").GetString());
        return (hasta, ozet.GetProperty("kart"));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage yanit) =>
        JsonDocument.Parse(await yanit.Content.ReadAsStringAsync()).RootElement;
}
