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

    // Aşama 9 denetimi: farklı gruplama, eğik çizgi ve çift boşluk ayırıcı.
    [Theory]
    [InlineData("0532 12 34 567")]
    [InlineData("05321 23 45 67")]
    [InlineData("0532/123/45/67")]
    [InlineData("0532  123 45 67")]
    [InlineData("90 532 123 45 67")]
    [InlineData("0532 - 123 - 45 - 67")]
    [InlineData("0532_123_45_67")]
    [InlineData("0532   123 45 67")]
    [InlineData("0532–123–45–67")]
    public void Farkli_gruplanmis_telefon_maskelenir(string telefon)
    {
        Assert.Equal("ara [TELEFON] lütfen", Maskeleyici.Maskele($"ara {telefon} lütfen"));
    }

    [Fact]
    public void Ayiricili_tc_maskelenir()
    {
        var bosluklu = $"{GecerliTc[..3]} {GecerliTc[3..6]} {GecerliTc[6..9]} {GecerliTc[9..]}";
        Assert.Equal("TC: [TC]", Maskeleyici.Maskele($"TC: {bosluklu}"));
        var egik = $"{GecerliTc[..3]}/{GecerliTc[3..6]}/{GecerliTc[6..9]}/{GecerliTc[9..]}";
        Assert.Equal("TC: [TC]", Maskeleyici.Maskele($"TC: {egik}"));
        var ciftBosluk = $"{GecerliTc[..3]}  {GecerliTc[3..]}";
        Assert.Equal("TC: [TC]", Maskeleyici.Maskele($"TC: {ciftBosluk}"));
        var tireli = $"{GecerliTc[..3]} - {GecerliTc[3..6]} - {GecerliTc[6..9]} - {GecerliTc[9..]}";
        Assert.Equal("TC: [TC]", Maskeleyici.Maskele($"TC: {tireli}"));
    }

    // SPEC §9.2: doğum tarihi modele gönderilmez; hasta serbest metinde yazsa da maskelenir.
    [Theory]
    [InlineData("12.03.1971 doğumluyum", "[TARIH] doğumluyum")]
    [InlineData("doğum: 12/3/1971", "doğum: [TARIH]")]
    [InlineData("1971-03-12 doğumlu", "[TARIH] doğumlu")]
    [InlineData("12 Mart 1971'de doğdum", "[TARIH]'de doğdum")]
    [InlineData("3 AĞUSTOS 1985", "[TARIH]")]
    [InlineData("1 NİSAN 2001", "[TARIH]")]
    [InlineData("7 kasım 1990", "[TARIH]")]
    [InlineData("12 subat 1971", "[TARIH]")]
    [InlineData("12 Mart, 1971", "[TARIH]")]
    [InlineData("12.03.71 doğumluyum", "[TARIH] doğumluyum")]
    [InlineData("1971/03/12", "[TARIH]")]
    [InlineData("1971.03.12", "[TARIH]")]
    [InlineData("3 agustos 1985", "[TARIH]")]
    [InlineData("5 eylul 1990", "[TARIH]")]
    [InlineData("1 EKİM 1980", "[TARIH]")]
    [InlineData("2 MAYIS 1980", "[TARIH]")]
    [InlineData("3 aralık 1980", "[TARIH]")]
    [InlineData("4 ŞUBAT 1980", "[TARIH]")]
    [InlineData("5 EKİM 1980", "[TARIH]")]
    public void Tarih_maskelenir(string metin, string beklenen)
    {
        Assert.Equal(beklenen, Maskeleyici.Maskele(metin));
    }

    [Theory]
    [InlineData("Şekerim 7.4 çıktı")]
    [InlineData("3 gündür ateşim 38.5")]
    [InlineData("12 mart'tan beri öksürük")]
    [InlineData("1-3 gün")]
    [InlineData("kilo 58/62/65/71/74 kg")]
    [InlineData("glukoz 5.6 6.1 7.2 8.0 9.1")]
    [InlineData("nabız 50 60 70 80 90")]
    public void Olcum_ve_yilsiz_tarih_maskelenmez(string metin)
    {
        Assert.Equal(metin, Maskeleyici.Maskele(metin));
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
    [InlineData("Ayşesiz geldim", "[AD] geldim")]
    [InlineData("Kayalarınki", "[SOYAD]")]
    [InlineData("Kaya'larınki", "[SOYAD]")]
    [InlineData("Kaya'lardayız", "[SOYAD]")]
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
    public void Tam_genislikli_rakamlar_da_maskelenir()
    {
        var tamGenislik = string.Concat(GecerliTc.Select(c => (char)(c - '0' + '０')));
        Assert.Equal("TC: [TC]", Maskeleyici.Maskele($"TC: {tamGenislik}"));
        Assert.Equal("ara [TELEFON]", Maskeleyici.Maskele("ara ０５３２ 123 45 67"));
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
    public void Noktasiz_i_ile_yazilan_ad_da_maskelenir()
    {
        Assert.Equal("[AD] ve [SOYAD]", Maskeleyici.Maskele("HASAN ve YILDIZ'IN", "Hasan", "Yıldız"));
        Assert.Equal("[SOYAD]", Maskeleyici.Maskele("yildiz", "Hasan", "Yıldız"));
    }
}
