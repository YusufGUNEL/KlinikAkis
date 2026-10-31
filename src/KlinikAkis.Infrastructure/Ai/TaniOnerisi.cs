using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KlinikAkis.Domain;
using Microsoft.Extensions.AI;

namespace KlinikAkis.Infrastructure.Ai;

/// <param name="Kod">Kapalı listeden (Icd10Listesi); şemada enum, sunucuda yine doğrulanır.</param>
public sealed record TaniMaddesi(string Kod, string Gerekce);

/// <summary>
/// LLM'den beklenen ICD-10 önerisi (SPEC §9.10): 0-3 kod + kısa gerekçe (dayanak yoksa boş). Kodlar C# enum adı olamadığı için ("E11.9")
/// tipten üretilen şemaya <c>enum</c> dizisi elle eklenir; model listenin dışına çıkamaz, çıkarsa sunucu reddeder.
/// </summary>
public sealed record TaniOnerisiYaniti(IReadOnlyList<TaniMaddesi> Oneriler)
{
    public const string SemaAdi = "tani_onerisi";
    public const int EnFazlaOneri = 3;

    public static readonly JsonElement Sema = SemaKur();

    public static readonly ChatResponseFormatJson YanitBicimi =
        ChatResponseFormat.ForJsonSchema(Sema, SemaAdi, "Kapalı listeden ICD-10 kodlama önerisi");

    public string JsonOlarak() => JsonSerializer.Serialize(this, OnAnamnezTaslagi.Json);

    public static TaniOnerisiYaniti Oku(string json) =>
        JsonSerializer.Deserialize<TaniOnerisiYaniti>(json, OnAnamnezTaslagi.Json)!;

    private static JsonElement SemaKur()
    {
        var sema = AIJsonUtilities.TransformSchema(
            AIJsonUtilities.CreateJsonSchema(typeof(TaniOnerisiYaniti), serializerOptions: OnAnamnezTaslagi.Json),
            new AIJsonSchemaTransformOptions { DisallowAdditionalProperties = true, RequireAllProperties = true });
        var kok = JsonNode.Parse(sema.GetRawText())!.AsObject();
        var oneriler = kok["properties"]!["oneriler"]!.AsObject();
        // Dayanak yoksa boş liste geçerlidir: model kod seçmeye zorlanmaz (yedekle aynı davranış).
        oneriler["minItems"] = 0;
        oneriler["maxItems"] = EnFazlaOneri;
        var kod = oneriler["items"]!["properties"]!["kod"]!.AsObject();
        kod["enum"] = new JsonArray(Icd10Listesi.Tum.Select(k => (JsonNode)JsonValue.Create(k.Kod)!).ToArray());
        return JsonSerializer.SerializeToElement(kok);
    }
}

/// <summary>
/// Sunucu doğrulaması: geçersiz JSON, 3'ten fazla madde, listede olmayan ya da tekrarlı kod, boş / 300 karakterden uzun
/// gerekçe ve girdide olmayan sayı reddedilir, yedeğe düşülür. Sayı kuralı epikrizle aynıdır.
/// </summary>
public static partial class TaniDogrulayici
{
    public const int GerekceSiniri = 300;
    public const string MaddeSayisi = "3'ten fazla madde";
    public const string ListedeYok = "listede olmayan kod";
    public const string Tekrarli = "tekrarlı kod";
    public const string Uzun = "gerekçe 300 karakterden uzun";
    public const string OndalikNokta = "ondalık nokta";

    public static (TaniOnerisiYaniti? Yanit, string? Neden) Dogrula(string? cikti, string maskeliGirdi)
    {
        TaniOnerisiYaniti? y;
        try
        {
            y = JsonSerializer.Deserialize<TaniOnerisiYaniti>(EpikrizDogrulayici.KodBloguSoy(cikti), OnAnamnezTaslagi.Json);
        }
        catch (JsonException)
        {
            return (null, EpikrizDogrulayici.GecersizJson);
        }
        if (y?.Oneriler is null || y.Oneriler.Any(m => m is null)) return (null, EpikrizDogrulayici.GecersizJson);
        if (y.Oneriler.Count > TaniOnerisiYaniti.EnFazlaOneri) return (null, MaddeSayisi);
        if (y.Oneriler.Any(m => !Icd10Listesi.Var(m.Kod))) return (null, ListedeYok);
        if (y.Oneriler.Select(m => m.Kod).Distinct(StringComparer.Ordinal).Count() != y.Oneriler.Count) return (null, Tekrarli);
        if (y.Oneriler.Any(m => string.IsNullOrWhiteSpace(m.Gerekce))) return (null, EpikrizDogrulayici.BosAlan);
        if (y.Oneriler.Any(m => m.Gerekce.Trim().Length > GerekceSiniri)) return (null, Uzun);
        // Hekim ekranında ondalık virgül (SPEC §10.5); "7.4" girdideki 7,4 ile aynı sayılırdı. "E11.9" harften sonra geldiği için sayılmaz.
        if (y.Oneriler.Any(m => NoktaliOndalik().IsMatch(m.Gerekce))) return (null, OndalikNokta);
        if (!y.Oneriler.All(m => EpikrizDogrulayici.SayilarGirdide(m.Gerekce, maskeliGirdi)))
            return (null, EpikrizDogrulayici.UydurmaSayi);
        return (new TaniOnerisiYaniti(y.Oneriler.Select(m => new TaniMaddesi(m.Kod, m.Gerekce.Trim())).ToList()), null);
    }

    [GeneratedRegex(@"(?<![\p{L}\d.,])\d+\.\d+")]
    private static partial Regex NoktaliOndalik();
}

/// <summary>
/// Model yanıt vermezse kodla öneri: kayıttaki kronik hastalık, kodla hesaplanmış tahlil işareti ve hekimin onayladığı
/// ön anamnezdeki belirtiler. Ağırlıkla belirti (R grubu) kodları: yeni tanı iddiası taşımaz. Hiçbiri yoksa boş liste;
/// hekim Düzenle ile listeden seçer. Eşleşme dar tutulur: yanlış kod önermek, kod önermemekten kötüdür.
/// </summary>
public static class TaniYedekSablonu
{
    /// <summary>Kronik hastalık → kod. Dışlananlar ("tip 1", "insipidus", "pulmoner"...) aynı satırda geçerse eşleşmez.</summary>
    private static readonly (string[] Ifadeler, string[] Dislananlar, string Kod)[] KronikEslemesi =
    [
        (["tip 2 diyabet", "tip ii diyabet", "tip 2 dm", "dm tip 2", "tip 2 diabetes"], [], "E11.9"),
        (["hipotiroidi"], [], "E03.9"),
        (["hipertansiyon"], ["pulmoner", "portal", "intrakraniyal", "gebelik"], "I10"),
        (["hiperlipidemi"], [], "E78.5"),
        (["obezite"], [], "E66.9"),
        (["reflu"], ["vezikoureteral", "vezikouretral"], "K21.9"),
    ];

    /// <summary>
    /// Onaylı ön anamnezdeki belirti kökleri. Ateş eşlenmez: kural tabanlı özet her zaman "Ateş: …" satırı yazar
    /// ("Ateş: Yok" da), yapılandırılmış cevap bu metinde güvenle ayrılamaz.
    /// </summary>
    private static readonly (string[] Kokler, string Kod, string Belirti)[] AnamnezEslemesi =
    [
        (["cok su ic", "su iciyor", "susa"], "R63.1", "çok su içme"),
        (["sik idrar", "sik sik idrar"], "R35", "sık idrara çıkma"),
        (["halsiz", "yorgun"], "R53", "halsizlik"),
        (["bogaz agri", "bogazim agri", "bogazi agri"], "R07.0", "boğaz ağrısı"),
        (["kulak agri", "kulagim agri", "kulagi agri"], "H92.0", "kulak ağrısı"),
        (["kilo al"], "R63.5", "kilo alma"),
        (["oksur"], "R05", "öksürük"),
        (["bas agri", "basim agri"], "R51", "baş ağrısı"),
    ];

    /// <summary>Aynı cümlecikte geçerse eşleşme atılır ("ateş yok", "kilo almadım", "su içmiyorum").</summary>
    private static readonly string[] OlumsuzSozcukler = ["yok", "degil", "olmadi", "olmuyor", "hayir"];
    private static readonly string[] OlumsuzEkler = ["miyor", "muyor", "madi", "medi", "madim", "medim", "mam", "mem", "maz", "mez"];

    private const string HbA1cLoinc = "4548-4";
    private const string GlukozLoinc = "1558-6";
    private const string HemoglobinLoinc = "718-7";

    public static TaniOnerisiYaniti Olustur(EpikrizGirdisi g)
    {
        var maddeler = new List<TaniMaddesi>();
        void Ekle(string kod, string gerekce)
        {
            if (maddeler.Count < TaniOnerisiYaniti.EnFazlaOneri && maddeler.All(m => m.Kod != kod))
                maddeler.Add(new TaniMaddesi(kod, gerekce));
        }

        foreach (var hastalik in g.KronikHastaliklar)
        {
            var katli = TurkceMetin.Katla(hastalik);
            foreach (var (ifadeler, dislananlar, kod) in KronikEslemesi)
                if (ifadeler.Any(k => TurkceMetin.KokVar(katli, k)) && !dislananlar.Any(d => TurkceMetin.KokVar(katli, d)))
                    Ekle(kod, $"Kayıttaki kronik hastalık: {hastalik}.");
        }

        foreach (var t in g.Tahliller)
        {
            var yuksek = t.Isaret is Isaret.Yuksek or Isaret.KritikYuksek;
            var dusuk = t.Isaret is Isaret.Dusuk or Isaret.KritikDusuk;
            if (yuksek && t.Loinc is HbA1cLoinc or GlukozLoinc)
                Ekle("R73.9", $"{t.Satir()} (işaret kodla hesaplandı).");
            else if (dusuk && t.Loinc == HemoglobinLoinc)
                Ekle("D64.9", $"{t.Satir()} (işaret kodla hesaplandı).");
        }

        // Önce böl, sonra katla: Katla noktalamayı boşluğa çevirir, tersi tüm metni tek cümlecik yapar.
        var cumlecikler = (g.OnAnamnez ?? "")
            .Split(['.', ',', ';', ':', '!', '?', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(TurkceMetin.Katla)
            .Where(c => c.Length > 0)
            .ToArray();
        foreach (var (kokler, kod, belirti) in AnamnezEslemesi)
            if (cumlecikler.Any(c => kokler.Any(k => OlumluGeciyor(c, k))))
                Ekle(kod, $"Onaylı ön anamnezde {belirti} geçiyor.");

        return new TaniOnerisiYaniti(maddeler);
    }

    /// <summary>Kök cümlecikte geçiyor ve cümlecik olumsuz değil (olumsuz sözcük ya da olumsuz fiil eki yok).</summary>
    private static bool OlumluGeciyor(string cumlecik, string kok)
    {
        if (!TurkceMetin.KokVar(cumlecik, kok)) return false;
        var sozcukler = cumlecik.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return !sozcukler.Any(s => OlumsuzSozcukler.Contains(s, StringComparer.Ordinal)
            || OlumsuzEkler.Any(e => s.Contains(e, StringComparison.Ordinal)));
    }
}
