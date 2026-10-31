namespace KlinikAkis.Domain;

public sealed record Icd10Kodu(string Kod, string Ad);

/// <summary>
/// ICD-10 önerisinin kapalı listesi (SPEC §9.10). DSÖ ICD-10 2019 sürümü (Türkiye'de kullanılan; ICD-10-CM değil);
/// her kod DSÖ tablosunda uç (alt kırılımı olmayan) koddur. Model yalnızca bu listeden seçer, sunucu da bu listeyle
/// doğrular. Senaryo hastalarının belirtileri (R grubu) ve sık poliklinik tanıları seçildi.
/// </summary>
public static class Icd10Listesi
{
    public static readonly IReadOnlyList<Icd10Kodu> Tum =
    [
        new("D50.9", "Demir eksikliği anemisi, tanımlanmamış"),
        new("D64.9", "Anemi, tanımlanmamış"),
        new("E03.9", "Hipotiroidi, tanımlanmamış"),
        new("E11.9", "Tip 2 diabetes mellitus, komplikasyonsuz"),
        new("E66.9", "Obezite, tanımlanmamış"),
        new("E78.5", "Hiperlipidemi, tanımlanmamış"),
        new("H66.9", "Otitis media, tanımlanmamış"),
        new("H92.0", "Kulak ağrısı (otalji)"),
        new("I10", "Esansiyel (primer) hipertansiyon"),
        new("J02.9", "Akut farenjit, tanımlanmamış"),
        new("J03.9", "Akut tonsillit, tanımlanmamış"),
        new("J06.9", "Akut üst solunum yolu enfeksiyonu, tanımlanmamış"),
        new("K21.9", "Gastroözofageal reflü hastalığı, özofajitsiz"),
        new("M54.5", "Bel ağrısı"),
        new("N39.0", "İdrar yolu enfeksiyonu, yeri belirtilmemiş"),
        new("R05", "Öksürük"),
        new("R07.0", "Boğaz ağrısı"),
        new("R07.4", "Göğüs ağrısı, tanımlanmamış"),
        new("R10.4", "Diğer ve tanımlanmamış karın ağrısı"),
        new("R35", "Poliüri"),
        new("R50.9", "Ateş, tanımlanmamış"),
        new("R51", "Baş ağrısı"),
        new("R53", "Halsizlik ve yorgunluk"),
        new("R63.1", "Polidipsi"),
        new("R63.5", "Anormal kilo artışı"),
        new("R73.9", "Hiperglisemi, tanımlanmamış"),
    ];

    private static readonly Dictionary<string, Icd10Kodu> KodaGore = Tum.ToDictionary(k => k.Kod, StringComparer.Ordinal);

    /// <summary>Kod karşılaştırması Ordinal: "e11.9" ya da boşluklu biçim listede sayılmaz.</summary>
    public static bool Var(string? kod) => kod is not null && KodaGore.ContainsKey(kod);

    public static Icd10Kodu? Bul(string kod) => KodaGore.GetValueOrDefault(kod);
}
