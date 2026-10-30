namespace KlinikAkis.Domain;

public enum RandevuEylemi { Kabul, Cagir, Basla, Kapat, Iptal, Gelmedi }

/// <summary>
/// Randevu durumu yalnızca buradan değişir (SPEC §5.1). Endpoint'ler geçişi buradan alır, arayüzdeki butonların
/// etkinliği de <see cref="IzinliEylemler"/>'den gelir: kural tek yerde.
/// <code>
/// Planlandi ──kabul──► KabulEdildi ──cagir──► Cagrildi ──basla──► Muayenede ──kapat──► Tamamlandi
///     └──iptal──► Iptal     └──gelmedi──► Gelmedi
/// </code>
/// </summary>
public static class RandevuDurumMakinesi
{
    private static readonly Dictionary<(RandevuDurumu, RandevuEylemi), RandevuDurumu> Gecisler = new()
    {
        [(RandevuDurumu.Planlandi, RandevuEylemi.Kabul)] = RandevuDurumu.KabulEdildi,
        [(RandevuDurumu.Planlandi, RandevuEylemi.Iptal)] = RandevuDurumu.Iptal,
        [(RandevuDurumu.KabulEdildi, RandevuEylemi.Cagir)] = RandevuDurumu.Cagrildi,
        [(RandevuDurumu.KabulEdildi, RandevuEylemi.Gelmedi)] = RandevuDurumu.Gelmedi,
        [(RandevuDurumu.Cagrildi, RandevuEylemi.Basla)] = RandevuDurumu.Muayenede,
        [(RandevuDurumu.Muayenede, RandevuEylemi.Kapat)] = RandevuDurumu.Tamamlandi,
    };

    public static RandevuDurumu Gecis(RandevuDurumu mevcut, RandevuEylemi eylem) =>
        Gecisler.TryGetValue((mevcut, eylem), out var yeni)
            ? yeni
            : throw new GecersizDurumGecisiException(mevcut, eylem);

    public static IReadOnlyList<RandevuEylemi> IzinliEylemler(RandevuDurumu mevcut) =>
        [.. Gecisler.Keys.Where(k => k.Item1 == mevcut).Select(k => k.Item2)];

    /// <summary>Eylemi yapabilecek rol: kabul masası mı, doktor mu (SPEC §5.1, §7).</summary>
    public static bool DoktorEylemiMi(RandevuEylemi eylem) =>
        eylem is RandevuEylemi.Cagir or RandevuEylemi.Basla or RandevuEylemi.Kapat;

    public static string GorunenAd(RandevuDurumu durum) => durum switch
    {
        RandevuDurumu.Planlandi => "Planlandı",
        RandevuDurumu.KabulEdildi => "Kabul edildi",
        RandevuDurumu.Cagrildi => "Çağrıldı",
        RandevuDurumu.Muayenede => "Muayenede",
        RandevuDurumu.Tamamlandi => "Tamamlandı",
        RandevuDurumu.Iptal => "İptal",
        RandevuDurumu.Gelmedi => "Gelmedi",
        _ => durum.ToString(),
    };

    /// <summary>"Randevu 'Planlandı' durumundayken {fiil}" cümlesinin fiili.</summary>
    public static string EylemFiili(RandevuEylemi eylem) => eylem switch
    {
        RandevuEylemi.Kabul => "kabul edilemez",
        RandevuEylemi.Cagir => "çağrılamaz",
        RandevuEylemi.Basla => "muayeneye başlanamaz",
        RandevuEylemi.Kapat => "vizit kapatılamaz",
        RandevuEylemi.Iptal => "iptal edilemez",
        RandevuEylemi.Gelmedi => "'Gelmedi' olarak işaretlenemez",
        _ => "değiştirilemez",
    };
}

public sealed class GecersizDurumGecisiException(RandevuDurumu mevcut, RandevuEylemi eylem)
    : InvalidOperationException(
        $"Randevu '{RandevuDurumMakinesi.GorunenAd(mevcut)}' durumundayken {RandevuDurumMakinesi.EylemFiili(eylem)}.")
{
    public RandevuDurumu Mevcut { get; } = mevcut;
    public RandevuEylemi Eylem { get; } = eylem;
}
