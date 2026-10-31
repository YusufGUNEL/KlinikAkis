using System.Text.Json;
using KlinikAkis.Domain;
using KlinikAkis.Domain.Varliklar;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.EntityFrameworkCore;

namespace KlinikAkis.Infrastructure.Outbox;

/// <summary>
/// e-Nabız gönderim kutusu (SPEC §5.2, simülasyon). Paketler iş adımıyla aynı kayıtta yazılır (outbox kalıbı);
/// gönderim ayrı: <see cref="IsleAsync"/> 2 sn'yi geçmiş bekleyen paketleri "gönderir". Dışarıya hiçbir şey çıkmaz.
/// Demo kuralı: her sıfırlamadan sonraki ilk 105 paketi iş kuralı hatası verir. Kural durumsuzdur: veritabanında daha
/// önce işlenmiş (Deneme &gt; 0) 105 yoksa bu ilk 105'tir. Sıfırlama tabloyu boşalttığı için her seferinde aynı sonuç.
/// </summary>
public sealed class OutboxGonderici(KlinikDbContext db)
{
    public static readonly TimeSpan Gecikme = TimeSpan.FromSeconds(2);
    public const string UcumHatasi = "Sonuç birimi UCUM biçiminde değil — simülasyon";

    private static long sonUyandirma;

    /// <summary>
    /// İşleyici boşta veritabanını yoklamaz (Neon uykuya geçebilsin); yeni ya da yeniden denenen paket onu uyandırır.
    /// Zaman damgası, kayıt henüz yazılmamışken yapılan ilk yoklamada paketin kaçmaması için (işleyici bir süre uyanık kalır).
    /// </summary>
    public static DateTime SonUyandirma => new(Interlocked.Read(ref sonUyandirma), DateTimeKind.Utc);

    public static void Uyandir(DateTime simdi) => Interlocked.Exchange(ref sonUyandirma, simdi.Ticks);

    /// <summary>Yeni paket: kimlik yerine takma ad; payload yalnızca klinik/idari alanlar.</summary>
    public static OutboxMesaji Paket(PaketTipi tip, string takmaAd, object payload, DateTime simdi)
    {
        Uyandir(simdi);
        return new()
        {
            Id = Guid.NewGuid(),
            PaketTipi = tip,
            HastaTakmaAdi = takmaAd,
            PayloadJson = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web),
            Zaman = simdi,
        };
    }

    public Task<bool> BekleyenVarAsync(CancellationToken ct = default) =>
        db.OutboxMesajlari.AnyAsync(o => o.Durum == OutboxDurumu.Bekliyor, ct);

    /// <summary>Zamanı gelmiş bekleyen paketleri gönderir; işlenen paket sayısını döner (0 ise olay yayınlanmaz).</summary>
    public async Task<int> IsleAsync(DateTime simdi, CancellationToken ct = default)
    {
        var sinir = simdi - Gecikme;
        var bekleyenler = await db.OutboxMesajlari
            .Where(o => o.Durum == OutboxDurumu.Bekliyor && o.Zaman <= sinir)
            .OrderBy(o => o.Zaman)
            .ToListAsync(ct);
        if (bekleyenler.Count == 0) return 0;

        var onceki105 = await db.OutboxMesajlari.AnyAsync(o => o.PaketTipi == PaketTipi.LabSonuc && o.Deneme > 0, ct);
        foreach (var paket in bekleyenler)
        {
            paket.Deneme++;
            if (paket.PaketTipi == PaketTipi.LabSonuc && !onceki105)
            {
                paket.Durum = OutboxDurumu.IsKuraliHatasi;
                paket.Hata = UcumHatasi;
                onceki105 = true;
            }
            else
            {
                paket.Durum = OutboxDurumu.Gonderildi;
                paket.Hata = null;
            }
        }
        await db.SaveChangesAsync(ct);
        return bekleyenler.Count;
    }
}
