using System.Text;
using System.Text.RegularExpressions;
using KlinikAkis.Domain;

namespace KlinikAkis.Infrastructure.Ai;

/// <summary>
/// Modele gitmeden önce kimlik verisini maskeler (SPEC §9.2). Sıra: e-posta → TC → telefon → kayıttaki ad/soyad.
/// Kayıtta olmayan isimler ve adresler maskelenmez (SPEC §14 Sınırlar).
/// </summary>
public static partial class Maskeleyici
{
    public const string Tc = "[TC]";
    public const string Telefon = "[TELEFON]";
    public const string Eposta = "[EPOSTA]";
    public const string Ad = "[AD]";
    public const string Soyad = "[SOYAD]";

    // Kesmesiz yazılan ekler kapalı listedir (katlanmış biçimde: "nın/nin/nun/nün" → "nin/nun").
    // Serbest "en fazla 6 harf" kabul edilseydi "Ayşegül" de "Ayşe" + "gül" sayılırdı.
    private static readonly HashSet<string> Ekler = new(StringComparer.Ordinal)
    {
        "nin", "nun", "in", "un", "ye", "ya", "yi", "yu", "e", "a", "i", "u", "ne", "na", "nu",
        "de", "da", "te", "ta", "den", "dan", "ten", "tan", "nde", "nda", "nden", "ndan",
        "le", "la", "yle", "yla", "ler", "lar", "dir", "dur", "ydi", "ydu",
        // Kendini tanıtma ve iyelik: "Ben Ayşeyim", "Kayayım", "Ayşem", "Ayşeciğim", "Kayalardan".
        "yim", "yum", "im", "um", "m", "yiz", "yuz", "iz", "uz", "ymis", "ymus", "mis", "mus",
        "cik", "cigim", "cim", "lerin", "larin", "leri", "lari", "lere", "lara", "lerden", "lardan",
    };

    private const int EkSiniri = 6;

    [GeneratedRegex(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}-]+(?:\.[\p{L}\p{N}-]+)+", RegexOptions.CultureInvariant)]
    private static partial Regex EpostaDeseni();

    [GeneratedRegex(@"(?<!\d)[1-9]\d{10}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex TcAdayi();

    // +90 532 123 45 67 · 0532 123 45 67 · 0 532-123-45-67 · (532) 123 45 67 · 5321234567
    [GeneratedRegex(@"(?<![\d+])(?:\+90[\s-]?|90[\s-]?|0[\s-]?)?\(?5\d{2}\)?[\s-]?\d{3}[\s-]?\d{2}[\s-]?\d{2}(?!\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex TelefonDeseni();

    // Harf dizisi; içinde tek bir kesme işareti olabilir ("Ayşe'nin", "Kaya’ya"). Kesme kümesi TurkceMetin.KesmeMi ile aynı.
    [GeneratedRegex(@"\p{L}+(?:['’‘`´]\p{L}+)?", RegexOptions.CultureInvariant)]
    private static partial Regex Sozcuk();

    public static string Maskele(string metin, string? ad = null, string? soyad = null)
    {
        if (string.IsNullOrEmpty(metin)) return metin;
        // Ayrıştırılmış (NFD) yapıştırılan "Ayşe" (s + birleşik çengel) sözcük ortasından bölünmesin.
        metin = metin.Normalize(NormalizationForm.FormC);

        var sonuc = EpostaDeseni().Replace(metin, Eposta);
        sonuc = TcAdayi().Replace(sonuc, m => TcKimlik.GecerliMi(m.Value) ? Tc : m.Value);
        sonuc = TelefonDeseni().Replace(sonuc, Telefon);

        var adlar = Parcalar(ad);
        var soyadlar = Parcalar(soyad);
        if (adlar.Count == 0 && soyadlar.Count == 0) return sonuc;

        return Sozcuk().Replace(sonuc, m =>
        {
            if (Eslesir(m.Value, adlar)) return Ad;
            if (Eslesir(m.Value, soyadlar)) return Soyad;
            return m.Value;
        });
    }

    private static List<string> Parcalar(string? isim) =>
        string.IsNullOrWhiteSpace(isim)
            ? []
            : isim.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries).Select(TurkceMetin.Katla).Where(p => p.Length > 1).ToList();

    /// <summary>Büyük/küçük harf ve Türkçe karakterden bağımsız (katlanmış) karşılaştırma; ek kesmeli ya da listeden.</summary>
    private static bool Eslesir(string sozcuk, List<string> isimler)
    {
        if (isimler.Count == 0) return false;

        var kesme = sozcuk.AsSpan().IndexOfAny(['\'', '’', '‘', '`', '´']);
        if (kesme >= 0)
        {
            // Kesme özel adın ekini ayırır: ekin ne olduğu önemli değil, uzunluğu sınırlı.
            var govde = TurkceMetin.Katla(sozcuk[..kesme]);
            var ek = sozcuk.Length - kesme - 1;
            return ek <= EkSiniri && isimler.Contains(govde, StringComparer.Ordinal);
        }

        var katli = TurkceMetin.Katla(sozcuk);
        foreach (var isim in isimler)
        {
            if (!katli.StartsWith(isim, StringComparison.Ordinal)) continue;
            var kalan = katli[isim.Length..];
            if (kalan.Length == 0 || Ekler.Contains(kalan)) return true;
        }
        return false;
    }
}
