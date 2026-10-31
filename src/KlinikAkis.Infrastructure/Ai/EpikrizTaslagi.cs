using System.Text;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using KlinikAkis.Domain;
using Microsoft.Extensions.AI;

namespace KlinikAkis.Infrastructure.Ai;

/// <summary>Epikrize giren bir tahlil satırı: değer ve işaret koddan (TahlilDegerlendirici), model yorumlamaz.</summary>
/// <param name="Loinc">ICD-10 yedeğinde testi ada göre değil koda göre tanımak için; modele giden satırda yer almaz.</param>
public sealed record EpikrizTahlili(string Ad, decimal Deger, string Birim, decimal Alt, decimal Ust, Isaret Isaret, int Ondalik, string? Loinc = null)
{
    private string Sayi(decimal d) => TurkceMetin.Sayi(d, Ondalik);

    /// <summary>"HbA1c: 7,4 % (referans 4,0–5,6) ▲ Y" — taslağa ve modele aynı biçimde girer.</summary>
    public string Satir() =>
        $"{Ad}: {Sayi(Deger)} {Birim} (referans {Sayi(Alt)}–{Sayi(Ust)}) {TahlilGirdisi.DoktorEtiketi(Isaret)}";

    public IEnumerable<decimal> Sayilar() => [Deger, Alt, Ust];
}

/// <summary>
/// Epikriz için kayıttan toplanan bilgiler (SPEC §13.7a). Ad, soyad, TC, telefon bu tipte yoktur; serbest metinler
/// (onaylı ön anamnez, hekim notu) <see cref="Maskele"/> ile maskelendikten sonra modele gider.
/// </summary>
/// <param name="OnAnamnez">Yalnızca hekimin onayladığı/düzenlediği ön anamnez; onaysız AI taslağı epikrize girmez.</param>
public sealed record EpikrizGirdisi(
    string TakmaAd, string YasAraligi, Cinsiyet Cinsiyet, string Brans,
    IReadOnlyList<string> Alerjiler, IReadOnlyList<string> KronikHastaliklar, IReadOnlyList<string> Ilaclar,
    string? OnAnamnez, string? HekimNotu, IReadOnlyList<EpikrizTahlili> Tahliller)
{
    public const string AlerjiYok = "Bilinen alerji yok";

    /// <summary>Alerji satırı kodda kurulur (CLAUDE.md: alan her zaman görünür, boşsa "Bilinen alerji yok").</summary>
    public string AlerjiSatiri => Alerjiler.Count == 0 ? AlerjiYok : string.Join(", ", Alerjiler);

    public EpikrizGirdisi Maskele(string ad, string soyad) => this with
    {
        OnAnamnez = OnAnamnez is null ? null : Maskeleyici.Maskele(OnAnamnez, ad, soyad),
        HekimNotu = HekimNotu is null ? null : Maskeleyici.Maskele(HekimNotu, ad, soyad),
    };

    /// <summary>Modele giden metin; denetim kaydına ve "modele giden veri" paneline aynen yazılır (önce <see cref="Maskele"/>).</summary>
    public string MaskeliMetin()
    {
        static string Liste(IReadOnlyList<string> l) => l.Count == 0 ? "yok" : string.Join(", ", l);
        var sb = new StringBuilder()
            .Append("hasta: ").Append(TakmaAd).Append(" · yaş: ").Append(YasAraligi)
            .Append(" · cinsiyet: ").Append(Cinsiyet == Cinsiyet.K ? "kadın" : "erkek").Append('\n')
            .Append("bölüm: ").Append(Brans).Append('\n')
            .Append("alerji: ").Append(AlerjiSatiri).Append('\n')
            .Append("kronik hastalıklar: ").Append(Liste(KronikHastaliklar)).Append('\n')
            .Append("kullandığı ilaçlar: ").Append(Liste(Ilaclar)).Append('\n')
            .Append("tahliller (işaret kodla hesaplandı):").Append('\n');
        if (Tahliller.Count == 0) sb.Append("- yok").Append('\n');
        foreach (var t in Tahliller) sb.Append("- ").Append(t.Satir()).Append('\n');
        sb.Append("<on_anamnez>").Append(OnAnamnez is null ? "hekim onaylı ön anamnez yok" : AiHatti.VeriOlarak(OnAnamnez))
            .Append("</on_anamnez>").Append('\n')
            .Append("<hekim_notu>").Append(HekimNotu is null ? "hekim notu yok" : AiHatti.VeriOlarak(HekimNotu))
            .Append("</hekim_notu>");
        return sb.ToString();
    }
}

/// <summary>LLM'den beklenen: yalnızca cümleler. Alerji ve tahlil satırları modele bırakılmaz.</summary>
public sealed record EpikrizYaniti(string Oyku, string Degerlendirme, string Plan)
{
    public const string SemaAdi = "epikriz";

    public static readonly ChatResponseFormatJson YanitBicimi = ChatResponseFormat.ForJsonSchema(
        AIJsonUtilities.TransformSchema(
            AIJsonUtilities.CreateJsonSchema(typeof(EpikrizYaniti), serializerOptions: OnAnamnezTaslagi.Json),
            new AIJsonSchemaTransformOptions { DisallowAdditionalProperties = true, RequireAllProperties = true }),
        SemaAdi, "Poliklinik vizitinin epikriz taslağı");

    public string JsonOlarak() => JsonSerializer.Serialize(this, OnAnamnezTaslagi.Json);
}

/// <summary>Saklanan ve hekime gösterilen taslak: modelin (ya da yedeğin) cümleleri + kodda kurulan satırlar.</summary>
public sealed record EpikrizTaslagi(string Oyku, string Degerlendirme, string Plan, string Alerji, IReadOnlyList<string> Tahliller)
{
    public static EpikrizTaslagi Olustur(EpikrizYaniti y, EpikrizGirdisi g) =>
        new(y.Oyku, y.Degerlendirme, y.Plan, g.AlerjiSatiri, g.Tahliller.Select(t => t.Satir()).ToList());

    public string JsonOlarak() => JsonSerializer.Serialize(this, OnAnamnezTaslagi.Json);

    public static EpikrizTaslagi Oku(string json) => JsonSerializer.Deserialize<EpikrizTaslagi>(json, OnAnamnezTaslagi.Json)!;
}

/// <summary>
/// Sunucu doğrulaması: geçersiz JSON, boş alan, alan başına 800 karakter sınırı ve girdide olmayan sayı (uydurma değer)
/// reddedilir, yedek şablona düşülür. Sayı kontrolü tahlil açıklamasındaki kuralı (1-3 arası adet serbest) kullanır;
/// ancak birime bitişik sayı da yakalanır ("1500mg", "9,1mmol"): epikriz plan cümlesinde doz uydurulabilir.
/// </summary>
public static partial class EpikrizDogrulayici
{
    public const int AlanSiniri = 800;
    public const string GecersizJson = "geçersiz JSON";
    public const string BosAlan = "boş alan";
    public const string Uzun = "800 karakterden uzun";
    public const string UydurmaSayi = "girdide olmayan sayı";

    public static (EpikrizYaniti? Yanit, string? Neden) Dogrula(string? cikti, string maskeliGirdi)
    {
        EpikrizYaniti? y;
        try
        {
            y = JsonSerializer.Deserialize<EpikrizYaniti>(KodBloguSoy(cikti), OnAnamnezTaslagi.Json);
        }
        catch (JsonException)
        {
            return (null, GecersizJson);
        }
        if (y is null) return (null, GecersizJson);
        string?[] alanlar = [y.Oyku, y.Degerlendirme, y.Plan];
        if (alanlar.Any(string.IsNullOrWhiteSpace)) return (null, BosAlan);
        if (alanlar.Any(a => a!.Trim().Length > AlanSiniri)) return (null, Uzun);
        if (!alanlar.All(a => SayilarGirdide(a!, maskeliGirdi)))
            return (null, UydurmaSayi);
        return (new EpikrizYaniti(y.Oyku.Trim(), y.Degerlendirme.Trim(), y.Plan.Trim()), null);
    }

    /// <summary>
    /// Metindeki her sayı modele giden metinde geçiyor mu (tahlil değerleri, aralıklar, nottaki sayılar); 1-3 arası
    /// tam sayı serbest. ICD-10 gerekçesi de aynı kuralla denetlenir.
    /// </summary>
    internal static bool SayilarGirdide(string metin, string maskeliGirdi)
    {
        var izinli = Sayilar(maskeliGirdi).ToHashSet();
        return Sayilar(metin).All(d => izinli.Contains(d) || (d is >= 1 and <= 3 && d == decimal.Truncate(d)));
    }

    /// <summary>Sayılar; harften sonra gelen rakamlar ("HbA1c", "B12") sayı değildir, birim önündekiler ("1500mg") sayıdır.</summary>
    private static IEnumerable<decimal> Sayilar(string metin) =>
        SayiDeseni().Matches(metin).Select(m => decimal.Parse(m.Value.Replace(',', '.'), CultureInfo.InvariantCulture));

    [GeneratedRegex(@"(?<![\p{L}\d,.])\d+(?:[,.]\d+)?(?!\d)")]
    private static partial Regex SayiDeseni();

    internal static string KodBloguSoy(string? metin)
    {
        var s = (metin ?? "").Trim();
        if (!s.StartsWith("```", StringComparison.Ordinal)) return s;
        var ilkSatir = s.IndexOf('\n');
        var son = s.LastIndexOf("```", StringComparison.Ordinal);
        return ilkSatir > 0 && son > ilkSatir ? s[(ilkSatir + 1)..son].Trim() : s;
    }
}

/// <summary>Model yanıt vermezse: kayıttaki onaylı metinlerden deterministik taslak (maskesiz, sunucuda kalır).</summary>
public static class EpikrizYedekSablonu
{
    public static EpikrizYaniti Olustur(EpikrizGirdisi g) => new(
        g.OnAnamnez ?? "Hekim onaylı ön anamnez yok; öykü hekim tarafından yazılacak.",
        g.HekimNotu ?? "Doktor notu girilmedi; değerlendirme hekim tarafından yazılacak.",
        "Plan hekim tarafından tamamlanacak.");
}
