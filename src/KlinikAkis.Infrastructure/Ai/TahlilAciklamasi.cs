using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KlinikAkis.Domain;
using Microsoft.Extensions.AI;

namespace KlinikAkis.Infrastructure.Ai;

/// <summary>
/// Tahlil açıklaması için modele gidenlerin tamamı (SPEC §9.6): test adı, değer, birim, aralık, koddan gelen işaret ve
/// son 3 değer. Hastaya dair kimlik bilgisi (ad, takma ad, yaş, TC) bu tipte hiç yoktur.
/// </summary>
/// <param name="Aralik">Hastanın cinsiyetine göre seçilmiş referans aralık.</param>
/// <param name="Trend">Eskiden yeniye en fazla 3 değer; sonuncusu açıklanan sonuçtur.</param>
/// <param name="Ondalik">Değerlerin gösterim basamağı ("7,0", "128").</param>
public sealed record TahlilGirdisi(
    string Ad, string HastaDostuAd, string Birim, decimal Alt, decimal Ust, Isaret Isaret, decimal Deger,
    IReadOnlyList<decimal> Trend, int Ondalik)
{
    public string Sayi(decimal d) => TurkceMetin.Sayi(d, Ondalik);

    public string TestAdi => $"{HastaDostuAd} ({Ad})";

    /// <summary>Modele giden metin; denetim kaydına ve "modele giden veri" paneline aynen yazılır.</summary>
    public string MaskeliMetin() =>
        new StringBuilder()
            .Append("tahlil: ").Append(TestAdi).Append('\n')
            .Append("sonuç: ").Append(Sayi(Deger)).Append(' ').Append(Birim).Append('\n')
            .Append("referans aralık: ").Append(Sayi(Alt)).Append('–').Append(Sayi(Ust)).Append(' ').Append(Birim).Append('\n')
            .Append("işaret (kodla hesaplandı): ").Append(IsaretMetni(Isaret)).Append('\n')
            .Append("son değerler (eskiden yeniye): ").Append(string.Join(" → ", Trend.Select(Sayi)))
            .ToString();

    public static string IsaretMetni(Isaret isaret) => isaret switch
    {
        Isaret.Normal => "referans aralığında",
        Isaret.Yuksek => "referans aralığının üstünde",
        Isaret.Dusuk => "referans aralığının altında",
        Isaret.KritikYuksek => "çok yüksek (kritik değer)",
        Isaret.KritikDusuk => "çok düşük (kritik değer)",
        _ => throw new ArgumentOutOfRangeException(nameof(isaret)),
    };

    public static bool Kritik(Isaret isaret) => isaret is Isaret.KritikYuksek or Isaret.KritikDusuk;
}

/// <summary>LLM'den beklenen tek alan (SPEC §9.6): <c>{ "aciklama": "string" }</c>.</summary>
public sealed record TahlilAciklamaYaniti(string Aciklama)
{
    public const string SemaAdi = "tahlil_aciklama";

    public static readonly ChatResponseFormatJson YanitBicimi = ChatResponseFormat.ForJsonSchema(
        AIJsonUtilities.TransformSchema(
            AIJsonUtilities.CreateJsonSchema(typeof(TahlilAciklamaYaniti), serializerOptions: OnAnamnezTaslagi.Json),
            new AIJsonSchemaTransformOptions { DisallowAdditionalProperties = true, RequireAllProperties = true }),
        SemaAdi, "Tahlil sonucunun hastaya sade dille açıklaması");

    public string JsonOlarak() => JsonSerializer.Serialize(this, OnAnamnezTaslagi.Json);
}

/// <summary>
/// Sunucu doğrulaması (SPEC §9.6): boş, 600 karakterden uzun ya da koddan gelen işaretle çelişen yön içeren açıklama
/// ya da girdide olmayan sayı yazan açıklama reddedilir, yedek şablona düşülür. Yön kontrolü <see cref="TurkceMetin"/>
/// ile katlanmış metinde kök aramasıdır. Kritik değerde iletişim cümlesi yoksa kod ekler (modele bırakılmaz).
/// </summary>
public static partial class TahlilAciklamaDogrulayici
{
    public const int Sinir = 600;
    public const string GecersizJson = "geçersiz JSON";
    public const string Uzun = "600 karakterden uzun";
    public const string CelisenYon = "işaretle çelişen yön";
    public const string UydurmaSayi = "girdide olmayan sayı";
    public const string OlmayanTrend = "tek ölçümde trend";
    public const string IletisimCumlesi = "Doktorunuz sizinle iletişime geçecek.";

    private static readonly string[] NormalIfadeleri =
        ["normal aralikta", "normal sinirlarda", "normal duzeyde", "referans araliginda", "referans araliginin icinde",
         "aralik icinde", "araligin icinde", "normal degerde", "sinirlar icinde", "olagan"];

    public static (string? Aciklama, string? Neden) Dogrula(string? cikti, TahlilGirdisi girdi)
    {
        TahlilAciklamaYaniti? y;
        try
        {
            y = JsonSerializer.Deserialize<TahlilAciklamaYaniti>(KodBloguSoy(cikti), OnAnamnezTaslagi.Json);
        }
        catch (JsonException)
        {
            return (null, GecersizJson);
        }
        var metin = y?.Aciklama?.Trim();
        if (string.IsNullOrEmpty(metin)) return (null, GecersizJson);
        if (metin.Length > Sinir) return (null, Uzun);
        if (Celisiyor(metin, girdi.Isaret)) return (null, CelisenYon);
        if (!SayilarGirdide(metin, girdi)) return (null, UydurmaSayi);
        if (girdi.Trend.Count < 2 && TrendAnlatiyor(metin)) return (null, OlmayanTrend);
        if (TahlilGirdisi.Kritik(girdi.Isaret) && !TurkceMetin.KokVar(TurkceMetin.Katla(metin), "iletisime gec"))
            metin = $"{metin} {IletisimCumlesi}";
        return (metin, null);
    }

    /// <summary>Tek ölçüm varken "önceki ölçüm", "artıyor", "aynı seviyede" gibi değişim anlatımı uydurmadır.</summary>
    public static bool TrendAnlatiyor(string metin)
    {
        var k = TurkceMetin.Katla(metin);
        return new[] { "onceki", "artiyor", "azaliyor", "artmis", "azalmis", "ayni seviye", "yukseliyor", "dusuyor" }
            .Any(kok => TurkceMetin.KokVar(k, kok));
    }

    /// <summary>
    /// Metindeki her sayı değer, aralık sınırı ya da trend değerlerinden biri olmalı ("4,7" yazılırsa 7,4 değil → ret).
    /// "HbA1c" içindeki 1 gibi harfe bitişik rakamlar sayı sayılmaz; 1-3 arası tam sayı ölçüm adedi olabilir.
    /// </summary>
    public static bool SayilarGirdide(string metin, TahlilGirdisi g)
    {
        decimal[] izinli = [g.Deger, g.Alt, g.Ust, .. g.Trend];
        foreach (Match m in SayiDeseni().Matches(metin))
        {
            var d = decimal.Parse(m.Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            if (!izinli.Contains(d) && !(d is >= 1 and <= 3 && d == decimal.Truncate(d))) return false;
        }
        return true;
    }

    [GeneratedRegex(@"(?<![\p{L}\d,.])\d+(?:[,.]\d+)?(?![\p{L}\d])")]
    private static partial Regex SayiDeseni();

    /// <summary>Yüksekte "düşük"/"normal", düşükte "yüksek"/"normal aralıkta", normalde "yüksek"/"düşük" geçemez.</summary>
    public static bool Celisiyor(string metin, Isaret isaret)
    {
        var k = TurkceMetin.Katla(metin);
        bool Kok(string kok) => TurkceMetin.KokVar(k, kok);
        // Yalın "normal" de çelişkidir ("Sonucunuz normal."); "normal aralığın üstünde" ise yönü doğru söyler.
        bool NormalDiyor() => NormalIfadeleri.Any(Kok)
            || ((TurkceMetin.SozcukVar(k, "normal") || TurkceMetin.SozcukVar(k, "normaldir")) && !Kok("normal aralig"));
        return isaret switch
        {
            Isaret.Yuksek or Isaret.KritikYuksek => Kok("dusuk") || NormalDiyor(),
            Isaret.Dusuk or Isaret.KritikDusuk => Kok("yuksek") || NormalDiyor(),
            _ => Kok("yuksek") || Kok("dusuk"),
        };
    }

    private static string KodBloguSoy(string? metin)
    {
        var s = (metin ?? "").Trim();
        if (!s.StartsWith("```", StringComparison.Ordinal)) return s;
        var ilkSatir = s.IndexOf('\n');
        var son = s.LastIndexOf("```", StringComparison.Ordinal);
        return ilkSatir > 0 && son > ilkSatir ? s[(ilkSatir + 1)..son].Trim() : s;
    }
}

/// <summary>Model yanıt vermezse ya da reddedilirse: işarete ve trende göre deterministik açıklama (kural tabanlı).</summary>
public static class TahlilYedekSablonu
{
    public static string Olustur(TahlilGirdisi g)
    {
        var aralik = $"({g.Sayi(g.Alt)}–{g.Sayi(g.Ust)} {g.Birim})";
        var sb = new StringBuilder()
            .Append(g.HastaDostuAd).Append(" sonucunuz ").Append(g.Sayi(g.Deger)).Append(' ').Append(g.Birim).Append("; ")
            .Append(g.Isaret switch
            {
                Isaret.Normal => $"bu değer referans aralığında {aralik}.",
                Isaret.Yuksek => $"bu değer referans aralığının üstünde {aralik}.",
                Isaret.Dusuk => $"bu değer referans aralığının altında {aralik}.",
                Isaret.KritikYuksek => $"bu değer referans aralığının çok üstünde {aralik}.",
                _ => $"bu değer referans aralığının çok altında {aralik}.",
            });
        if (g.Trend.Count >= 2)
        {
            var (ilk, son) = (g.Trend[0], g.Trend[^1]);
            sb.Append(' ').Append(son > ilk ? "Önceki ölçümlere göre artıyor."
                : son < ilk ? "Önceki ölçümlere göre azalıyor." : "Önceki ölçümlerle aynı.");
        }
        sb.Append(' ').Append(TahlilGirdisi.Kritik(g.Isaret)
            ? "Doktorunuz sizinle iletişime geçecek."
            : "Bu sonucu doktorunuzla konuşun.");
        return sb.ToString();
    }
}
