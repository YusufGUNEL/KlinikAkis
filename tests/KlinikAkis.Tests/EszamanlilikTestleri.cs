using KlinikAkis.Domain;
using KlinikAkis.Domain.Varliklar;
using KlinikAkis.Infrastructure.Seed;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlinikAkis.Tests;

/// <summary>
/// Oku-kontrol-yaz uçlarında eşzamanlı ikinci yazım reddedilir (Aşama 9 kod incelemesi): iki ayrı bağlam aynı satırı
/// okuyup değiştirince ikinci kayıt DbUpdateConcurrencyException alır; uç bunu 409'a çevirir.
/// </summary>
public sealed class EszamanlilikTestleri(KlinikApiFabrikasi fabrika) : IClassFixture<KlinikApiFabrikasi>
{
    [Fact]
    public async Task Ayni_tahlil_iki_kez_eszamanli_yayinlanamaz()
    {
        await using var k1 = fabrika.Services.CreateAsyncScope();
        await using var k2 = fabrika.Services.CreateAsyncScope();
        var db1 = k1.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var db2 = k2.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var id = await db1.TahlilSonuclari.Where(s => s.HastaId == SenaryoIdleri.Elif && !s.HastayaYayinlandi)
            .Select(s => s.Id).FirstAsync();

        var s1 = await db1.TahlilSonuclari.FindAsync(id);
        var s2 = await db2.TahlilSonuclari.FindAsync(id);
        s1!.HastayaYayinlandi = true;
        s1.YayinlananAciklama = "ilk";
        s2!.HastayaYayinlandi = true;
        s2.YayinlananAciklama = "ikinci";
        await db1.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task Ayni_on_anamnez_iki_randevuya_eszamanli_baglanamaz()
    {
        await using var k1 = fabrika.Services.CreateAsyncScope();
        await using var k2 = fabrika.Services.CreateAsyncScope();
        var db1 = k1.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var db2 = k2.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var onAnamnez = new OnAnamnez { Id = Guid.NewGuid(), HastaId = SenaryoIdleri.Elif, Durum = OnAnamnezDurumu.Taslak };
        db1.OnAnamnezler.Add(onAnamnez);
        await db1.SaveChangesAsync();

        var o2 = await db2.OnAnamnezler.FindAsync(onAnamnez.Id);
        onAnamnez.RandevuId = Guid.NewGuid();
        o2!.RandevuId = Guid.NewGuid();
        await db1.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task Hekim_karari_ile_eszamanli_ikinci_karar_reddedilir()
    {
        await using var k1 = fabrika.Services.CreateAsyncScope();
        await using var k2 = fabrika.Services.CreateAsyncScope();
        var db1 = k1.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var db2 = k2.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var onAnamnez = new OnAnamnez { Id = Guid.NewGuid(), HastaId = SenaryoIdleri.Elif, Durum = OnAnamnezDurumu.Taslak };
        db1.OnAnamnezler.Add(onAnamnez);
        await db1.SaveChangesAsync();

        var o2 = await db2.OnAnamnezler.FindAsync(onAnamnez.Id);
        onAnamnez.Durum = OnAnamnezDurumu.Onaylandi;
        o2!.Durum = OnAnamnezDurumu.Reddedildi;
        await db1.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }
}
