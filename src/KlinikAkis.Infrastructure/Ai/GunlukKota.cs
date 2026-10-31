using KlinikAkis.Domain;

namespace KlinikAkis.Infrastructure.Ai;

/// <summary>
/// Günlük toplam LLM çağrı kotası (SPEC §9.9, <c>AI:GunlukLimit</c>). Gün İstanbul'a göre döner. Sayaç bellekte:
/// tek örnek çalışıyoruz (§14); yeniden başlatma sayacı sıfırlar, bu demo için kabul edilir.
/// Sağlayıcı 429 döndürürse (ücretsiz katman doldu) o günün kalanı da dolu sayılır.
/// </summary>
public sealed class GunlukKota(TimeProvider zaman, int limit)
{
    private readonly Lock _kilit = new();
    private DateOnly _gun;
    private int _sayi;
    private bool _saglayiciDoldu;

    /// <summary>Kotadan bir çağrı ayırır; doluysa false.</summary>
    public bool Ayir()
    {
        lock (_kilit)
        {
            GunuYenile();
            if (_saglayiciDoldu || _sayi >= limit) return false;
            _sayi++;
            return true;
        }
    }

    public void SaglayiciDoldu()
    {
        lock (_kilit)
        {
            GunuYenile();
            _saglayiciDoldu = true;
        }
    }

    private void GunuYenile()
    {
        var bugun = IstanbulSaati.Bugun(zaman.GetUtcNow().UtcDateTime);
        if (bugun == _gun) return;
        _gun = bugun;
        _sayi = 0;
        _saglayiciDoldu = false;
    }
}
