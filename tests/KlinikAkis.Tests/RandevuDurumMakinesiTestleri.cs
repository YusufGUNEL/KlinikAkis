using KlinikAkis.Domain;

namespace KlinikAkis.Tests;

/// <summary>SPEC §11 test 9 (birim kısmı): durum makinesi tek kural kaynağı.</summary>
public sealed class RandevuDurumMakinesiTestleri
{
    [Theory]
    [InlineData(RandevuDurumu.Planlandi, RandevuEylemi.Kabul, RandevuDurumu.KabulEdildi)]
    [InlineData(RandevuDurumu.Planlandi, RandevuEylemi.Iptal, RandevuDurumu.Iptal)]
    [InlineData(RandevuDurumu.KabulEdildi, RandevuEylemi.Cagir, RandevuDurumu.Cagrildi)]
    [InlineData(RandevuDurumu.KabulEdildi, RandevuEylemi.Gelmedi, RandevuDurumu.Gelmedi)]
    [InlineData(RandevuDurumu.Cagrildi, RandevuEylemi.Basla, RandevuDurumu.Muayenede)]
    [InlineData(RandevuDurumu.Muayenede, RandevuEylemi.Kapat, RandevuDurumu.Tamamlandi)]
    public void Gecerli_gecis_yeni_durumu_verir(RandevuDurumu mevcut, RandevuEylemi eylem, RandevuDurumu beklenen) =>
        Assert.Equal(beklenen, RandevuDurumMakinesi.Gecis(mevcut, eylem));

    [Fact]
    public void Tablo_disindaki_her_gecis_reddedilir()
    {
        var gecerli = 0;
        foreach (var durum in Enum.GetValues<RandevuDurumu>())
        foreach (var eylem in Enum.GetValues<RandevuEylemi>())
        {
            if (RandevuDurumMakinesi.IzinliEylemler(durum).Contains(eylem))
            {
                gecerli++;
                continue;
            }
            Assert.Throws<GecersizDurumGecisiException>(() => RandevuDurumMakinesi.Gecis(durum, eylem));
        }
        Assert.Equal(6, gecerli);
    }

    [Fact]
    public void Gecersiz_gecis_mesaji_Turkce_gorunen_adlarla()
    {
        var hata = Assert.Throws<GecersizDurumGecisiException>(
            () => RandevuDurumMakinesi.Gecis(RandevuDurumu.Planlandi, RandevuEylemi.Cagir));
        Assert.Equal("Randevu 'Planlandı' durumundayken çağrılamaz.", hata.Message);
    }

    [Theory]
    [InlineData(RandevuDurumu.Tamamlandi)]
    [InlineData(RandevuDurumu.Iptal)]
    [InlineData(RandevuDurumu.Gelmedi)]
    public void Son_durumlarda_eylem_yok(RandevuDurumu durum) =>
        Assert.Empty(RandevuDurumMakinesi.IzinliEylemler(durum));

    [Fact]
    public void TakipNo_ve_SiraNo_bicimi()
    {
        Assert.Equal("SIM-000123", TakipNo.Olustur(123));
        Assert.Equal(17, SiraNo.Sira(SiraNo.Olustur(17)));
        Assert.Equal(0, SiraNo.Sira("bozuk"));
    }
}
