using System.Globalization;

namespace KlinikAkis.Domain;

/// <summary>Sıra numarası: "A-017" (branş + gün içinde artan). Harf şimdilik sabit.</summary>
public static class SiraNo
{
    public static string Olustur(int gunIciSira) =>
        "A-" + gunIciSira.ToString("D3", CultureInfo.InvariantCulture);
}
