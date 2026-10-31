using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlinikAkis.Infrastructure.Seed;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlinikAkis.Tests;

/// <summary>TV sıra ekranı (SPEC §13.7b, taslak 8): son 3 çağrı, yalnız kendi branşı, baş harf, rol.</summary>
public sealed class TvTestleri(KlinikApiFabrikasi fabrika) : IClassFixture<KlinikApiFabrikasi>
{
    [Fact]
    public async Task Cagrilan_hasta_tvde_bas_harf_ve_oda_ile_gorunur_tam_ad_gitmez()
    {
        var randevuId = await CagirAsync(SenaryoIdleri.Ayse, SenaryoIdleri.HekimEndokrinoloji);
        var tv = await fabrika.GirisYapAsync("tv", await BransAsync(SenaryoIdleri.HekimEndokrinoloji));

        var yanit = await tv.GetAsync("/api/tv/cagrilar");
        var govde = await yanit.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var randevu = await db.Randevular.Include(r => r.Hekim).Include(r => r.Hasta).SingleAsync(r => r.Id == randevuId);
        // Sabit test saatinde sınıftaki başka çağrılarla aynı an olabilir: satır sıra no ile bulunur.
        var ilk = JsonDocument.Parse(govde).RootElement.EnumerateArray().Single(c => c.GetProperty("siraNo").GetString() == randevu.SiraNo);
        Assert.Equal(randevu.SiraNo, ilk.GetProperty("siraNo").GetString());
        Assert.Equal(randevu.Hekim!.Oda, ilk.GetProperty("oda").GetString());
        Assert.Equal("A.K.", ilk.GetProperty("basHarfler").GetString());
        Assert.Equal(KlinikApiFabrikasi.Simdi.UtcDateTime, randevu.CagrilmaZamani);
        // Yanıtın hiçbir yerinde ad ya da soyad yok.
        Assert.DoesNotContain(randevu.Hasta!.Ad, govde, StringComparison.Ordinal);
        Assert.DoesNotContain(randevu.Hasta.Soyad, govde, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tv_yalnizca_kendi_bransinin_cagrilarini_gorur()
    {
        await CagirAsync(SenaryoIdleri.Mehmet, SenaryoIdleri.HekimEndokrinoloji);
        var kardiyolojiTv = await fabrika.GirisYapAsync("tv", await BransAsync(SenaryoIdleri.HekimKardiyoloji));

        var liste = await kardiyolojiTv.GetFromJsonAsync<JsonElement>("/api/tv/cagrilar");

        Assert.Equal(0, liste.GetArrayLength());
    }

    [Fact]
    public async Task En_fazla_uc_cagri_cagri_anina_gore_en_yenisi_ilk()
    {
        // Sabit test saatinde tüm çağrılar aynı anda olur; sırayı sınamak için çağrı anları elle ayrılır.
        Guid[] hastalar = [SenaryoIdleri.Zeynep, SenaryoIdleri.Hasan, SenaryoIdleri.Elif, SenaryoIdleri.Can];
        var idler = new List<Guid>();
        foreach (var h in hastalar) idler.Add(await CagirAsync(h, SenaryoIdleri.HekimKbb));
        await using (var kapsam = fabrika.Services.CreateAsyncScope())
        {
            var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
            // Slot sırasının tersi: ilk alınan randevu en son çağrılmış olsun.
            for (var i = 0; i < idler.Count; i++)
                (await db.Randevular.FindAsync(idler[i]))!.CagrilmaZamani = KlinikApiFabrikasi.Simdi.UtcDateTime.AddMinutes(-i);
            await db.SaveChangesAsync();
        }
        var tv = await fabrika.GirisYapAsync("tv", await BransAsync(SenaryoIdleri.HekimKbb));

        var liste = (await tv.GetFromJsonAsync<JsonElement>("/api/tv/cagrilar")).EnumerateArray().ToList();

        Assert.Equal(3, liste.Count);
        await using var okuma = fabrika.Services.CreateAsyncScope();
        var okuDb = okuma.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var siraNolari = await okuDb.Randevular.Where(r => idler.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.SiraNo);
        // En yeni ilk: idler[0] (şimdi), idler[1] (-1 dk), idler[2] (-2 dk); idler[3] (-3 dk) listede yok.
        Assert.Equal([siraNolari[idler[0]], siraNolari[idler[1]], siraNolari[idler[2]]], liste.Select(c => c.GetProperty("siraNo").GetString()));
    }

    [Theory]
    [InlineData("doktor")]
    [InlineData("kabul")]
    [InlineData("hasta")]
    public async Task Tv_disi_rol_erisemez(string rol)
    {
        Guid? kimlik = rol switch
        {
            "doktor" => SenaryoIdleri.HekimEndokrinoloji,
            "hasta" => SenaryoIdleri.Ayse,
            _ => null,
        };
        var istemci = await fabrika.GirisYapAsync(rol, kimlik);

        Assert.Equal(HttpStatusCode.Forbidden, (await istemci.GetAsync("/api/tv/cagrilar")).StatusCode);
    }

    private async Task<Guid> BransAsync(Guid hekimId)
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        return (await db.Hekimler.FindAsync(hekimId))!.BransId;
    }

    /// <summary>Randevu al → kabul → çağır; randevu Id'si döner.</summary>
    private async Task<Guid> CagirAsync(Guid hastaId, Guid hekimId)
    {
        var hasta = await fabrika.GirisYapAsync("hasta", hastaId);
        Guid slotId;
        await using (var kapsam = fabrika.Services.CreateAsyncScope())
        {
            var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
            var simdi = KlinikApiFabrikasi.Simdi.UtcDateTime;
            slotId = await db.Slotlar.AsNoTracking().Where(s => s.HekimId == hekimId && !s.Dolu && s.Baslangic > simdi)
                .OrderBy(s => s.Baslangic).Select(s => s.Id).FirstAsync();
        }
        var alindi = await hasta.PostAsJsonAsync("/api/randevular", new { slotId });
        alindi.EnsureSuccessStatusCode();
        var id = JsonDocument.Parse(await alindi.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var kabul = await fabrika.GirisYapAsync("kabul");
        (await kabul.PostAsync($"/api/randevular/{id}/kabul", null)).EnsureSuccessStatusCode();
        var hekim = await fabrika.GirisYapAsync("doktor", hekimId);
        (await hekim.PostAsync($"/api/randevular/{id}/cagir", null)).EnsureSuccessStatusCode();
        return id;
    }
}
