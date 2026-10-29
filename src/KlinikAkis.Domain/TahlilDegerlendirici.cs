using KlinikAkis.Domain.Varliklar;

namespace KlinikAkis.Domain;

/// <summary>
/// Tahlil işaretini deterministik olarak hesaplar (SPEC §9.6). LLM bu kararı vermez.
/// Sınır değerler dahildir (= Normal); kritik eşiğe eşit ya da ötesi kritiktir.
/// </summary>
public static class TahlilDegerlendirici
{
    public static (decimal Alt, decimal Ust) Aralik(TahlilTanimi tanim, Cinsiyet cinsiyet) =>
        tanim.CinsiyeteGore && cinsiyet == Cinsiyet.E && tanim.ErkekAltSinir is { } alt && tanim.ErkekUstSinir is { } ust
            ? (alt, ust)
            : (tanim.AltSinir, tanim.UstSinir);

    public static Isaret Degerlendir(TahlilTanimi tanim, Cinsiyet cinsiyet, decimal deger)
    {
        if (tanim.KritikUst is { } kritikUst && deger >= kritikUst) return Isaret.KritikYuksek;
        if (tanim.KritikAlt is { } kritikAlt && deger <= kritikAlt) return Isaret.KritikDusuk;

        var (alt, ust) = Aralik(tanim, cinsiyet);
        if (deger > ust) return Isaret.Yuksek;
        if (deger < alt) return Isaret.Dusuk;
        return Isaret.Normal;
    }
}
