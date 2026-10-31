using System.Globalization;

namespace KlinikAkis.Domain;

/// <summary>
/// Ekranda ad gösterimi: doktora "Ayşe K.", kabul masasına maskeli "A*** K***" (taslak 7).
/// Baş harf metin öğesine göre alınır: ayrışık yazılmış "Ö" (O + U+0308) ikiye bölünmez.
/// </summary>
public static class AdGosterimi
{
    public static string Kisa(string ad, string soyad) => $"{ad} {BasHarf(soyad)}.";

    /// <summary>TV sıra ekranı: "A.K." — tam ad TV'ye hiç gitmez.</summary>
    public static string BasHarfler(string ad, string soyad) => $"{BasHarf(ad)}.{BasHarf(soyad)}.";

    public static string Maskeli(string ad, string soyad) => $"{BasHarf(ad)}*** {BasHarf(soyad)}***";

    public static string BasHarf(string metin) =>
        string.IsNullOrEmpty(metin) ? "?" : metin[..StringInfo.GetNextTextElementLength(metin)];
}
