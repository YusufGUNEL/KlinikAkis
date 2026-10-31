using KlinikAkis.Domain;
using KlinikAkis.Domain.Varliklar;
using KlinikAkis.Infrastructure.Ai;
using KlinikAkis.Infrastructure.Seed;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlinikAkis.Tests;

/// <summary>
/// Tahlil açıklaması AiHatti dalları (SPEC §9.6): modele kimlik gitmez, işaret koddan gelir; 600 karakteri aşan ya da
/// işaretle çelişen yanıt reddedilir, şablon yedeğe düşülür; her dal denetim kaydı yazar.
/// </summary>
public sealed class TahlilAciklamaTestleri(KlinikApiFabrikasi fabrika) : IClassFixture<KlinikApiFabrikasi>
{
    private static readonly TahlilGirdisi AyseHba1c =
        new("HbA1c", "Şeker ortalaması", "%", 4.0m, 5.6m, Isaret.Yuksek, 7.4m, [6.6m, 7.0m, 7.4m], 1);

    [Fact]
    public async Task Modele_yalnizca_test_bilgisi_gider_kimlik_gitmez_denetime_TahlilAciklama_yazilir()
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var hatti = AiHattiTestleri.Hatti(kapsam.ServiceProvider, new FakeChatClient { Gecikme = TimeSpan.Zero });
        var ayse = await kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>().Hastalar.AsNoTracking()
            .FirstAsync(h => h.Id == SenaryoIdleri.Ayse);

        var sonuc = await hatti.TahlilAciklamasiAsync(ayse.Id, AyseHba1c, default);

        Assert.Equal(AiKaynak.Llm, sonuc.Kaynak);
        Assert.Contains("üstünde", sonuc.Aciklama, StringComparison.Ordinal);
        var kayit = Son(kapsam);
        Assert.Equal(AiOzellik.TahlilAciklama, kayit.Ozellik);
        Assert.Equal(AiHatti.TahlilIstemSurumu, kayit.IstemSurumu);
        Assert.Equal(ayse.Id, kayit.HastaId);   // panel süzgeci; modele gitmez
        Assert.Contains("sonuç: 7,4 %", kayit.MaskeliGirdi, StringComparison.Ordinal);
        Assert.Contains("6,6 → 7,0 → 7,4", kayit.MaskeliGirdi, StringComparison.Ordinal);
        Assert.Contains("referans aralığının üstünde", kayit.MaskeliGirdi, StringComparison.Ordinal);
        foreach (var kimlik in new[] { ayse.Ad, ayse.Soyad, ayse.TcKimlik, ayse.TakmaAd, ayse.Telefon })
            Assert.DoesNotContain(kimlik, kayit.MaskeliGirdi, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("uzun", TahlilAciklamaDogrulayici.Uzun)]
    [InlineData("Değeriniz normal aralıkta, endişe edecek bir şey yok.", TahlilAciklamaDogrulayici.CelisenYon)]
    [InlineData("Şeker ortalamanız düşük çıktı.", TahlilAciklamaDogrulayici.CelisenYon)]
    [InlineData("Sonucunuz normal, endişelenecek bir şey yok.", TahlilAciklamaDogrulayici.CelisenYon)]
    [InlineData("Şeker ortalamanız 4,7; referans aralığının üstünde.", TahlilAciklamaDogrulayici.UydurmaSayi)]
    [InlineData("{bozuk", TahlilAciklamaDogrulayici.GecersizJson)]
    public async Task Gecersiz_yanit_reddedilir_isarete_uygun_sablon_yedege_dusulur(string aciklama, string neden)
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var metin = aciklama == "uzun" ? new string('a', TahlilAciklamaDogrulayici.Sinir + 1) : aciklama;
        var yanit = aciklama.StartsWith('{') ? aciklama : System.Text.Json.JsonSerializer.Serialize(new { aciklama = metin });
        var hatti = AiHattiTestleri.Hatti(kapsam.ServiceProvider, new FakeChatClient { Gecikme = TimeSpan.Zero, Yanitla = _ => yanit });

        var sonuc = await hatti.TahlilAciklamasiAsync(SenaryoIdleri.Ayse, AyseHba1c, default);

        Assert.Equal(AiKaynak.Yedek, sonuc.Kaynak);
        Assert.Null(sonuc.Model);
        Assert.Equal(
            "Şeker ortalaması sonucunuz 7,4 %; bu değer referans aralığının üstünde (4,0–5,6 %). " +
            "Önceki ölçümlere göre artıyor. Bu sonucu doktorunuzla konuşun.", sonuc.Aciklama);
        var kayit = Son(kapsam);
        Assert.True(kayit.YedegeDustu);
        Assert.False(kayit.Basarili);
        Assert.Equal(neden, kayit.Neden);
        Assert.Equal(yanit, kayit.CiktiJson);   // modelin ham çıktısı denetimde kalır
    }

    [Fact]
    public async Task Zaman_asiminda_yedege_duser_kritikte_iletisim_cumlesi_vardir()
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var hatti = AiHattiTestleri.Hatti(kapsam.ServiceProvider, new FakeChatClient { Gecikme = TimeSpan.FromSeconds(5) },
            zamanAsimi: TimeSpan.FromMilliseconds(100));
        var hb = new TahlilGirdisi("Hemoglobin", "Kan değeri", "g/dL", 12.0m, 15.5m, Isaret.KritikDusuk, 6.8m, [6.8m], 1);

        var sonuc = await hatti.TahlilAciklamasiAsync(SenaryoIdleri.Zeynep, hb, default);

        Assert.Equal(AiKaynak.Yedek, sonuc.Kaynak);
        Assert.Contains("çok altında", sonuc.Aciklama, StringComparison.Ordinal);
        Assert.EndsWith("Doktorunuz sizinle iletişime geçecek.", sonuc.Aciklama, StringComparison.Ordinal);
        Assert.Equal("zaman aşımı", Son(kapsam).Neden);
    }

    [Fact]
    public async Task Gunluk_kota_dolunca_model_cagrilmaz_rozetli_yedek()
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var sahte = new FakeChatClient { Gecikme = TimeSpan.Zero };
        var kota = new GunlukKota(kapsam.ServiceProvider.GetRequiredService<TimeProvider>(), limit: 0);
        var hatti = AiHattiTestleri.Hatti(kapsam.ServiceProvider, sahte, kota);

        var sonuc = await hatti.TahlilAciklamasiAsync(SenaryoIdleri.Ayse, AyseHba1c, default);

        Assert.Equal(0, sahte.CagriSayisi);
        Assert.True(sonuc.KotaDoldu);
        Assert.Equal(AiHatti.KotaNedeni, Son(kapsam).Neden);
    }

    [Theory]
    [InlineData("Değeriniz referans aralığının üstünde ve artıyor.", Isaret.Yuksek, false)]
    [InlineData("Değeriniz normal aralığın üstündedir.", Isaret.Yuksek, false)]
    [InlineData("NORMAL ARALIKTA", Isaret.Yuksek, true)]
    [InlineData("Sonucunuz normal.", Isaret.Yuksek, true)]
    [InlineData("DEĞERİNİZ DÜŞÜK", Isaret.Yuksek, true)]
    [InlineData("Sonucunuz normal sınırlarda.", Isaret.KritikYuksek, true)]
    [InlineData("Değeriniz aralığın altında; son ölçümlerde yükseliyor.", Isaret.Dusuk, false)]
    [InlineData("Değeriniz yüksek.", Isaret.Dusuk, true)]
    [InlineData("Değeriniz referans aralığında.", Isaret.Normal, false)]
    [InlineData("Biraz yuksek ama sorun yok.", Isaret.Normal, true)]   // Türkçe klavyesiz yazım da yakalanır
    public void Yon_celiskisi_Turkce_katlanmis_metinde_aranir(string metin, Isaret isaret, bool celisiyor) =>
        Assert.Equal(celisiyor, TahlilAciklamaDogrulayici.Celisiyor(metin, isaret));

    [Theory]
    [InlineData("Şeker ortalaması (HbA1c) sonucunuz 7,4 %; aralık 4,0–5,6 %. Son 3 ölçüm: 6,6 → 7,0 → 7,4.", true)]
    [InlineData("Sonucunuz 7.4, önceki 6,6.", true)]
    [InlineData("Sonucunuz 8,1 %.", false)]
    [InlineData("Son 12 ayda artıyor.", false)]
    public void Metindeki_sayilar_girdide_olmali(string metin, bool gecerli) =>
        Assert.Equal(gecerli, TahlilAciklamaDogrulayici.SayilarGirdide(metin, AyseHba1c));

    [Fact]
    public void Tek_olcumde_trend_anlatan_yanit_reddedilir()
    {
        var glukoz = new TahlilGirdisi("Glukoz (açlık)", "Açlık kan şekeri", "mg/dL", 70m, 100m, Isaret.Yuksek, 128m, [128m], 0);

        Assert.Equal(TahlilAciklamaDogrulayici.OlmayanTrend, TahlilAciklamaDogrulayici.Dogrula(
            """{"aciklama":"Açlık kan şekeriniz 128; önceki ölçümünle aynı seviyede kalmış."}""", glukoz).Neden);
        Assert.Null(TahlilAciklamaDogrulayici.Dogrula(
            """{"aciklama":"Açlık kan şekeriniz 128 mg/dL, referans aralığının üstünde."}""", glukoz).Neden);
    }

    [Fact]
    public void Kritikte_iletisim_cumlesi_yoksa_kod_ekler()
    {
        var hb = new TahlilGirdisi("Hemoglobin", "Kan değeri", "g/dL", 12.0m, 15.5m, Isaret.KritikDusuk, 6.8m, [6.8m], 1);
        var (aciklama, neden) = TahlilAciklamaDogrulayici.Dogrula("""{"aciklama":"Kan değeriniz 6,8 ile çok düşük."}""", hb);

        Assert.Null(neden);
        Assert.Equal("Kan değeriniz 6,8 ile çok düşük. " + TahlilAciklamaDogrulayici.IletisimCumlesi, aciklama);
    }

    [Fact]
    public async Task Saglayici_429_kota_rozetli_yedek_ve_denetimde_neden()
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var sahte = new FakeChatClient
        {
            Gecikme = TimeSpan.Zero,
            Yanitla = _ => throw new HttpRequestException("RESOURCE_EXHAUSTED", null, System.Net.HttpStatusCode.TooManyRequests),
        };
        var hatti = AiHattiTestleri.Hatti(kapsam.ServiceProvider, sahte);

        var sonuc = await hatti.TahlilAciklamasiAsync(SenaryoIdleri.Ayse, AyseHba1c, default);

        Assert.Equal(AiKaynak.Yedek, sonuc.Kaynak);
        Assert.True(sonuc.KotaDoldu);
        var kayit = Son(kapsam);
        Assert.Equal(AiOzellik.TahlilAciklama, kayit.Ozellik);
        Assert.Contains("429", kayit.Neden, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Istemci_iptalinde_denetim_kaydi_saklanir_ve_iptal_yukari_tasinir()
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var hatti = AiHattiTestleri.Hatti(kapsam.ServiceProvider, new FakeChatClient { Gecikme = TimeSpan.FromSeconds(5) });
        using var iptal = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hatti.TahlilAciklamasiAsync(SenaryoIdleri.Ayse, AyseHba1c, iptal.Token));

        await using var yeni = fabrika.Services.CreateAsyncScope();
        var kayit = await yeni.ServiceProvider.GetRequiredService<KlinikDbContext>().AiAuditLoglari.AsNoTracking()
            .Where(k => k.Ozellik == AiOzellik.TahlilAciklama && k.Neden == "istemci iptal etti")
            .FirstOrDefaultAsync();
        Assert.NotNull(kayit);
    }

    private static AiAuditLog Son(AsyncServiceScope kapsam) =>
        kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>().ChangeTracker.Entries<AiAuditLog>()
            .Select(e => e.Entity).Last();
}
