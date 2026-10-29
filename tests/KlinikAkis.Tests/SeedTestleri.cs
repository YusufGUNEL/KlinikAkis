using System.Net;
using System.Text.Json;
using KlinikAkis.Domain;
using KlinikAkis.Infrastructure.Seed;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlinikAkis.Tests;

/// <summary>SPEC §11 test 10: seed deterministik ve tüm seed TC'leri sağlamadan geçer.</summary>
public sealed class SeedTestleri(KlinikApiFabrikasi fabrika) : IClassFixture<KlinikApiFabrikasi>
{
    [Fact]
    public async Task Iki_reset_ayni_Idleri_ve_degerleri_uretir()
    {
        var istemci = fabrika.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await istemci.PostAsync("/api/demo/reset", null)).StatusCode);
        var ilk = await AnlikGoruntu();
        Assert.Equal(HttpStatusCode.OK, (await istemci.PostAsync("/api/demo/reset", null)).StatusCode);
        var ikinci = await AnlikGoruntu();

        Assert.Equal(ilk, ikinci);
    }

    [Fact]
    public async Task Reset_degistirilmis_veriyi_seed_durumuna_dondurur()
    {
        var istemci = fabrika.CreateClient();
        await istemci.PostAsync("/api/demo/reset", null);
        var once = await AnlikGoruntu();

        await KapsamIcinde(async db =>
        {
            var ayse = await db.Hastalar.SingleAsync(h => h.Id == SenaryoIdleri.Ayse);
            ayse.Alerjiler = [];
            db.Slotlar.First(s => !s.Dolu).Dolu = true;
            await db.SaveChangesAsync();
        });
        Assert.NotEqual(once, await AnlikGoruntu());

        await istemci.PostAsync("/api/demo/reset", null);

        Assert.Equal(once, await AnlikGoruntu());
    }

    [Fact]
    public async Task Tum_seed_TCleri_saglamadan_gecer_ve_99_ile_baslar()
    {
        await fabrika.CreateClient().PostAsync("/api/demo/reset", null);

        var tcler = await KapsamIcinde(db => db.Hastalar.Select(h => h.TcKimlik).ToListAsync());

        Assert.Equal(46, tcler.Count);
        Assert.All(tcler, tc =>
        {
            Assert.True(TcKimlik.GecerliMi(tc), $"Geçersiz TC: {tc}");
            Assert.StartsWith("99", tc, StringComparison.Ordinal);
        });
        Assert.Equal(tcler.Count, tcler.Distinct().Count());
    }

    [Fact]
    public async Task Seed_senaryo_gereksinimlerini_karsilar()
    {
        await fabrika.CreateClient().PostAsync("/api/demo/reset", null);

        await KapsamIcinde(async db =>
        {
            var ayse = await db.Hastalar.SingleAsync(h => h.Id == SenaryoIdleri.Ayse);
            Assert.Equal("P-7F3A", ayse.TakmaAd);
            Assert.Equal(["Penisilin"], ayse.Alerjiler);
            Assert.Equal(DateTimeKind.Utc, ayse.AcikRizaZamani!.Value.Kind);

            // Son HbA1c bu sabah 08:15 İstanbul (= 05:15 UTC) ve kodla ▲ Yuksek.
            var sonHba1c = await db.TahlilSonuclari
                .Where(s => s.HastaId == SenaryoIdleri.Ayse && s.TahlilTanimiId == SenaryoIdleri.HbA1c)
                .OrderByDescending(s => s.Zaman).FirstAsync();
            Assert.Equal(7.4m, sonHba1c.Deger);
            Assert.Equal(new DateTime(2026, 10, 29, 5, 15, 0, DateTimeKind.Utc), sonHba1c.Zaman);
            Assert.Equal(Isaret.Yuksek, sonHba1c.Isaret);

            // Slotlar: şimdiden (14:00 İstanbul) itibaren 8 saat, 10 dk, 6 hekim × 48.
            var slotlar = await db.Slotlar.ToListAsync();
            Assert.Equal(6 * 48, slotlar.Count);
            Assert.Equal(KlinikApiFabrikasi.Simdi.UtcDateTime, slotlar.Min(s => s.Baslangic));
            Assert.Contains(slotlar, s => s.Dolu);
            Assert.Contains(slotlar, s => !s.Dolu && s.HekimId == SenaryoIdleri.HekimEndokrinoloji);

            // Her dolu slotta bir dolgu randevu var; senaryo hastalarının randevusu yok.
            var randevular = await db.Randevular.ToListAsync();
            Assert.Equal(slotlar.Count(s => s.Dolu), randevular.Count);
            Assert.DoesNotContain(randevular, r => SenaryoIdleri.Hastalar.Contains(r.HastaId));
        });
    }

    /// <summary>Seed'in yazdığı tüm tabloların Id'leri, değerleri ve zamanları; Id sırasıyla.</summary>
    private Task<string> AnlikGoruntu() => KapsamIcinde(async db => JsonSerializer.Serialize(new
    {
        Branslar = await db.Branslar.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Hekimler = await db.Hekimler.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Hastalar = await db.Hastalar.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Tanimlar = await db.TahlilTanimlari.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Sonuclar = await db.TahlilSonuclari.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Slotlar = await db.Slotlar.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Randevular = await db.Randevular.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
    }));

    private async Task<T> KapsamIcinde<T>(Func<KlinikDbContext, Task<T>> islem)
    {
        using var kapsam = fabrika.Services.CreateScope();
        return await islem(kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>());
    }

    private async Task KapsamIcinde(Func<KlinikDbContext, Task> islem)
    {
        using var kapsam = fabrika.Services.CreateScope();
        await islem(kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>());
    }
}
