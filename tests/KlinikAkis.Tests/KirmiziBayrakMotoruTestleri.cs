using KlinikAkis.Domain;

namespace KlinikAkis.Tests;

/// <summary>SPEC §9.3 ve §11 test 2: Türkçe çekim, büyük harf ve ASCII yazımla tetiklenir.</summary>
public sealed class KirmiziBayrakMotoruTestleri
{
    private const int Yetiskin = 61 * 12;

    [Theory]
    [InlineData("göğsüm çok ağrıyor, terledim")]
    [InlineData("GÖĞÜS AĞRISI VE TERLEME")]
    [InlineData("göğsüm ağrıyor, terledim")]
    [InlineData("gogsum agriyor terliyorum")]
    [InlineData("Göğsümde sıkışma var, sol koluma yayılıyor")]
    [InlineData("Kalbim sıkışıyor, sol koluma vuruyor, terliyorum")]
    public void Gogus_agrisi_ve_terleme_cekim_ve_buyuk_harfle_tetiklenir(string metin)
    {
        Assert.Equal(KirmiziBayrakKurali.GogusAgrisiTerleme, KirmiziBayrakMotoru.Degerlendir([metin], Yetiskin)?.Kural);
    }

    [Theory]
    [InlineData("GÖĞÜS AĞRISI")]
    [InlineData("göğsüm ağrıyor")]
    public void Yalniz_gogus_agrisi_da_tetiklenir(string metin)
    {
        Assert.Equal(KirmiziBayrakKurali.GogusAgrisi, KirmiziBayrakMotoru.Degerlendir([metin], Yetiskin)?.Kural);
    }

    [Fact]
    public void Belirtiler_farkli_adimlarda_yazilsa_da_birlesir()
    {
        var bayrak = KirmiziBayrakMotoru.Degerlendir(["Göğsüm ağrıyor", "1-3 gün", "Orta", "Yok", "Hiçbiri", "Bir de çok terledim"], Yetiskin);

        Assert.Equal(KirmiziBayrakKurali.GogusAgrisiTerleme, bayrak?.Kural);
    }

    [Theory]
    [InlineData("Yüzümde kayma var, konuşamıyorum", KirmiziBayrakKurali.InmeBulgusu)]
    [InlineData("Yüzümde kayma var", KirmiziBayrakKurali.InmeBulgusu)]
    [InlineData("Babamın yüzü kaydı", KirmiziBayrakKurali.InmeBulgusu)]
    [InlineData("Konuşmam bozuldu", KirmiziBayrakKurali.InmeBulgusu)]
    [InlineData("Sol tarafım uyuştu", KirmiziBayrakKurali.InmeBulgusu)]
    [InlineData("Yüzümde kayma / konuşma bozukluğu", KirmiziBayrakKurali.InmeBulgusu)]
    [InlineData("Nefesim daralıyor", KirmiziBayrakKurali.AgirNefesDarligi)]
    [InlineData("Nefes almakta zorlanıyorum", KirmiziBayrakKurali.AgirNefesDarligi)]
    [InlineData("Hamileyim, kan geldi", KirmiziBayrakKurali.GebelikKanama)]
    [InlineData("Nefes alamıyorum", KirmiziBayrakKurali.AgirNefesDarligi)]
    [InlineData("Nefes darlığı", KirmiziBayrakKurali.AgirNefesDarligi)]
    [InlineData("Sabah bayıldım", KirmiziBayrakKurali.BilincKaybi)]
    [InlineData("İNTİHAR etmeyi düşünüyorum", KirmiziBayrakKurali.KendineZarar)]
    [InlineData("Kendime zarar verme düşüncesi", KirmiziBayrakKurali.KendineZarar)]
    [InlineData("Hamileyim ve kanamam var", KirmiziBayrakKurali.GebelikKanama)]
    public void Diger_kurallar(string metin, KirmiziBayrakKurali beklenen)
    {
        Assert.Equal(beklenen, KirmiziBayrakMotoru.Degerlendir([metin], Yetiskin)?.Kural);
    }

    [Fact]
    public void Uc_ay_alti_bebekte_ates_tetiklenir_buyuk_cocukta_tetiklenmez()
    {
        string[] sohbet = ["Huzursuz", "1 günden az", "Orta", "38 °C ve üstü"];

        Assert.Equal(KirmiziBayrakKurali.BebekAtes, KirmiziBayrakMotoru.Degerlendir(sohbet, 2)?.Kural);
        Assert.Null(KirmiziBayrakMotoru.Degerlendir(sohbet, 7 * 12));
        Assert.Null(KirmiziBayrakMotoru.Degerlendir(["Huzursuz", "38 °C altı"], 2));
        Assert.Equal(KirmiziBayrakKurali.BebekAtes, KirmiziBayrakMotoru.Degerlendir(["38,5 derece"], 2)?.Kural);
    }

    [Theory]
    [InlineData("Son haftalarda çok su içiyorum, sık idrara çıkıyorum, halsizim.")]
    [InlineData("3 gündür boğaz ağrısı ve 38,4 ateş")]
    [InlineData("Kilo alıyorum, sürekli üşüyorum")]
    [InlineData("Hiçbiri")]
    [InlineData("Hamileyim, kanepeye uzandım, belim ağrıyor")]
    public void Senaryo_sikayetleri_tetiklemez(string metin)
    {
        Assert.Null(KirmiziBayrakMotoru.Degerlendir([metin], Yetiskin));
    }

    [Fact]
    public void Olumsuzlama_ayirt_edilmez_fazla_tetiklemek_guvenli_taraf()
    {
        Assert.NotNull(KirmiziBayrakMotoru.Degerlendir(["Göğüs ağrım yok"], Yetiskin));
    }
}
