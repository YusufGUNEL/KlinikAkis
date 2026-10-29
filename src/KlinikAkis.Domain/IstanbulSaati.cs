namespace KlinikAkis.Domain;

/// <summary>
/// Veri UTC saklanır; "bugün" ve gün içi saatler Europe/Istanbul'a göre hesaplanır (SPEC §5, §10.5).
/// </summary>
public static class IstanbulSaati
{
    public static readonly TimeZoneInfo Dilim = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    /// <summary>Kind=Local kabul edilmez (makinenin dilimi sessizce karışmasın); Unspecified UTC sayılır.</summary>
    public static DateTime YerelSaat(DateTime utc) =>
        utc.Kind == DateTimeKind.Local
            ? throw new ArgumentException("Zaman UTC olmalı; Kind=Local verildi.", nameof(utc))
            : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Dilim);

    public static DateOnly Bugun(DateTime utcSimdi) => DateOnly.FromDateTime(YerelSaat(utcSimdi));

    /// <summary>İstanbul'daki bir günün bir saatini UTC'ye çevirir.</summary>
    public static DateTime Utc(DateOnly gun, TimeOnly saat) =>
        TimeZoneInfo.ConvertTimeToUtc(gun.ToDateTime(saat, DateTimeKind.Unspecified), Dilim);

    /// <summary>İstanbul gününün [başlangıç, bitiş) UTC aralığı.</summary>
    public static (DateTime Baslangic, DateTime Bitis) GunAraligi(DateOnly gun) =>
        (Utc(gun, TimeOnly.MinValue), Utc(gun.AddDays(1), TimeOnly.MinValue));

    public static int Yas(DateOnly dogumTarihi, DateTime utcSimdi)
    {
        var bugun = Bugun(utcSimdi);
        var yas = bugun.Year - dogumTarihi.Year;
        if (bugun < dogumTarihi.AddYears(yas)) yas--;
        return yas;
    }
}
