using KlinikAkis.Domain;

namespace KlinikAkis.Tests;

/// <summary>"Bugün" UTC tarihine değil İstanbul'a göre (UTC+3); sınır 21:00 UTC.</summary>
public sealed class IstanbulSaatiTestleri
{
    [Fact]
    public void UTC_gece_yarisindan_once_Istanbulda_ertesi_gundur()
    {
        Assert.Equal(new DateOnly(2026, 10, 30), IstanbulSaati.Bugun(new DateTime(2026, 10, 29, 21, 30, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateOnly(2026, 10, 29), IstanbulSaati.Bugun(new DateTime(2026, 10, 29, 20, 59, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Gun_araligi_Istanbul_gece_yarisindan_baslar()
    {
        var (baslangic, bitis) = IstanbulSaati.GunAraligi(new DateOnly(2026, 10, 30));

        Assert.Equal(new DateTime(2026, 10, 29, 21, 0, 0, DateTimeKind.Utc), baslangic);
        Assert.Equal(new DateTime(2026, 10, 30, 21, 0, 0, DateTimeKind.Utc), bitis);
    }

    [Fact]
    public void Yas_dogum_gununde_Istanbul_gece_yarisinda_artar()
    {
        var dogum = new DateOnly(1972, 3, 14);

        // 13.03.2026 22:00 UTC = 14.03.2026 01:00 İstanbul → doğum günü.
        Assert.Equal(54, IstanbulSaati.Yas(dogum, new DateTime(2026, 3, 13, 22, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(53, IstanbulSaati.Yas(dogum, new DateTime(2026, 3, 13, 20, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Local_zaman_reddedilir() =>
        Assert.Throws<ArgumentException>(() => IstanbulSaati.Bugun(new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Local)));
}
