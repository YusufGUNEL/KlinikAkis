using System.Globalization;

namespace KlinikAkis.Domain;

/// <summary>Sıra numarası: "A-017" (branş + gün içinde artan). Harf şimdilik sabit.</summary>
public static class SiraNo
{
    public static string Olustur(int gunIciSira) =>
        "A-" + gunIciSira.ToString("D3", CultureInfo.InvariantCulture);

    /// <summary>"A-017" → 17; biçim dışıysa 0.</summary>
    public static int Sira(string siraNo) =>
        siraNo.Length > 2 && int.TryParse(siraNo.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
}
