using KlinikAkis.Domain;

namespace KlinikAkis.Tests;

public sealed class TurkceMetinTestleri
{
    [Theory]
    [InlineData("İNTİHAR", "intihar")]
    [InlineData("IŞIK", "ışık")]
    [InlineData("Ayşe'nin   telefonu!", "ayşenin telefonu")]
    [InlineData("Kaya’ya,  söyle", "kayaya söyle")]
    [InlineData("  ", "")]
    public void Normalize_tr_kucuk_harf_kesme_ve_bosluk(string girdi, string beklenen)
    {
        Assert.Equal(beklenen, TurkceMetin.Normalize(girdi));
    }

    [Fact]
    public void Katla_turkce_harfleri_asciiye_indirir()
    {
        Assert.Equal("gogsum agriyor", TurkceMetin.Katla("GÖĞSÜM AĞRIYOR"));
        Assert.Equal("isik isik", TurkceMetin.Katla("IŞIK İşık"));
        Assert.Equal("ayse", TurkceMetin.Katla("Ayşe".Normalize(System.Text.NormalizationForm.FormD)));
    }

    [Fact]
    public void Kok_yalnizca_sozcuk_basinda_eslesir()
    {
        Assert.True(TurkceMetin.KokVar("cok terledim", "terl"));
        Assert.False(TurkceMetin.KokVar("hepterl", "terl"));
    }
}
