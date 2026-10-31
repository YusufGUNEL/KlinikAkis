using KlinikAkis.Infrastructure.Veri;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlinikAkis.Infrastructure.Seed;

/// <summary>Şemayı kurar ve demo verisini seed durumuna döndürür (SPEC §5.3). Veritabanı silinmez.</summary>
public sealed class DemoSifirlayici(KlinikDbContext db, TimeProvider zaman, ILogger<DemoSifirlayici> log)
{
    /// <summary>
    /// Başlangıçta: şema yoksa kurar, veritabanı boşsa seed'i yükler. Migration olmadığı için şema değişince
    /// Postgres'te <paramref name="semaYenidenKur"/> (Demo:SemaYenidenKur=true) ile bir kez sıfırdan kurulur.
    /// </summary>
    public async Task HazirlaAsync(bool semaYenidenKur = false, CancellationToken ct = default)
    {
        if (semaYenidenKur && db.Database.IsNpgsql())
        {
            // Her açılışta tüm veriyi siler: şema kurulduktan sonra bayrak kapatılmalı (Render ücretsizde sık yeniden başlar).
            log.LogWarning("Demo:SemaYenidenKur=true: şema silinip yeniden kuruluyor. Şema kurulunca bu ortam değişkenini kaldırın.");
            await db.Database.ExecuteSqlRawAsync("DROP SCHEMA public CASCADE; CREATE SCHEMA public;", ct);
        }
        await db.Database.EnsureCreatedAsync(ct);
        if (await db.Hastalar.AnyAsync(ct))
            return;

        DemoVeriTohumu.Yukle(db, zaman.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
    }

    public async Task<SeedSayimlari> SifirlaAsync(CancellationToken ct = default)
    {
        await using var islem = await db.Database.BeginTransactionAsync(ct);

        if (db.Database.IsNpgsql())
        {
            // Tek komut: FK sırası gerekmez. Tablo adları modelden gelir (kullanıcı girdisi değil).
            var komut = "TRUNCATE " + string.Join(", ", db.Model.GetEntityTypes().Select(t => $"\"{t.GetTableName()}\"")) + " CASCADE";
            await db.Database.ExecuteSqlRawAsync(komut, ct);
        }
        else
        {
            await SqliteTemizleAsync(ct);
        }

        db.ChangeTracker.Clear();
        var sayimlar = DemoVeriTohumu.Yukle(db, zaman.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);

        await islem.CommitAsync(ct);
        return sayimlar;
    }

    private async Task SqliteTemizleAsync(CancellationToken ct)
    {
        // FK sırasıyla: önce bağımlı tablolar.
        await db.ErisimKayitlari.ExecuteDeleteAsync(ct);
        await db.AiAuditLoglari.ExecuteDeleteAsync(ct);
        await db.OutboxMesajlari.ExecuteDeleteAsync(ct);
        await db.Epikrizler.ExecuteDeleteAsync(ct);
        await db.TaniOnerileri.ExecuteDeleteAsync(ct);
        await db.Muayeneler.ExecuteDeleteAsync(ct);
        await db.Kabuller.ExecuteDeleteAsync(ct);
        await db.Randevular.ExecuteDeleteAsync(ct);
        await db.OnAnamnezler.ExecuteDeleteAsync(ct);
        await db.TahlilSonuclari.ExecuteDeleteAsync(ct);
        await db.TahlilTanimlari.ExecuteDeleteAsync(ct);
        await db.Slotlar.ExecuteDeleteAsync(ct);
        await db.Hekimler.ExecuteDeleteAsync(ct);
        await db.Hastalar.ExecuteDeleteAsync(ct);
        await db.Branslar.ExecuteDeleteAsync(ct);
    }
}
