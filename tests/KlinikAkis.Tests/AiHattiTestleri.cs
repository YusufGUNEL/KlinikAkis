using System.Net;
using System.Text.Json;
using KlinikAkis.Domain;
using KlinikAkis.Domain.Varliklar;
using KlinikAkis.Infrastructure.Ai;
using KlinikAkis.Infrastructure.Seed;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace KlinikAkis.Tests;

/// <summary>AiHatti dalları (SPEC §9.1, §9.5, §9.9; §11 test 6, 7): doğrulama, yedek, zaman aşımı, kota.</summary>
public sealed class AiHattiTestleri(KlinikApiFabrikasi fabrika, ITestOutputHelper cikti) : IClassFixture<KlinikApiFabrikasi>
{
    private static readonly HashSet<BransKodu> Hekimli =
        [BransKodu.Endokrinoloji, BransKodu.Dahiliye, BransKodu.Kbb, BransKodu.CocukSagligi, BransKodu.Kardiyoloji];

    internal static AiHatti Hatti(IServiceProvider sp, IChatClient istemci, GunlukKota? kota = null, TimeSpan? zamanAsimi = null)
    {
        var zaman = sp.GetRequiredService<TimeProvider>();
        return new AiHatti(istemci, kota ?? new GunlukKota(zaman, 300),
            sp.GetRequiredService<KlinikDbContext>(), zaman,
            // Asıl sağlayıcı "Gemini" gibi davranır (kota sayılır); istemci yine sahte.
            new AiAyarlari(AiAyarlari.Gemini, FakeChatClient.ModelAdi, 300, zamanAsimi ?? TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public async Task Gecerli_yanit_Llm_taslagi_olur_ve_denetime_dogrulanmis_JSON_yazilir()
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var sahte = new FakeChatClient { Gecikme = TimeSpan.Zero };
        var hatti = Hatti(kapsam.ServiceProvider, sahte);

        var sonuc = await hatti.OnAnamnezOzetiAsync(await HastaAsync(kapsam, SenaryoIdleri.Ayse), AyseSohbeti, default);

        Assert.Equal(AiKaynak.Llm, sonuc.Kaynak);
        Assert.Equal(BransKodu.Endokrinoloji, sonuc.Taslak!.OnerilenBrans);
        Assert.Equal(FakeChatClient.ModelAdi, sonuc.Model);
        Assert.False(sonuc.KotaDoldu);
        var kayit = Son(kapsam);
        Assert.True(kayit.Basarili);
        Assert.False(kayit.YedegeDustu);
        Assert.Equal(AiHatti.OnAnamnezIstemSurumu, kayit.IstemSurumu);
        Assert.Contains("\"onerilenBrans\":\"Endokrinoloji\"", kayit.CiktiJson);
    }

    // §11 test 6: listede olmayan bölüm reddedilir, yedeğe düşülür.
    [Theory]
    [InlineData("Astroloji")]      // enum'da yok
    [InlineData("Noroloji")]       // enum'da var ama bu hastanede hekimi yok
    public async Task Listede_olmayan_bolum_yedege_duser(string bolum)
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var sahte = new FakeChatClient { Gecikme = TimeSpan.Zero, Yanitla = _ => GecerliJson(onerilen: bolum) };
        var hatti = Hatti(kapsam.ServiceProvider, sahte);

        var sonuc = await hatti.OnAnamnezOzetiAsync(await HastaAsync(kapsam, SenaryoIdleri.Ayse), AyseSohbeti, default);

        Assert.Equal(AiKaynak.Yedek, sonuc.Kaynak);
        Assert.Null(sonuc.Model);
        // Kural tabanlı yedek de bölüm önerir: "su içiyorum, idrar" → Endokrinoloji.
        Assert.Equal(BransKodu.Endokrinoloji, sonuc.Taslak!.OnerilenBrans);
        var kayit = Son(kapsam);
        Assert.True(kayit.YedegeDustu);
        Assert.False(kayit.Basarili);
        Assert.Equal(OnAnamnezDogrulayici.GecersizBolum, kayit.Neden);
        Assert.Contains(bolum, kayit.CiktiJson);   // modelin ham çıktısı denetimde kalır
    }

    // §11 test 7: zaman aşımında (test için kısaltılmış süre) yedeğe düşülür.
    [Fact]
    public async Task Zaman_asiminda_yedege_duser()
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var sahte = new FakeChatClient { Gecikme = TimeSpan.FromSeconds(5) };
        var hatti = Hatti(kapsam.ServiceProvider, sahte, zamanAsimi: TimeSpan.FromMilliseconds(100));

        var sonuc = await hatti.OnAnamnezOzetiAsync(await HastaAsync(kapsam, SenaryoIdleri.Mehmet), MehmetSohbeti, default);

        Assert.Equal(AiKaynak.Yedek, sonuc.Kaynak);
        Assert.Equal(BransKodu.Kbb, sonuc.Taslak!.OnerilenBrans);
        var kayit = Son(kapsam);
        Assert.True(kayit.YedegeDustu);
        Assert.Equal("zaman aşımı", kayit.Neden);
        Assert.True(kayit.GecikmeMs < 5000);
    }

    [Fact]
    public async Task Gunluk_kota_dolunca_saglayici_cagrilmaz_hastanin_cevaplarindan_yedek_doner()
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var asil = new FakeChatClient { Gecikme = TimeSpan.Zero };
        var kota = new GunlukKota(kapsam.ServiceProvider.GetRequiredService<TimeProvider>(), limit: 0);
        var hatti = Hatti(kapsam.ServiceProvider, asil, kota);

        var sonuc = await hatti.OnAnamnezOzetiAsync(await HastaAsync(kapsam, SenaryoIdleri.Ayse), AyseSohbeti, default);

        Assert.Equal(0, asil.CagriSayisi);
        Assert.True(sonuc.KotaDoldu);
        Assert.Equal(AiKaynak.Yedek, sonuc.Kaynak);
        // Hazır örnek metin değil, hastanın kendi şikayeti.
        Assert.Contains("çok su içiyorum", sonuc.Taslak!.Ozet, StringComparison.Ordinal);
        var kayit = Son(kapsam);
        Assert.Equal(AiHatti.KotaNedeni, kayit.Neden);
        Assert.True(kayit.YedegeDustu);
    }

    // Fake da §10.2'ye uyar: madde yalnızca hastanın söylediğinden gelir ve gerçek adıma bağlanır.
    [Fact]
    public async Task Fake_taslak_maddeleri_hastanin_cevaplarina_ve_dogru_adima_baglidir()
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var hatti = Hatti(kapsam.ServiceProvider, new FakeChatClient { Gecikme = TimeSpan.Zero });

        var sonuc = await hatti.OnAnamnezOzetiAsync(await HastaAsync(kapsam, SenaryoIdleri.Mehmet), MehmetSohbeti, default);

        var t = sonuc.Taslak!;
        Assert.Equal(["Boğaz ağrısı", "Ateş"], t.Semptomlar);
        Assert.Equal([new KaynakMaddesi("Boğaz ağrısı", 1), new KaynakMaddesi("Ateş", 4)], t.Kaynaklar);
        Assert.Equal("1-3 gün", t.Sure);
    }

    // Fake, koddaki "çocuk: hayır" satırına uyar: yetişkin kulak ağrısında Çocuk Sağlığı önerip yedeğe düşmez.
    [Fact]
    public async Task Fake_yetiskin_kulak_agrisinda_Kbb_onerir()
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var hatti = Hatti(kapsam.ServiceProvider, new FakeChatClient { Gecikme = TimeSpan.Zero });
        IReadOnlyList<SohbetKaydi> sohbet = [.. MehmetSohbeti.Select(k => k.Adim == 1 ? k with { Metin = "2 gündür kulağım ağrıyor" } : k)];

        var sonuc = await hatti.OnAnamnezOzetiAsync(await HastaAsync(kapsam, SenaryoIdleri.Mehmet), sohbet, default);

        Assert.Equal(AiKaynak.Llm, sonuc.Kaynak);
        Assert.Equal(BransKodu.Kbb, sonuc.Taslak!.OnerilenBrans);
    }

    [Fact]
    public async Task Fake_modda_kota_sayilmaz_rozet_cikmaz()
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var sp = kapsam.ServiceProvider;
        var zaman = sp.GetRequiredService<TimeProvider>();
        var hatti = new AiHatti(new FakeChatClient { Gecikme = TimeSpan.Zero }, new GunlukKota(zaman, limit: 0),
            sp.GetRequiredService<KlinikDbContext>(), zaman,
            new AiAyarlari(FakeChatClient.Saglayici, FakeChatClient.ModelAdi, 0, TimeSpan.FromSeconds(15)));

        var sonuc = await hatti.OnAnamnezOzetiAsync(await HastaAsync(kapsam, SenaryoIdleri.Ayse), AyseSohbeti, default);

        Assert.Equal(AiKaynak.Llm, sonuc.Kaynak);
        Assert.False(sonuc.KotaDoldu);
    }

    [Fact]
    public async Task Saglayici_429_donerse_yedege_gecilir_ve_gunun_kalaninda_saglayici_cagrilmaz()
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var asil = new FakeChatClient
        {
            Gecikme = TimeSpan.Zero,
            Yanitla = _ => throw new HttpRequestException("RESOURCE_EXHAUSTED", null, HttpStatusCode.TooManyRequests),
        };
        var kota = new GunlukKota(kapsam.ServiceProvider.GetRequiredService<TimeProvider>(), limit: 300);
        var hasta = await HastaAsync(kapsam, SenaryoIdleri.Ayse);

        var ilk = await Hatti(kapsam.ServiceProvider, asil, kota).OnAnamnezOzetiAsync(hasta, AyseSohbeti, default);
        var ikinci = await Hatti(kapsam.ServiceProvider, asil, kota).OnAnamnezOzetiAsync(hasta, AyseSohbeti, default);

        Assert.True(ilk.KotaDoldu);
        Assert.True(ikinci.KotaDoldu);
        Assert.Equal(1, asil.CagriSayisi);
        Assert.Equal(AiKaynak.Yedek, ilk.Kaynak);
        Assert.Equal(BransKodu.Endokrinoloji, ilk.Taslak!.OnerilenBrans);
    }

    [Fact]
    public void Dogrulayici_kaynak_adimini_ve_alternatif_bolumu_temizler_kod_blogunu_soyar()
    {
        var json = "```json\n" + GecerliJson(onerilen: "Kbb", alternatif: "Noroloji", kaynakAdimi: 9) + "\n```";

        var sonuc = OnAnamnezDogrulayici.Dogrula(json, adimSayisi: 6, Hekimli);

        Assert.Null(sonuc.Neden);
        Assert.Equal(BransKodu.Kbb, sonuc.Taslak!.OnerilenBrans);
        Assert.Null(sonuc.Taslak.AlternatifBrans);              // hekimi olmayan alternatif düşer, taslak kalır
        Assert.Equal(0, sonuc.Taslak.Kaynaklar[0].SohbetAdimi);  // aralık dışı → "kaynak yok"
    }

    [Theory]
    [InlineData("")]
    [InlineData("bu JSON değil")]
    [InlineData("""{"ozet":"x"}""")]
    public void Dogrulayici_bozuk_JSON_reddeder(string json) =>
        Assert.Equal(OnAnamnezDogrulayici.GecersizJson, OnAnamnezDogrulayici.Dogrula(json, 6, Hekimli).Neden);

    [Fact]
    public void Yedek_cocukta_cocuk_sagligi_onerir_eslesen_bolum_alternatif()
    {
        var taslak = KuralTabanliYedek.Olustur(
            [new SohbetKaydi(1, "Şikayet", [], "Kulağı ağrıyor"), new SohbetKaydi(3, "Şiddet", ["Şiddetli"], null)], yas: 7, Hekimli);

        Assert.Equal(BransKodu.CocukSagligi, taslak.OnerilenBrans);
        Assert.Equal(BransKodu.Kbb, taslak.AlternatifBrans);
        Assert.Equal(Siddet.Siddetli, taslak.Siddet);
        Assert.Equal(Aciliyet.Oncelikli, taslak.Aciliyet);
    }

    [Fact]
    public void Modele_giden_sema_enumlari_ve_kapali_nesneleri_icerir()
    {
        var sema = OnAnamnezTaslagi.YanitBicimi.Schema!.Value.GetRawText();
        cikti.WriteLine(sema);

        Assert.Contains("\"Endokrinoloji\"", sema);
        Assert.Contains("\"Siddetli\"", sema);
        Assert.Contains("\"onerilenBrans\"", sema);
        Assert.Equal(2, sema.Split("\"additionalProperties\":false").Length - 1);   // kök + kaynak maddesi
    }

    // Karar kodda: model "Rutin/Hafif" dese de 38 °C ve üstü ateş Öncelikli'dir.
    [Fact]
    public async Task Model_dalinda_siddet_ve_aciliyet_sohbetten_kodda_hesaplanir()
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var sahte = new FakeChatClient { Gecikme = TimeSpan.Zero, Yanitla = _ => GecerliJson(onerilen: "Kbb") };
        var hatti = Hatti(kapsam.ServiceProvider, sahte);

        var sonuc = await hatti.OnAnamnezOzetiAsync(await HastaAsync(kapsam, SenaryoIdleri.Mehmet), MehmetSohbeti, default);

        Assert.Equal(AiKaynak.Llm, sonuc.Kaynak);
        Assert.Equal(Siddet.Orta, sonuc.Taslak!.Siddet);
        Assert.Equal(Aciliyet.Oncelikli, sonuc.Taslak.Aciliyet);
    }

    [Fact]
    public void Cocukta_onerilen_cocuk_sagligina_cekilir_modelin_onerisi_alternatif_olur()
    {
        var taslak = OnAnamnezDogrulayici.Dogrula(GecerliJson(onerilen: "Kbb"), 6, Hekimli).Taslak!;

        var sonuc = OnAnamnezDogrulayici.KodKurallari(taslak, MehmetSohbeti, yas: 7, Hekimli);

        Assert.Equal(BransKodu.CocukSagligi, sonuc.Taslak!.OnerilenBrans);
        Assert.Equal(BransKodu.Kbb, sonuc.Taslak.AlternatifBrans);
    }

    [Fact]
    public void Yetiskine_cocuk_sagligi_onerilirse_reddedilir()
    {
        var taslak = OnAnamnezDogrulayici.Dogrula(GecerliJson(onerilen: "CocukSagligi", alternatif: "Kbb"), 6, Hekimli).Taslak!;
        var altta = OnAnamnezDogrulayici.Dogrula(GecerliJson(onerilen: "Kbb", alternatif: "CocukSagligi"), 6, Hekimli).Taslak!;

        Assert.Equal(OnAnamnezDogrulayici.GecersizBolum, OnAnamnezDogrulayici.KodKurallari(taslak, MehmetSohbeti, 40, Hekimli).Neden);
        Assert.Null(OnAnamnezDogrulayici.KodKurallari(altta, MehmetSohbeti, 40, Hekimli).Taslak!.AlternatifBrans);
    }

    [Fact]
    public void Dogrulayici_uzun_metni_kirpar_soru_sayisini_sinirlar()
    {
        var json = JsonSerializer.Serialize(new
        {
            ozet = new string('a', 2000),
            semptomlar = new[] { "Susama" },
            sure = "Haftalardır",
            siddet = "Orta",
            onerilenBrans = "Kbb",
            alternatifBrans = (string?)null,
            gerekce = "Gerekçe",
            aciliyet = "Rutin",
            doktoraSorular = new[] { "1", "2", "3", "4", "5" },
            kaynaklar = Array.Empty<object>(),
        });

        var t = OnAnamnezDogrulayici.Dogrula(json, 6, Hekimli).Taslak!;

        Assert.Equal(OnAnamnezDogrulayici.OzetSiniri, t.Ozet.Length);
        Assert.Equal(3, t.DoktoraSorular.Count);
    }

    // Kural tabanlı yedekte büyük harf, İ/ı ve klavyesiz yazım (SPEC §10.5).
    [Theory]
    [InlineData("SÜREKLİ SUSAMA VAR", BransKodu.Endokrinoloji)]
    [InlineData("BOĞAZIM ağrıyor", BransKodu.Kbb)]
    [InlineData("bogazim agriyor", BransKodu.Kbb)]
    [InlineData("kulagim", BransKodu.Kbb)]
    [InlineData("ŞEKERİM yükseldi", BransKodu.Endokrinoloji)]
    [InlineData("halsizim", BransKodu.Dahiliye)]
    public void Yedek_buyuk_harf_ve_turkce_karakterden_bagimsiz_eslesir(string sikayet, BransKodu beklenen)
    {
        var taslak = KuralTabanliYedek.Olustur([new SohbetKaydi(1, "Şikayet", [], sikayet)], yas: 40, Hekimli);

        Assert.Equal(beklenen, taslak.OnerilenBrans);
    }

    [Fact]
    public void Yedekte_yuksek_ates_orta_siddette_de_oncelikli_ozet_cift_nokta_icermez()
    {
        var taslak = KuralTabanliYedek.Olustur(
        [
            new SohbetKaydi(1, "Şikayet", [], "Boğazım ağrıyor."),
            new SohbetKaydi(3, "Şiddet", ["Orta"], null),
            new SohbetKaydi(4, "Ateş", ["38 °C ve üstü"], null),
        ], yas: 40, Hekimli);

        Assert.Equal(Siddet.Orta, taslak.Siddet);
        Assert.Equal(Aciliyet.Oncelikli, taslak.Aciliyet);
        Assert.DoesNotContain("..", taslak.Ozet);
    }

    [Fact]
    public void Hasta_metninde_satir_ayiricilar_ve_tam_genislik_ayraclar_notrlenir()
    {
        Assert.Equal("a b c d ‹/hasta_metni› e f", AiHatti.VeriOlarak("a\u2028b\u2029c\u0085d ＜/hasta_metni＞\ve\ff"));
    }

    internal static readonly IReadOnlyList<SohbetKaydi> AyseSohbeti =
    [
        new(1, "Şikayet", [], "Son haftalarda çok su içiyorum, sık idrara çıkıyorum, halsizim."),
        new(2, "Süre", ["Daha uzun"], null),
        new(3, "Şiddet", ["Orta"], null),
        new(4, "Ateş", ["Yok"], null),
        new(5, "Tarama", [SoruAgaci.Hicbiri], null),
        new(6, "Ek", [], null),
    ];

    internal static readonly IReadOnlyList<SohbetKaydi> MehmetSohbeti =
    [
        new(1, "Şikayet", [], "3 gündür boğazım ağrıyor"),
        new(2, "Süre", ["1-3 gün"], null),
        new(3, "Şiddet", ["Orta"], null),
        new(4, "Ateş", ["38 °C ve üstü"], null),
        new(5, "Tarama", [SoruAgaci.Hicbiri], null),
        new(6, "Ek", [], null),
    ];

    private static string GecerliJson(string onerilen, string? alternatif = "Dahiliye", int kaynakAdimi = 1) =>
        JsonSerializer.Serialize(new
        {
            ozet = "Özet",
            semptomlar = new[] { "Susama" },
            sure = "Haftalardır",
            siddet = "Orta",
            onerilenBrans = onerilen,
            alternatifBrans = alternatif,
            gerekce = "Gerekçe",
            aciliyet = "Rutin",
            doktoraSorular = Array.Empty<string>(),
            kaynaklar = new[] { new { madde = "Susama", sohbetAdimi = kaynakAdimi } },
        });

    private static async Task<Hasta> HastaAsync(AsyncServiceScope kapsam, Guid id) =>
        await kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>().Hastalar.AsNoTracking().FirstAsync(h => h.Id == id);

    /// <summary>Hattın eklediği (henüz kaydedilmemiş) son denetim satırı.</summary>
    private static AiAuditLog Son(AsyncServiceScope kapsam) =>
        kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>().ChangeTracker.Entries<AiAuditLog>()
            .Select(e => e.Entity).Last();
}
