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
        "min", "mun", "miz", "muz", "yken", "gil", "gile", "gilde", "cige", "cigi", "cigin",
        // Ek-fiil ve -ki: "Mehmettir", "Mehmetti", "Ayşeydim", "Ayşeninki", "Kayadaki", "Tekinlerdeyiz".
        "di", "du", "ti", "tu", "tir", "tur", "dim", "dum", "tim", "tum", "ydim", "ydum", "si", "su",
        "ki", "ninki", "nunki", "deki", "daki", "teki", "taki", "lerde", "larda", "lerdeyiz", "lardayiz",
    };

    // Ünsüz yumuşaması (katlanmış): "Mehmet" → "Mehmed'in", "Tarık" → "Tarığa" (ç→c katlanınca zaten aynı).
    private static readonly Dictionary<char, char> Yumusama = new() { ['t'] = 'd', ['k'] = 'g', ['p'] = 'b' };

    private const int EkSiniri = 6;

    [GeneratedRegex(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}-]+(?:\.[\p{L}\p{N}-]+)+", RegexOptions.CultureInvariant)]
    private static partial Regex EpostaDeseni();

    // 11 hane; araya boşluk, nokta, tire girebilir ("100 000 001 46"). Sağlama ayırıcılar atılarak yapılır.
    [GeneratedRegex(@"(?<!\d)[1-9](?:[\s.-]?\d){10}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex TcAdayi();

    // +90 532 123 45 67 · 0532 123 45 67 · 0 532-123-45-67 · 0532.123.45.67 · (532) 123 45 67 · 5321234567
    // Sabit hat yalnızca başında 0 / +90 ile: 0212 555 44 33.
    [GeneratedRegex(@"(?<![\d+])(?:(?:\+90[\s.-]?|90[\s.-]?|0[\s.-]?)?\(?5\d{2}\)?|(?:\+90[\s.-]?|0[\s.-]?)\(?[2-4]\d{2}\)?)[\s.-]?\d{3}[\s.-]?\d{2}[\s.-]?\d{2}(?!\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex TelefonDeseni();

    // Görünmez biçim karakterleri (sıfır genişlikli boşluk, yumuşak tire, ZWJ) adı bölmesin.
    [GeneratedRegex(@"\p{Cf}", RegexOptions.CultureInvariant)]
    private static partial Regex BicimKarakteri();

    // Harf dizisi; içinde tek bir kesme işareti olabilir ("Ayşe'nin", "Kaya’ya"). Kesme kümesi TurkceMetin.KesmeMi ile aynı.
    // Birleşik işaretler (\p{M}) sözcüğü bölmesin; karşılaştırmadan önce atılır.
    [GeneratedRegex(@"[\p{L}\p{M}]+(?:['’‘`´][\p{L}\p{M}]+)?", RegexOptions.CultureInvariant)]
    private static partial Regex Sozcuk();

    [GeneratedRegex(@"\p{Mn}", RegexOptions.CultureInvariant)]
    private static partial Regex BirlesikIsaret();

    public static string Maskele(string metin, string? ad = null, string? soyad = null)
    {
        if (string.IsNullOrEmpty(metin)) return metin;
        // Önce biçim karakterleri silinir, sonra NFC: "s + ZWJ + birleşik çengel" de "ş" olsun.
        metin = BicimKarakteri().Replace(metin, "").Normalize(NormalizationForm.FormC);
        // Tam genişlikli ("０５３２") ve başka yazı sistemlerinin rakamları ASCII'ye: TC/telefon desenleri kaçmasın.
        metin = AsciiRakam(metin);

        var sonuc = EpostaDeseni().Replace(metin, Eposta);
        sonuc = TcAdayi().Replace(sonuc, m => TcKimlik.GecerliMi(Rakamlar(m.Value)) ? Tc : m.Value);
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

    private static string Rakamlar(string s) => string.Concat(s.Where(char.IsAsciiDigit));

    private static string AsciiRakam(string s)
    {
        if (!s.Any(c => char.IsDigit(c) && !char.IsAsciiDigit(c))) return s;
        return string.Create(s.Length, s, static (hedef, kaynak) =>
        {
            for (var i = 0; i < kaynak.Length; i++)
            {
                var c = kaynak[i];
                hedef[i] = char.IsDigit(c) && !char.IsAsciiDigit(c) ? (char)('0' + (int)char.GetNumericValue(c)) : c;
            }
        });
    }

    private static List<string> Parcalar(string? isim) =>
        string.IsNullOrWhiteSpace(isim)
            ? []
            : isim.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries).Select(TurkceMetin.Katla).Where(p => p.Length > 1)
                .SelectMany(Govdeler).Distinct().ToList();

    private static IEnumerable<string> Govdeler(string isim)
    {
        yield return isim;
        if (Yumusama.TryGetValue(isim[^1], out var yumusak)) yield return isim[..^1] + yumusak;
    }

    private static string Sade(string s) => TurkceMetin.Katla(BirlesikIsaret().Replace(s, ""));

    /// <summary>Büyük/küçük harf ve Türkçe karakterden bağımsız (katlanmış) karşılaştırma; ek kesmeli ya da listeden.</summary>
    private static bool Eslesir(string sozcuk, List<string> isimler)
    {
        if (isimler.Count == 0) return false;

        var kesme = sozcuk.AsSpan().IndexOfAny(['\'', '’', '‘', '`', '´']);
        if (kesme >= 0)
        {
            // Kesme özel adın ekini ayırır: ekin ne olduğu önemli değil, uzunluğu sınırlı.
            var govde = Sade(sozcuk[..kesme]);
            var ek = sozcuk.Length - kesme - 1;
            return ek <= EkSiniri && isimler.Contains(govde, StringComparer.Ordinal);
        }

        var katli = Sade(sozcuk);
        foreach (var isim in isimler)
        {
            if (!katli.StartsWith(isim, StringComparison.Ordinal)) continue;
            var kalan = katli[isim.Length..];
            if (kalan.Length == 0 || Ekler.Contains(kalan)) return true;
        }
        return false;
    }
}
