using KlinikAkis.Infrastructure.Outbox;
using KlinikAkis.Infrastructure.Veri;

namespace KlinikAkis.Api.Gercekzaman;

/// <summary>
/// e-Nabız gönderim döngüsü (SPEC §5.2): saniyede bir <see cref="OutboxGonderici.IsleAsync"/> çağırır; paket işlendiyse
/// OutboxGuncellendi olayını yayınlar. Gönderim mantığı Infrastructure'da (test edilebilir), döngü burada çünkü
/// IOlayYayinci Api katmanında. Olay demo grubuna gider: paket kaydında hekim yok; payload yine yalnızca {tur, id}.
/// Boşta veritabanına gidilmez (Neon'un uykuya geçmesi için): yalnızca bekleyen paket varken ya da son paketten sonraki
/// <see cref="UyanikKalma"/> içinde yoklanır.
/// </summary>
public sealed class OutboxIsleyici(IServiceScopeFactory kapsamlar, IOlayYayinci yayinci, TimeProvider zaman,
    ILogger<OutboxIsleyici> gunluk) : BackgroundService
{
    public static readonly TimeSpan Aralik = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan UyanikKalma = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var zamanlayici = new PeriodicTimer(Aralik, zaman);
        var bekleyenVar = true; // açılışta önceki çalışmadan kalmış paket olabilir
        while (await zamanlayici.WaitForNextTickAsync(ct))
        {
            var simdi = zaman.GetUtcNow().UtcDateTime;
            if (!bekleyenVar && simdi - OutboxGonderici.SonUyandirma > UyanikKalma) continue;
            try
            {
                await using var kapsam = kapsamlar.CreateAsyncScope();
                var gonderici = new OutboxGonderici(kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>());
                if (await gonderici.IsleAsync(simdi, ct) > 0)
                    await yayinci.YayinlaAsync(OlayTuru.OutboxGuncellendi, null, [Grup.Demo], ct);
                bekleyenVar = await gonderici.BekleyenVarAsync(ct);
            }
            catch (Exception hata) when (hata is not OperationCanceledException)
            {
                // Döngü düşmesin (ör. sıfırlama sırasında tablo boşalırken); bir sonraki tikte yeniden denenir.
                gunluk.LogWarning(hata, "Outbox işlenemedi");
            }
        }
    }
}
