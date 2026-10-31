using KlinikAkis.Domain;

namespace KlinikAkis.Infrastructure.Ai;

/// <summary>
/// Model yanıt vermediğinde ya da yanıtı doğrulamadan geçmediğinde kullanılan taslak (SPEC §9.5): anahtar kelime →
/// bölüm eşlemesi + yapılandırılmış cevaplardan şablon özet. LLM yok; kartta "Otomatik taslak · kural tabanlı" yazar.
/// </summary>
public static class KuralTabanliYedek
{
    // Sıra önemli: ilk eşleşen kazanır. Kökler katlanmış metinde aranır ("susadım" → "susa").
    private static readonly (string[] Kokler, BransKodu Brans)[] Eslemeler =
    [
        (["susa", "su ic", "idrar", "seker", "diyabet", "tiroid", "guatr", "kilo al", "usu"], BransKodu.Endokrinoloji),
        (["bogaz", "kulak", "kulag", "burun", "bademcik", "sinuzit", "yutkun"], BransKodu.Kbb),
        (["carpinti", "tansiyon", "kalp"], BransKodu.Kardiyoloji),
    ];

    public const int CocukYasSiniri = 18;

    public static OnAnamnezTaslagi Olustur(IReadOnlyList<SohbetKaydi> sohbet, int yas, IReadOnlySet<BransKodu> hekimliBranslar)
    {
        string Cevap(int adim) => sohbet.FirstOrDefault(k => k.Adim == adim)?.Cevap ?? "";
        var sikayet = Cevap(1);
        var sure = Cevap(2);
        var siddetCevabi = Cevap(3);
        var ates = Cevap(4);
        var (siddet, aciliyet) = SiddetVeAciliyet(sohbet);

        var katli = TurkceMetin.Katla(string.Join(" . ", sohbet.Select(k => k.Cevap)));
        var eslesen = Eslemeler.FirstOrDefault(e => e.Kokler.Any(k => TurkceMetin.KokVar(katli, k)));
        BransKodu? anahtar = eslesen.Kokler is null ? null : eslesen.Brans;

        // Çocukta önce Çocuk Sağlığı, eşleşen bölüm alternatif (Senaryo 6: Çocuk Sağlığı / alt. KBB).
        var (oneri, alternatif) = yas < CocukYasSiniri
            ? (BransKodu.CocukSagligi, anahtar)
            : (anahtar ?? BransKodu.Dahiliye, anahtar is null or BransKodu.Dahiliye ? (BransKodu?)null : BransKodu.Dahiliye);
        if (!hekimliBranslar.Contains(oneri)) (oneri, alternatif) = (BransKodu.Dahiliye, null);
        if (alternatif is { } a && (!hekimliBranslar.Contains(a) || a == oneri)) alternatif = null;

        var ozet = $"Ana şikayet: {Bos(sikayet)}. Süre: {Bos(sure)}. Şiddet: {Bos(siddetCevabi)}. Ateş: {Bos(ates)}.";
        // Gerekçe yalnızca hekim kartında görünür.
        var gerekce = oneri switch
        {
            BransKodu.CocukSagligi => "18 yaş altı: önce Çocuk Sağlığı değerlendirmesi.",
            _ when anahtar is null || oneri != anahtar => "Şikayet belirli bir bölümle eşleşmedi; önce genel değerlendirme.",
            _ => "Şikayet bu bölümle ilişkili anahtar sözcükler içeriyor.",
        };

        return new OnAnamnezTaslagi(
            ozet, [Bos(sikayet)], Bos(sure), siddet, oneri, alternatif, gerekce, aciliyet, [],
            [new KaynakMaddesi(Bos(sikayet), 1), new KaynakMaddesi(Bos(sure), 2), new KaynakMaddesi(Bos(siddetCevabi), 3)]);
    }

    /// <summary>
    /// Şiddet 3. adımın cevabından; aciliyet şiddetli ya da 38 °C ve üstü ateşte Öncelikli. Model dalında da bu
    /// değerler modelinkinin üstüne yazılır (karar kodda).
    /// </summary>
    public static (Siddet Siddet, Aciliyet Aciliyet) SiddetVeAciliyet(IReadOnlyList<SohbetKaydi> sohbet)
    {
        string Katli(int adim) => TurkceMetin.Katla(sohbet.FirstOrDefault(k => k.Adim == adim)?.Cevap ?? "");
        var siddetCevabi = Katli(3);
        var siddet = TurkceMetin.KokVar(siddetCevabi, "siddetli") ? Siddet.Siddetli
            : TurkceMetin.KokVar(siddetCevabi, "hafif") ? Siddet.Hafif
            : Siddet.Orta;
        // Yalnızca sohbetteki "38 °C ve üstü" seçimi; serbest metindeki sayı aranmaz.
        var yuksekAtes = TurkceMetin.KokVar(Katli(4), "38 c ve ustu");
        return (siddet, siddet == Siddet.Siddetli || yuksekAtes ? Aciliyet.Oncelikli : Aciliyet.Rutin);
    }

    // Sondaki noktayı atar: şablonda "ağrıyor.." oluşmasın.
    private static string Bos(string s) => string.IsNullOrWhiteSpace(s) ? "belirtilmedi" : s.TrimEnd('.', ' ');
}
