namespace KlinikAkis.Domain;

public sealed record SohbetSorusu(
    int Adim, string Metin, IReadOnlyList<string> Secenekler, bool CokluSecim, bool Atlanabilir);

/// <summary>Bir sohbet adımının kaydı (OnAnamnez.SohbetJson). Cevap = seçilen etiketler + serbest metin.</summary>
public sealed record SohbetKaydi(int Adim, string Soru, IReadOnlyList<string> Secimler, string? Metin)
{
    public string Cevap => string.Join(", ", Secimler.Append(Metin).Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>
/// Şikayet sohbetinin deterministik soru ağacı (SPEC §9.4). Sorular kodda sabittir; LLM soru üretmez.
/// Her adımda serbest metin de yazılabilir.
/// </summary>
public static class SoruAgaci
{
    public const string Hicbiri = "Hiçbiri";

    public static readonly IReadOnlyList<SohbetSorusu> Sorular =
    [
        new(1, "Şikayetiniz nedir? Kendi cümlelerinizle yazabilir ya da birini seçebilirsiniz.",
            ["Baş ağrısı", "Boğaz ağrısı", "Ateş", "Halsizlik", "Karın ağrısı", "Öksürük"], false, false),
        new(2, "Şikayetiniz ne kadar süredir var?", ["1 günden az", "1-3 gün", "1 hafta", "Daha uzun"], false, false),
        new(3, "Ne kadar şiddetli?", ["Hafif", "Orta", "Şiddetli"], false, false),
        new(4, "Ateşiniz var mı?", ["Yok", "38 °C altı", "38 °C ve üstü", "Ölçmedim"], false, false),
        new(5, "Şu belirtilerden biri var mı? Size uyanların hepsini seçin.",
            ["Göğüs ağrısı", "Nefes darlığı", "Yüzümde kayma / konuşma bozukluğu", "Bayılma",
             "Kendime zarar verme düşüncesi", Hicbiri], true, false),
        new(6, "Eklemek istediğiniz başka bir şey var mı?", [], false, true),
    ];

    public static int ToplamAdim => Sorular.Count;

    public static SohbetSorusu Soru(int adim) => Sorular[adim - 1];

    public const int MetinSiniri = 1000;

    /// <summary>Cevabı soruya göre denetler; geçersizse Türkçe hata mesajı döner.</summary>
    public static string? Denetle(SohbetSorusu soru, IReadOnlyList<string> secimler, string? metin)
    {
        if (metin is { Length: > MetinSiniri })
            return $"Cevap en fazla {MetinSiniri} karakter olabilir.";
        if (secimler.Any(s => !soru.Secenekler.Contains(s, StringComparer.Ordinal)))
            return "Seçeneklerden biri bu soruya ait değil.";
        if (secimler.Distinct(StringComparer.Ordinal).Count() != secimler.Count)
            return "Aynı seçenek iki kez gönderildi.";
        if (!soru.CokluSecim && secimler.Count > 1)
            return "Bu soruda yalnızca bir seçenek seçilebilir.";
        if (secimler.Contains(Hicbiri, StringComparer.Ordinal) && secimler.Count > 1)
            return "\"Hiçbiri\" başka bir seçenekle birlikte seçilemez.";
        if (!soru.Atlanabilir && secimler.Count == 0 && string.IsNullOrWhiteSpace(metin))
            return "Bir seçenek seçin ya da kısaca yazın.";
        return null;
    }
}
