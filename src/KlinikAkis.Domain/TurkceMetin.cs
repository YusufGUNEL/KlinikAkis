using System.Globalization;
using System.Text;

namespace KlinikAkis.Domain;

/// <summary>
/// Türkçe metin eşleşmesinin tek yeri (SPEC §10.5). Sunucu InvariantCulture ile çalışır; tr-TR yalnızca
/// burada, açıkça kullanılır ("İ" → "i", "I" → "ı").
/// </summary>
public static class TurkceMetin
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Ekranda gösterilen ad listeleri için alfabe sırası (Ç, Ğ, İ, Ö, Ş, Ü kendi yerinde).</summary>
    public static readonly StringComparer Siralama = StringComparer.Create(Tr, ignoreCase: false);

    /// <summary>tr-TR küçük harf, kesme işaretleri birleşir ("Ayşe'nin" → "ayşenin"), diğer noktalama boşluk olur, boşluk sadeleşir.</summary>
    public static string Normalize(string? metin)
    {
        if (string.IsNullOrWhiteSpace(metin)) return "";
        var kucuk = metin.Normalize(NormalizationForm.FormC).ToLower(Tr);
        var sb = new StringBuilder(kucuk.Length);
        var boslukBekliyor = false;
        foreach (var c in kucuk)
        {
            if (KesmeMi(c)) continue;
            if (char.IsLetterOrDigit(c))
            {
                if (boslukBekliyor && sb.Length > 0) sb.Append(' ');
                boslukBekliyor = false;
                sb.Append(c);
            }
            else
            {
                boslukBekliyor = true;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Normalize + Türkçe harfleri ASCII'ye katlar ("göğsüm" → "gogsum"). Türkçe klavyesiz yazan hasta da
    /// ("gogsum agriyor") aynı köke düşer; kök eşleşmesi ve ad maskeleme bu biçim üzerinden yapılır.
    /// </summary>
    public static string Katla(string? metin)
    {
        var n = Normalize(metin);
        var sb = new StringBuilder(n.Length);
        foreach (var c in n)
            sb.Append(c switch
            {
                'ç' => 'c',
                'ğ' => 'g',
                'ı' => 'i',
                'ö' => 'o',
                'ş' => 's',
                'ü' => 'u',
                'â' => 'a',
                'î' => 'i',
                'û' => 'u',
                _ => c,
            });
        return sb.ToString();
    }

    /// <summary>Katlanmış metinde bir sözcük bu kökle başlıyor mu ("gogs" ⊂ "gogsum"). Çok sözcüklü kök de olur ("kola yayil").</summary>
    public static bool KokVar(string katlanmis, string kok)
    {
        var i = 0;
        while ((i = katlanmis.IndexOf(kok, i, StringComparison.Ordinal)) >= 0)
        {
            if (i == 0 || katlanmis[i - 1] == ' ') return true;
            i++;
        }
        return false;
    }

    /// <summary>Katlanmış metinde tam sözcük ("kan" var, "kanepe" yok).</summary>
    public static bool SozcukVar(string katlanmis, string sozcuk) =>
        katlanmis.Split(' ').Contains(sozcuk, StringComparer.Ordinal);

    /// <summary>Hastaya giden cümlede ve istemde sayı: ondalık virgül, sabit basamak ("7,0", "128").</summary>
    public static string Sayi(decimal deger, int ondalik) => deger.ToString("F" + ondalik, Tr);

    public static bool KesmeMi(char c) => c is '\'' or '’' or '‘' or '`' or '´';
}
