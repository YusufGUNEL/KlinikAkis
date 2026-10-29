using KlinikAkis.Infrastructure.Veri;
using Microsoft.EntityFrameworkCore;

namespace KlinikAkis.Infrastructure.Seed;

/// <summary>Şemayı kurar ve demo verisini seed durumuna döndürür (SPEC §5.3). Veritabanı silinmez.</summary>
public sealed class DemoSifirlayici(KlinikDbContext db, TimeProvider zaman)
{
    /// <summary>Başlangıçta: şema yoksa kurar, veritabanı boşsa seed'i yükler.</summary>
    public async Task HazirlaAsync(CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);
        if (await db.Hastalar.AnyAsync(ct))
            return;

        DemoVeriTohumu.Yukle(db, zaman.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
    }

    public async Task<SeedSayimlari> SifirlaAsync(CancellationToken ct = default)
    {
        await using var islem = await db.Database.BeginTransactionAsync(ct);

        // FK sırasıyla: önce bağımlı tablolar. (Postgres'te tek TRUNCATE ... CASCADE — Aşama 3.)
        await db.ErisimKayitlari.ExecuteDeleteAsync(ct);
        await db.AiAuditLoglari.ExecuteDeleteAsync(ct);
        await db.OutboxMesajlari.ExecuteDeleteAsync(ct);
        await db.Epikrizler.ExecuteDeleteAsync(ct);
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

        db.ChangeTracker.Clear();
        var sayimlar = DemoVeriTohumu.Yukle(db, zaman.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);

        await islem.CommitAsync(ct);
        return sayimlar;
    }
}
