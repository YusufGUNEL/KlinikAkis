using System.Text.Json;
using KlinikAkis.Domain;

namespace KlinikAkis.Infrastructure.Ai;

public sealed record DogrulamaSonucu(OnAnamnezTaslagi? Taslak, string? Neden);

/// <summary>
/// Sunucu doğrulaması (SPEC §9.5): şema biçimi garanti etse de karar kodda. Önerilen bölüm enum'da yoksa ya da o
/// bölümde hekim yoksa reddedilir (yedeğe düşülür). Alternatif bölüm geçersizse yalnızca o alan düşer. Kaynak
/// adımı aralık dışıysa madde kalır, "kaynak yok" (0) işaretlenir.
/// </summary>
public static class OnAnamnezDogrulayici
{
    public const string GecersizJson = "geçersiz JSON";
    public const string GecersizBolum = "geçersiz bölüm";
    public const int OzetSiniri = 600;
    public const int MaddeSiniri = 200;
    public const int SoruSiniri = 3;

    public static DogrulamaSonucu Dogrula(string? cikti, int adimSayisi, IReadOnlySet<BransKodu> hekimliBranslar)
    {
        var json = KodBloguSoy(cikti);
        if (json.Length == 0) return new(null, GecersizJson);

        OnAnamnezTaslagi? t;
        try
        {
            t = JsonSerializer.Deserialize<OnAnamnezTaslagi>(json, OnAnamnezTaslagi.Json);
        }
        catch (JsonException)
        {
            return new(null, BolumAlaniGecersiz(json) ? GecersizBolum : GecersizJson);
        }
        if (t is null || string.IsNullOrWhiteSpace(t.Ozet) || t.Semptomlar is null || t.Kaynaklar is null
            || t.DoktoraSorular is null || t.Sure is null || t.Gerekce is null
            || t.Semptomlar.Any(s => s is null) || t.DoktoraSorular.Any(s => s is null) || t.Kaynaklar.Any(k => k?.Madde is null)
            || !Enum.IsDefined(t.Siddet) || !Enum.IsDefined(t.Aciliyet))
            return new(null, GecersizJson);
        if (!Enum.IsDefined(t.OnerilenBrans) || !hekimliBranslar.Contains(t.OnerilenBrans))
            return new(null, GecersizBolum);

        var alternatif = t.AlternatifBrans is { } a && a != t.OnerilenBrans && hekimliBranslar.Contains(a) ? a : (BransKodu?)null;
        var kaynaklar = t.Kaynaklar
            .Select(k => new KaynakMaddesi(Kirp(k.Madde, MaddeSiniri),
                k.SohbetAdimi >= 1 && k.SohbetAdimi <= adimSayisi ? k.SohbetAdimi : 0))
            .ToList();
        // Uzun metin kartı bozmasın: alanlar kırpılır, soru sayısı sınırlanır.
        return new(t with
        {
            Ozet = Kirp(t.Ozet, OzetSiniri),
            Semptomlar = t.Semptomlar.Select(s => Kirp(s, MaddeSiniri)).ToList(),
            Sure = Kirp(t.Sure, MaddeSiniri),
            Gerekce = Kirp(t.Gerekce, MaddeSiniri),
            DoktoraSorular = t.DoktoraSorular.Take(SoruSiniri).Select(s => Kirp(s, MaddeSiniri)).ToList(),
            AlternatifBrans = alternatif,
            Kaynaklar = kaynaklar,
        }, null);
    }

    /// <summary>
    /// Sınıflandırma kodda (CLAUDE.md "karar kodda"): şiddet ve aciliyet sohbetten hesaplanıp modelinkinin üstüne
    /// yazılır. 18 yaş altında önerilen Çocuk Sağlığı'dır (modelin önerisi alternatife iner); yetişkine Çocuk Sağlığı
    /// önerilirse yanıt reddedilir, yedeğe düşülür.
    /// </summary>
    public static DogrulamaSonucu KodKurallari(OnAnamnezTaslagi t, IReadOnlyList<SohbetKaydi> sohbet, int yas,
        IReadOnlySet<BransKodu> hekimliBranslar)
    {
        var (siddet, aciliyet) = KuralTabanliYedek.SiddetVeAciliyet(sohbet);
        var (oneri, alternatif) = (t.OnerilenBrans, t.AlternatifBrans);
        if (yas < KuralTabanliYedek.CocukYasSiniri)
        {
            if (oneri != BransKodu.CocukSagligi && hekimliBranslar.Contains(BransKodu.CocukSagligi))
                (oneri, alternatif) = (BransKodu.CocukSagligi, oneri);
        }
        else
        {
            if (oneri == BransKodu.CocukSagligi) return new(null, GecersizBolum);
            if (alternatif == BransKodu.CocukSagligi) alternatif = null;
        }
        return new(t with { Siddet = siddet, Aciliyet = aciliyet, OnerilenBrans = oneri, AlternatifBrans = alternatif }, null);
    }

    private static string Kirp(string s, int sinir) => s.Length <= sinir ? s : s[..(sinir - 1)].TrimEnd() + "…";

    /// <summary>Şemayı yerel desteklemeyen model yanıtı ```json bloğuna sarabilir.</summary>
    private static string KodBloguSoy(string? metin)
    {
        var s = (metin ?? "").Trim();
        if (!s.StartsWith("```", StringComparison.Ordinal)) return s;
        var ilkSatir = s.IndexOf('\n');
        var son = s.LastIndexOf("```", StringComparison.Ordinal);
        return ilkSatir > 0 && son > ilkSatir ? s[(ilkSatir + 1)..son].Trim() : s;
    }

    private static bool BolumAlaniGecersiz(string json)
    {
        try
        {
            using var belge = JsonDocument.Parse(json);
            return belge.RootElement.TryGetProperty("onerilenBrans", out var b)
                && (b.ValueKind != JsonValueKind.String || !Enum.TryParse<BransKodu>(b.GetString(), ignoreCase: false, out _));
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
