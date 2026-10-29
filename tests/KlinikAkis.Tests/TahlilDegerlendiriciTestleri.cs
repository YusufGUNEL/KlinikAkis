using KlinikAkis.Domain;
using KlinikAkis.Domain.Varliklar;

namespace KlinikAkis.Tests;

/// <summary>SPEC §11 test 5: cinsiyete özgü aralık, sınır değer = Normal, kritik eşikler.</summary>
public sealed class TahlilDegerlendiriciTestleri
{
    // Hemoglobin: K 12,0-15,5 · E 13,5-17,5 · kritik ≤7,0 / ≥20,0
    private static readonly TahlilTanimi Hemoglobin = new()
    {
        AltSinir = 12.0m, UstSinir = 15.5m, CinsiyeteGore = true, ErkekAltSinir = 13.5m, ErkekUstSinir = 17.5m,
        KritikAlt = 7.0m, KritikUst = 20.0m,
    };

    [Theory]
    [InlineData(Cinsiyet.K, 12.0, Isaret.Normal)]   // alt sınır dahil
    [InlineData(Cinsiyet.K, 15.5, Isaret.Normal)]   // üst sınır dahil
    [InlineData(Cinsiyet.K, 11.9, Isaret.Dusuk)]
    [InlineData(Cinsiyet.K, 15.6, Isaret.Yuksek)]
    [InlineData(Cinsiyet.E, 13.0, Isaret.Dusuk)]    // kadında normal, erkekte düşük
    [InlineData(Cinsiyet.E, 16.5, Isaret.Normal)]   // kadında yüksek, erkekte normal
    [InlineData(Cinsiyet.E, 17.5, Isaret.Normal)]
    [InlineData(Cinsiyet.K, 7.0, Isaret.KritikDusuk)]
    [InlineData(Cinsiyet.E, 7.1, Isaret.Dusuk)]
    [InlineData(Cinsiyet.E, 20.0, Isaret.KritikYuksek)]
    [InlineData(Cinsiyet.K, 19.9, Isaret.Yuksek)]
    public void Cinsiyete_ozgu_aralikla_isaret_uretir(Cinsiyet cinsiyet, double deger, Isaret beklenen) =>
        Assert.Equal(beklenen, TahlilDegerlendirici.Degerlendir(Hemoglobin, cinsiyet, (decimal)deger));

    [Fact]
    public void Cinsiyete_gore_olmayan_tanimda_iki_cinsiyet_ayni_araligi_kullanir()
    {
        var hba1c = new TahlilTanimi { AltSinir = 4.0m, UstSinir = 5.6m };

        Assert.Equal((4.0m, 5.6m), TahlilDegerlendirici.Aralik(hba1c, Cinsiyet.E));
        Assert.Equal(Isaret.Yuksek, TahlilDegerlendirici.Degerlendir(hba1c, Cinsiyet.K, 7.4m));
        Assert.Equal(Isaret.Yuksek, TahlilDegerlendirici.Degerlendir(hba1c, Cinsiyet.E, 7.4m));
    }
}
