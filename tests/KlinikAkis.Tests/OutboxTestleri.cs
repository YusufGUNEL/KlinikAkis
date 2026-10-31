using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlinikAkis.Domain;
using KlinikAkis.Infrastructure.Outbox;
using KlinikAkis.Infrastructure.Seed;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlinikAkis.Tests;

/// <summary>
/// e-Nabız gönderim kutusu (SPEC §5.2, §13.7c): kabul → 101, kapat → 103, yayınla → tek 105; 2 sn sonra gönderim;
/// sıfırlamadan sonraki ilk 105 iş kuralı hatası, "Yeniden dene" ile geçer. Gönderim zamanı testte elle verilir
/// (sabit saat: arka plandaki işleyici test paketlerine hiç dokunmaz).
/// </summary>
public sealed class OutboxTestleri(KlinikApiFabrikasi fabrika) : IClassFixture<KlinikApiFabrikasi>
{
    private static DateTime Simdi => KlinikApiFabrikasi.Simdi.UtcDateTime;

    [Fact]
    public async Task Kabul_kapat_yayinla_paket_yazar_ilk_105_hata_yeniden_dene_gecer()
    {
        await SifirlaAsync();
        var takmaAd = await TakmaAdAsync(SenaryoIdleri.Ayse);
        var kabul = await fabrika.GirisYapAsync("kabul");
        var hekim = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimEndokrinoloji);
        var randevuId = await RandevuAlAsync(SenaryoIdleri.Ayse, SenaryoIdleri.HekimEndokrinoloji);

        (await kabul.PostAsync($"/api/randevular/{randevuId}/kabul", null)).EnsureSuccessStatusCode();
        foreach (var eylem in new[] { "cagir", "basla", "kapat" })
            (await hekim.PostAsync($"/api/randevular/{randevuId}/{eylem}", null)).EnsureSuccessStatusCode();
        var hba1c = await SonSonucAsync(hekim, SenaryoIdleri.Ayse, "4548-4");
        var glukoz = await SonSonucAsync(hekim, SenaryoIdleri.Ayse, "1558-6");
        (await hekim.PostAsJsonAsync("/api/tahliller/yayinla", new
        {
            sonuclar = new[] { new { sonucId = hba1c, aciklama = "Şeker ortalaması yüksek." }, new { sonucId = glukoz, aciklama = "Açlık şekeri yüksek." } },
        })).EnsureSuccessStatusCode();

        var paketler = await PaketlerAsync(takmaAd);
        Assert.Equal([PaketTipi.HastaKayit, PaketTipi.Muayene, PaketTipi.LabSonuc], paketler.Select(p => p.PaketTipi).Order());
        Assert.All(paketler, p => Assert.Equal(OutboxDurumu.Bekliyor, p.Durum));
        // Paketlerde kimlik yok: ad, soyad, TC ve hasta Id'si hiçbir payload'da geçmez (yalnızca takma ad).
        var ayse = await HastaAsync(SenaryoIdleri.Ayse);
        foreach (var iz in new[] { ayse.Ad, ayse.Soyad, ayse.TcKimlik, ayse.Id.ToString() })
            Assert.All(paketler, p => Assert.DoesNotContain(iz, p.PayloadJson, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("\"takipNo\"", paketler.Single(p => p.PaketTipi == PaketTipi.HastaKayit).PayloadJson, StringComparison.Ordinal);
        Assert.Contains("\"epikrizOnayli\":false", paketler.Single(p => p.PaketTipi == PaketTipi.Muayene).PayloadJson, StringComparison.Ordinal);
        // Yayın başına tek 105: iki sonuç aynı pakette.
        var lab = paketler.Single(p => p.PaketTipi == PaketTipi.LabSonuc);
        Assert.Contains("4548-4", lab.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("1558-6", lab.PayloadJson, StringComparison.Ordinal);

        // 2 sn dolmadan gönderilmez.
        Assert.Equal(0, await IsleAsync(Simdi.AddSeconds(1)));
        Assert.Equal(3, await IsleAsync(Simdi.AddSeconds(3)));
        paketler = await PaketlerAsync(takmaAd);
        Assert.Equal(OutboxDurumu.Gonderildi, paketler.Single(p => p.PaketTipi == PaketTipi.HastaKayit).Durum);
        Assert.Equal(OutboxDurumu.Gonderildi, paketler.Single(p => p.PaketTipi == PaketTipi.Muayene).Durum);
        lab = paketler.Single(p => p.PaketTipi == PaketTipi.LabSonuc);
        Assert.Equal(OutboxDurumu.IsKuraliHatasi, lab.Durum);
        Assert.Equal(OutboxGonderici.UcumHatasi, lab.Hata);

        // Liste: kabul ve doktor görür, takma adla süzülür.
        var liste = await kabul.GetFromJsonAsync<JsonElement>($"/api/outbox?takmaAd={takmaAd}");
        Assert.Equal(3, liste.GetArrayLength());
        Assert.Contains(liste.EnumerateArray(), s => s.GetProperty("durum").GetString() == "IsKuraliHatasi");

        // Gönderilmiş paket yeniden denenmez; hatalı paket Bekliyor'a döner ve bir sonraki işlemede gider.
        var gonderilmis = paketler.Single(p => p.PaketTipi == PaketTipi.HastaKayit).Id;
        Assert.Equal(HttpStatusCode.Conflict, (await kabul.PostAsync($"/api/outbox/{gonderilmis}/yeniden-dene", null)).StatusCode);
        (await hekim.PostAsync($"/api/outbox/{lab.Id}/yeniden-dene", null)).EnsureSuccessStatusCode();
        Assert.Equal(1, await IsleAsync(Simdi.AddSeconds(4)));
        lab = (await PaketlerAsync(takmaAd)).Single(p => p.PaketTipi == PaketTipi.LabSonuc);
        Assert.Equal(OutboxDurumu.Gonderildi, lab.Durum);
        Assert.Equal(2, lab.Deneme);
        Assert.Null(lab.Hata);
    }

    [Fact]
    public async Task Sifirlamadan_sonra_ilk_105_yine_hata_verir_sonraki_gecer()
    {
        // İki tur: her sıfırlamadan sonra ilk 105 hata, ikinci 105 doğrudan geçer (deterministik, bellek bayrağı yok).
        for (var tur = 0; tur < 2; tur++)
        {
            await SifirlaAsync();
            var hekim = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimEndokrinoloji);
            await YayinlaAsync(hekim, SenaryoIdleri.Ayse, "4548-4");
            await IsleAsync(Simdi.AddSeconds(3));
            await YayinlaAsync(hekim, SenaryoIdleri.Elif, "3016-3");
            await IsleAsync(Simdi.AddSeconds(3));

            Assert.Equal(OutboxDurumu.IsKuraliHatasi, (await PaketlerAsync(await TakmaAdAsync(SenaryoIdleri.Ayse))).Single().Durum);
            Assert.Equal(OutboxDurumu.Gonderildi, (await PaketlerAsync(await TakmaAdAsync(SenaryoIdleri.Elif))).Single().Durum);
        }
    }

    [Theory]
    [InlineData("hasta")]
    [InlineData("tv")]
    public async Task Hasta_ve_tv_gonderim_kutusunu_goremez(string rol)
    {
        Guid? kimlik = rol == "hasta" ? SenaryoIdleri.Ayse : await BransAsync(SenaryoIdleri.HekimEndokrinoloji);
        var istemci = await fabrika.GirisYapAsync(rol, kimlik);

        Assert.Equal(HttpStatusCode.Forbidden, (await istemci.GetAsync("/api/outbox")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await istemci.PostAsync($"/api/outbox/{Guid.NewGuid()}/yeniden-dene", null)).StatusCode);
    }

    private async Task SifirlaAsync() =>
        (await fabrika.CreateClient().PostAsync("/api/demo/reset", null)).EnsureSuccessStatusCode();

    private async Task<int> IsleAsync(DateTime simdi)
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        return await new OutboxGonderici(kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>()).IsleAsync(simdi);
    }

    private async Task<List<Domain.Varliklar.OutboxMesaji>> PaketlerAsync(string takmaAd)
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        return await db.OutboxMesajlari.AsNoTracking().Where(o => o.HastaTakmaAdi == takmaAd).ToListAsync();
    }

    private async Task<Domain.Varliklar.Hasta> HastaAsync(Guid hastaId)
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        return await kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>().Hastalar.AsNoTracking().FirstAsync(h => h.Id == hastaId);
    }

    private async Task<string> TakmaAdAsync(Guid hastaId)
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        return await db.Hastalar.Where(h => h.Id == hastaId).Select(h => h.TakmaAd).FirstAsync();
    }

    private async Task<Guid> BransAsync(Guid hekimId)
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        return (await db.Hekimler.FindAsync(hekimId))!.BransId;
    }

    private async Task<Guid> RandevuAlAsync(Guid hastaId, Guid hekimId)
    {
        Guid slotId;
        await using (var kapsam = fabrika.Services.CreateAsyncScope())
        {
            var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
            slotId = await db.Slotlar.AsNoTracking().Where(s => s.HekimId == hekimId && !s.Dolu && s.Baslangic > Simdi)
                .OrderBy(s => s.Baslangic).Select(s => s.Id).FirstAsync();
        }
        var hasta = await fabrika.GirisYapAsync("hasta", hastaId);
        var yanit = await hasta.PostAsJsonAsync("/api/randevular", new { slotId });
        yanit.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await yanit.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task YayinlaAsync(HttpClient hekim, Guid hastaId, string loinc)
    {
        var id = await SonSonucAsync(hekim, hastaId, loinc);
        (await hekim.PostAsJsonAsync("/api/tahliller/yayinla", new { sonuclar = new[] { new { sonucId = id, aciklama = "Açıklama." } } }))
            .EnsureSuccessStatusCode();
    }

    private static async Task<Guid> SonSonucAsync(HttpClient hekim, Guid hastaId, string loinc)
    {
        var satirlar = JsonDocument.Parse(await hekim.GetStringAsync($"/api/hastalar/{hastaId}/tahliller")).RootElement;
        return satirlar.EnumerateArray().Single(s => s.GetProperty("loincKodu").GetString() == loinc)
            .GetProperty("son").GetProperty("id").GetGuid();
    }
}
