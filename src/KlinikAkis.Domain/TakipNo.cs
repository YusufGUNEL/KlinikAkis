using System.Globalization;

namespace KlinikAkis.Domain;

/// <summary>Kabul takip numarası: "SIM-000123" (simülasyon, ASCII; gerçek SGK/MEDULA numarası değildir).</summary>
public static class TakipNo
{
    public static string Olustur(int sira) => "SIM-" + sira.ToString("D6", CultureInfo.InvariantCulture);
}
