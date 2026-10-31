using KlinikAkis.Domain;
using KlinikAkis.Infrastructure.Ai;

namespace KlinikAkis.Tests;

/// <summary>SPEC §9.2 ve §11 test 3 (TC, telefon, e-posta), test 4 (ekli ad).</summary>
public sealed class MaskeleyiciTestleri
{
    private static readonly string GecerliTc = TcKimlik.SaglamaEkle("991000001");

    [Fact]
    public void Gecerli_tc_maskelenir_saglamasi_tutmayan_11_hane_maskelenmez()
    {
        var gecersiz = GecerliTc[..10] + (char)('0' + (GecerliTc[10] - '0' + 1) % 10);

        Assert.Equal("TC: [TC]", Maskeleyici.Maskele($"TC: {GecerliTc}"));
        Assert.Equal($"no {gecersiz}", Maskeleyici.Maskele($"no {gecersiz}"));
    }

    [Theory]
    [InlineData("+90 532 123 45 67")]
    [InlineData("+905321234567")]
    [InlineData("0532 123 45 67")]
    [InlineData("05321234567")]
    [InlineData("0 532-123-45-67")]
    [InlineData("(532) 123 45 67")]
    [InlineData("532 123 4567")]
    [InlineData("0532.123.45.67")]
    [InlineData("0212 555 44 33")]
    public void Telefon_bicimleri_maskelenir(string telefon)
    {
        Assert.Equal("ara [TELEFON] lütfen", Maskeleyici.Maskele($"ara {telefon} lütfen"));
    }

    [Fact]
    public void Ayiricili_tc_maskelenir()
    {
        var bosluklu = $"{GecerliTc[..3]} {GecerliTc[3..6]} {GecerliTc[6..9]} {GecerliTc[9..]}";
        Assert.Equal("TC: [TC]", Maskeleyici.Maskele($"TC: {bosluklu}"));
    }

    // Denetçinin bulduğu kaçaklar: görünmez karakterle bölünmüş ad ve listede olmayan ekler.
    [Theory]
    [InlineData("Ay\u200Bşe Ka\u00ADya", "[AD] [SOYAD]")]
    [InlineData("Ayşemin annesi", "[AD] annesi")]
    [InlineData("Ayşeyken", "[AD]")]
    [InlineData("Ayşegilde kaldım", "[AD] kaldım")]
    [InlineData("AYŞEMİZ", "[AD]")]
    [InlineData("Ays‍̧e", "[AD]")]
    [InlineData("Ayşeninki", "[AD]")]
    [InlineData("Ayşedeki", "[AD]")]
    [InlineData("Ayşeydim", "[AD]")]
    [InlineData("Kayadaki", "[SOYAD]")]
    public void Gorunmez_karakter_ve_ek_kacaklari_maskelenir(string metin, string beklenen)
    {
        Assert.Equal(beklenen, Maskeleyici.Maskele(metin, "Ayşe", "Kaya"));
    }

    // Ünsüz yumuşaması ve ek-fiil: "Mehmet" → "Mehmed'in", "Mehmedin", "Mehmettir".
    [Theory]
    [InlineData("Mehmedin başı ağrıyor", "[AD] başı ağrıyor")]
    [InlineData("Mehmed'in", "[AD]")]
    [InlineData("Mehmettir", "[AD]")]
    [InlineData("Mehmetti", "[AD]")]
    [InlineData("ben Mehmettim", "ben [AD]")]
    [InlineData("Tekinlerdeyiz", "[SOYAD]")]
    public void Yumusama_ve_ek_fiil_maskelenir(string metin, string beklenen)
    {
        Assert.Equal(beklenen, Maskeleyici.Maskele(metin, "Mehmet", "Tekin"));
    }

    [Fact]
    public void Eposta_maskelenir()
    {
        Assert.Equal("yaz: [EPOSTA].", Maskeleyici.Maskele("yaz: ayse.kaya@ornek.test."));
    }

    [Theory]
    [InlineData("Ayşe'nin", "[AD]")]
    [InlineData("AYŞE", "[AD]")]
    [InlineData("ayşenin", "[AD]")]
    [InlineData("Ayşe'ye", "[AD]")]
    [InlineData("AYŞE'YE", "[AD]")]
    [InlineData("Ayse", "[AD]")]
    [InlineData("Kaya'ya", "[SOYAD]")]
    [InlineData("KAYAYA", "[SOYAD]")]
    [InlineData("Ayşegül", "Ayşegül")]
    [InlineData("Kayalık", "Kayalık")]
    [InlineData("Ayşeyim", "[AD]")]
    [InlineData("Kayayım", "[SOYAD]")]
    [InlineData("Ayşeciğim", "[AD]")]
    [InlineData("Kayalardan", "[SOYAD]")]
    [InlineData("Kaya'dan", "[SOYAD]")]
    [InlineData("Kayadan", "[SOYAD]")]
    public void Ekli_ad_ve_soyad_maskelenir_baska_isim_maskelenmez(string sozcuk, string beklenen)
    {
        Assert.Equal($"bu {beklenen} için", Maskeleyici.Maskele($"bu {sozcuk} için", "Ayşe", "Kaya"));
    }

    [Fact]
    public void Demo_cumlesi()
    {
        Assert.Equal("[AD] telefonu [TELEFON]", Maskeleyici.Maskele("Ayşe'nin telefonu 0532 123 45 67", "Ayşe", "Kaya"));
    }

    [Fact]
    public void Nfd_girdi_ve_iki_parcali_ad_maskelenir()
    {
        var nfd = "Ayşe'nin".Normalize(System.Text.NormalizationForm.FormD);
        Assert.Equal("[AD] telefonu", Maskeleyici.Maskele($"{nfd} telefonu", "Ayşe", "Kaya"));
        Assert.Equal("[AD] [AD] geldi", Maskeleyici.Maskele("Ayşe Nur geldi", "Ayşe Nur", "Kaya"));
        Assert.Equal("[SOYAD] [SOYAD]", Maskeleyici.Maskele("Kaya Demir", "Ayşe", "Kaya-Demir"));
    }

    [Fact]
    public void Noktali_I_ve_klavyesiz_yazim()
    {
        Assert.Equal("[AD] [AD] [AD]", Maskeleyici.Maskele("İSMAİL'İN ismailin ISMAIL", "İsmail", "Kaya"));
    }

    [Fact]
    public void Noktali_i_ile_yazilan_ad_da_maskelenir()
    {
        Assert.Equal("[AD] ve [SOYAD]", Maskeleyici.Maskele("HASAN ve YILDIZ'IN", "Hasan", "Yıldız"));
        Assert.Equal("[SOYAD]", Maskeleyici.Maskele("yildiz", "Hasan", "Yıldız"));
    }
}
