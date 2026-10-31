using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using KlinikAkis.Domain;
using KlinikAkis.Domain.Varliklar;
using KlinikAkis.Infrastructure.Ai;
using KlinikAkis.Infrastructure.Seed;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlinikAkis.Tests;

/// <summary>
/// ICD-10 önerisi (SPEC §9.10): kapalı liste şemada enum, listede olmayan kod / zaman aşımı yedeğe düşer, hekim kararı
/// tanılara yazılır, 103 paketine kodlar girer, yetki ve kapalı vizit kuralları.
/// </summary>
public sealed partial class TaniOnerisiTestleri(KlinikApiFabrikasi fabrika) : IClassFixture<KlinikApiFabrikasi>
{
    private static readonly EpikrizGirdisi AyseGirdisi = new(
        "P-7F3A", "50-59", Cinsiyet.K, "Endokrinoloji", ["Penisilin"], ["Tip 2 diyabet"], ["Metformin"],
        "Şikayetler: Çok su içme, Sık idrara çıkma, Halsizlik.", null,
        [new EpikrizTahlili("HbA1c", 7.4m, "%", 4.0m, 5.6m, Isaret.Yuksek, 1, "4548-4")]);

    [Fact]
    public void Kapali_liste_benzersiz_DSO_bicimli_ve_semadaki_enum_ile_ayni()
    {
        var kodlar = Icd10Listesi.Tum.Select(k => k.Kod).ToList();
        Assert.Equal(kodlar.Count, kodlar.Distinct(StringComparer.Ordinal).Count());
        Assert.All(Icd10Listesi.Tum, k =>
        {
            Assert.Matches(DsoBicimi(), k.Kod);
            Assert.False(string.IsNullOrWhiteSpace(k.Ad));
        });

        var kod = TaniOnerisiYaniti.Sema.GetProperty("properties").GetProperty("oneriler")
            .GetProperty("items").GetProperty("properties").GetProperty("kod");
        Assert.Equal(kodlar, kod.GetProperty("enum").EnumerateArray().Select(e => e.GetString()!).ToList());
        var oneriler = TaniOnerisiYaniti.Sema.GetProperty("properties").GetProperty("oneriler");
        Assert.Equal(TaniOnerisiYaniti.EnFazlaOneri, oneriler.GetProperty("maxItems").GetInt32());
        Assert.False(TaniOnerisiYaniti.Sema.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public async Task Gecerli_yanit_kaydedilir_denetim_kaydi_maskeli()
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var hatti = AiHattiTestleri.Hatti(kapsam.ServiceProvider, new FakeChatClient { Gecikme = TimeSpan.Zero });
        var ayse = await db.Hastalar.AsNoTracking().FirstAsync(h => h.Id == SenaryoIdleri.Ayse);
        var girdi = AyseGirdisi with { HekimNotu = $"Ayşe'nin ağız kuruluğu var, tel 0532 123 45 67, TC {ayse.TcKimlik}." };

        var sonuc = await hatti.TaniOnerisiAsync(ayse, girdi, default);
        await db.SaveChangesAsync();

        Assert.Equal(AiKaynak.Llm, sonuc.Kaynak);
        Assert.Equal(["E11.9", "R73.9", "R63.1"], sonuc.Oneri.Oneriler.Select(m => m.Kod));
        var kayit = (await DenetimlerAsync(SenaryoIdleri.Ayse)).Single(k => k.MaskeliGirdi.Contains("ağız kuruluğu", StringComparison.Ordinal));
        Assert.Equal(AiHatti.TaniIstemSurumu, kayit.IstemSurumu);
        Assert.True(kayit.Basarili);
        Assert.Contains("[AD] ağız kuruluğu", kayit.MaskeliGirdi, StringComparison.Ordinal);
        foreach (var kimlik in new[] { ayse.Ad, ayse.Soyad, ayse.TcKimlik, ayse.Telefon })
            Assert.DoesNotContain(kimlik, kayit.MaskeliGirdi, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""{"oneriler":[{"kod":"E11.65","gerekce":"Diyabet."}]}""", TaniDogrulayici.ListedeYok)]
    [InlineData("""{"oneriler":[{"kod":"e11.9","gerekce":"Diyabet."}]}""", TaniDogrulayici.ListedeYok)]
    [InlineData("""{"oneriler":[{"kod":"R53","gerekce":"a"},{"kod":"R35","gerekce":"b"},{"kod":"R51","gerekce":"c"},{"kod":"R05","gerekce":"d"}]}""", TaniDogrulayici.MaddeSayisi)]
    [InlineData("""{"oneriler":[{"kod":"R53","gerekce":"a"},{"kod":"R53","gerekce":"b"}]}""", TaniDogrulayici.Tekrarli)]
    [InlineData("""{"oneriler":[{"kod":"R53","gerekce":" "}]}""", EpikrizDogrulayici.BosAlan)]
    [InlineData("""{"oneriler":[{"kod":"R73.9","gerekce":"HbA1c 9,8 bulundu."}]}""", EpikrizDogrulayici.UydurmaSayi)]
    [InlineData("{bozuk", EpikrizDogrulayici.GecersizJson)]
    [InlineData("""{"oneriler":[{"kod":"R73.9","gerekce":"HbA1c 7.4 yüksek."}]}""", TaniDogrulayici.OndalikNokta)]
    public async Task Gecersiz_yanit_yedege_duser_kodlar_yine_listeden(string yanit, string neden)
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var hatti = AiHattiTestleri.Hatti(kapsam.ServiceProvider, new FakeChatClient { Gecikme = TimeSpan.Zero, Yanitla = _ => yanit });
        var ayse = await db.Hastalar.AsNoTracking().FirstAsync(h => h.Id == SenaryoIdleri.Ayse);

        var sonuc = await hatti.TaniOnerisiAsync(ayse, AyseGirdisi, default);
        await db.SaveChangesAsync();

        Assert.Equal(AiKaynak.Yedek, sonuc.Kaynak);
        Assert.Null(sonuc.Model);
        // Yedek kayıttan kodla: kronik hastalık → tahlil işareti → onaylı anamnez; en fazla 3.
        Assert.Equal(["E11.9", "R73.9", "R63.1"], sonuc.Oneri.Oneriler.Select(m => m.Kod));
        Assert.All(sonuc.Oneri.Oneriler, m => Assert.True(Icd10Listesi.Var(m.Kod)));
        Assert.Contains(await DenetimlerAsync(SenaryoIdleri.Ayse), k => k.YedegeDustu && k.Neden == neden);
    }

    [Fact]
    public async Task Zaman_asiminda_yedege_duser()
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var hatti = AiHattiTestleri.Hatti(kapsam.ServiceProvider, new FakeChatClient { Gecikme = TimeSpan.FromSeconds(5) },
            zamanAsimi: TimeSpan.FromMilliseconds(100));
        var zeynep = await db.Hastalar.AsNoTracking().FirstAsync(h => h.Id == SenaryoIdleri.Zeynep);
        var girdi = AyseGirdisi with
        {
            TakmaAd = zeynep.TakmaAd, KronikHastaliklar = [], OnAnamnez = null,
            Tahliller = [new EpikrizTahlili("Hemoglobin", 6.8m, "g/dL", 12.0m, 15.5m, Isaret.KritikDusuk, 1, "718-7")],
        };

        var sonuc = await hatti.TaniOnerisiAsync(zeynep, girdi, default);
        await db.SaveChangesAsync();

        Assert.Equal(AiKaynak.Yedek, sonuc.Kaynak);
        Assert.Equal("D64.9", Assert.Single(sonuc.Oneri.Oneriler).Kod);
        Assert.Contains(await DenetimlerAsync(SenaryoIdleri.Zeynep), k => k.YedegeDustu && k.Neden == "zaman aşımı");
    }

    [Fact]
    public async Task Kota_dolunca_model_cagrilmaz_yedek_ve_kota_nedeni()
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var zaman = kapsam.ServiceProvider.GetRequiredService<TimeProvider>();
        var sahte = new FakeChatClient { Gecikme = TimeSpan.Zero };
        var hatti = AiHattiTestleri.Hatti(kapsam.ServiceProvider, sahte, new GunlukKota(zaman, 0));
        var mehmet = await db.Hastalar.AsNoTracking().FirstAsync(h => h.Id == SenaryoIdleri.Mehmet);

        var sonuc = await hatti.TaniOnerisiAsync(mehmet, AyseGirdisi with { TakmaAd = mehmet.TakmaAd }, default);
        await db.SaveChangesAsync();

        Assert.Equal(0, sahte.CagriSayisi);
        Assert.True(sonuc.KotaDoldu);
        Assert.Equal(AiKaynak.Yedek, sonuc.Kaynak);
        Assert.Contains(await DenetimlerAsync(SenaryoIdleri.Mehmet), k => k.YedegeDustu && k.Neden == AiHatti.KotaNedeni);
    }

    [Fact]
    public async Task Dayanak_yoksa_model_bos_liste_doner_kod_uydurulmaz()
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var hatti = AiHattiTestleri.Hatti(kapsam.ServiceProvider, new FakeChatClient { Gecikme = TimeSpan.Zero });
        var can = await db.Hastalar.AsNoTracking().FirstAsync(h => h.Id == SenaryoIdleri.Can);
        var bos = AyseGirdisi with { TakmaAd = can.TakmaAd, KronikHastaliklar = [], OnAnamnez = null, Tahliller = [] };

        var sonuc = await hatti.TaniOnerisiAsync(can, bos, default);
        await db.SaveChangesAsync();

        Assert.Equal(AiKaynak.Llm, sonuc.Kaynak);
        Assert.Empty(sonuc.Oneri.Oneriler);
        Assert.Equal(0, TaniOnerisiYaniti.Sema.GetProperty("properties").GetProperty("oneriler").GetProperty("minItems").GetInt32());
    }

    [Theory]
    [InlineData("TİP 2 DİYABET", "E11.9")]
    [InlineData("Tip II diyabet", "E11.9")]
    [InlineData("HİPERTANSİYON", "I10")]
    [InlineData("Tip I diyabet", null)]
    [InlineData("Diabetes insipidus", null)]
    [InlineData("Gestasyonel diyabet", null)]
    [InlineData("Pulmoner hipertansiyon", null)]
    [InlineData("Vezikoüreteral reflü", null)]
    public void Yedek_kronik_eslemesi_dar(string hastalik, string? beklenen)
    {
        var girdi = AyseGirdisi with { KronikHastaliklar = [hastalik], OnAnamnez = null, Tahliller = [] };
        var kodlar = TaniYedekSablonu.Olustur(girdi).Oneriler.Select(m => m.Kod).ToList();
        if (beklenen is null) Assert.Empty(kodlar);
        else Assert.Equal([beklenen], kodlar);
    }

    [Theory]
    [InlineData("Ana şikayet: Boğaz ağrısı. Süre: 1-3 gün. Ateş: Yok.", "R07.0")]
    [InlineData("Ateş: 38 ve üstü. Kilo almadım, su içmiyorum.", null)]
    [InlineData("İdrarda yanma var. Kulak çınlaması.", null)]
    [InlineData("SIK İDRARA ÇIKIYORUM, ÇOK SU İÇİYORUM", "R63.1,R35")]
    public void Yedek_anamnezde_olumsuz_ve_yakin_anlamli_ifadeden_kod_uretmez(string anamnez, string? beklenen)
    {
        var girdi = AyseGirdisi with { KronikHastaliklar = [], OnAnamnez = anamnez, Tahliller = [] };
        var kodlar = string.Join(',', TaniYedekSablonu.Olustur(girdi).Oneriler.Select(m => m.Kod));
        Assert.Equal(beklenen ?? "", kodlar);
    }

    [Fact]
    public void Yedek_dayanak_yoksa_bos_tip_1_diyabet_E11_9_olmaz()
    {
        var girdi = AyseGirdisi with { KronikHastaliklar = ["Tip 1 diyabet"], OnAnamnez = null, Tahliller = [] };
        Assert.Empty(TaniYedekSablonu.Olustur(girdi).Oneriler);
    }

    [Fact]
    public async Task Duzenle_onay_tanilara_yazilir_bekleyen_oneri_yeniden_uretilmez_103_paketinde_kodlar()
    {
        var (doktor, muayeneId) = await MuayenedeAsync(SenaryoIdleri.Ayse);

        var ilk = (await Json(await doktor.PostAsync($"/api/muayeneler/{muayeneId}/tani-onerisi", null))).GetProperty("taniOnerisi");
        Assert.Equal("Llm", ilk.GetProperty("kaynak").GetString());
        Assert.Equal("Taslak", ilk.GetProperty("durum").GetString());
        var satir = ilk.GetProperty("oneriler")[0];
        Assert.Equal("E11.9", satir.GetProperty("kod").GetString());
        Assert.Equal("Tip 2 diabetes mellitus, komplikasyonsuz", satir.GetProperty("ad").GetString());

        // Bekleyen öneri varken model yeniden çağrılmaz: aynı öneri döner, denetim kaydı artmaz.
        var kayitSayisi = (await DenetimlerAsync(SenaryoIdleri.Ayse)).Count;
        var tekrar = (await Json(await doktor.PostAsync($"/api/muayeneler/{muayeneId}/tani-onerisi", null))).GetProperty("taniOnerisi");
        Assert.Equal(ilk.GetProperty("id").GetGuid(), tekrar.GetProperty("id").GetGuid());
        Assert.Equal(kayitSayisi, (await DenetimlerAsync(SenaryoIdleri.Ayse)).Count);

        var id = ilk.GetProperty("id").GetGuid();
        var detay = await Json(await doktor.PostAsJsonAsync($"/api/tani-onerisi/{id}/karar",
            new { karar = "duzenle", kodlar = new[] { "E11.9", "R63.1" } }));
        Assert.Equal("Onaylandi", detay.GetProperty("taniOnerisi").GetProperty("durum").GetString());
        Assert.Equal(["E11.9", "R63.1"], detay.GetProperty("tanilar").EnumerateArray().Select(t => t.GetProperty("kod").GetString()));
        var erisim = await Json(await doktor.GetAsync($"/api/hastalar/{SenaryoIdleri.Ayse}/erisim"));
        Assert.Equal("ICD-10 önerisini düzenleyip onayladı (E11.9, R63.1)", erisim[0].GetProperty("eylem").GetString());
        Assert.Equal(HttpStatusCode.Conflict,
            (await doktor.PostAsJsonAsync($"/api/tani-onerisi/{id}/karar", new { karar = "onayla" })).StatusCode);

        (await doktor.PostAsync($"/api/randevular/{detay.GetProperty("randevuId").GetGuid()}/kapat", null)).EnsureSuccessStatusCode();
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var paket = await kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>().OutboxMesajlari.AsNoTracking()
            .Where(o => o.PaketTipi == PaketTipi.Muayene && o.HastaTakmaAdi == "P-7F3A").ToListAsync();
        var payload = JsonDocument.Parse(Assert.Single(paket).PayloadJson).RootElement;
        Assert.Equal(["E11.9", "R63.1"], payload.GetProperty("tanilar").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal(HttpStatusCode.Conflict, (await doktor.PostAsync($"/api/muayeneler/{muayeneId}/tani-onerisi", null)).StatusCode);
    }

    [Fact]
    public async Task Baska_hekim_403_listede_olmayan_kod_400_ret_tanilara_dokunmaz_yenisi_uretilir()
    {
        var (doktor, muayeneId) = await MuayenedeAsync(SenaryoIdleri.Elif);
        var baskaHekim = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimKardiyoloji);
        Assert.Equal(HttpStatusCode.Forbidden, (await baskaHekim.PostAsync($"/api/muayeneler/{muayeneId}/tani-onerisi", null)).StatusCode);

        var id = (await Json(await doktor.PostAsync($"/api/muayeneler/{muayeneId}/tani-onerisi", null)))
            .GetProperty("taniOnerisi").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await baskaHekim.PostAsJsonAsync($"/api/tani-onerisi/{id}/karar", new { karar = "onayla" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await doktor.PostAsJsonAsync($"/api/tani-onerisi/{id}/karar", new { karar = "duzenle", kodlar = new[] { "E11.65" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await doktor.PostAsJsonAsync($"/api/tani-onerisi/{id}/karar", new { karar = "duzenle", kodlar = Array.Empty<string>() })).StatusCode);

        var detay = await Json(await doktor.PostAsJsonAsync($"/api/tani-onerisi/{id}/karar", new { karar = "reddet" }));
        Assert.Equal("Reddedildi", detay.GetProperty("taniOnerisi").GetProperty("durum").GetString());
        Assert.Empty(detay.GetProperty("tanilar").EnumerateArray());

        var yeni = (await Json(await doktor.PostAsync($"/api/muayeneler/{muayeneId}/tani-onerisi", null))).GetProperty("taniOnerisi");
        Assert.NotEqual(id, yeni.GetProperty("id").GetGuid());
        Assert.Equal("Taslak", yeni.GetProperty("durum").GetString());
    }

    [Fact]
    public async Task Icd10_listesi_yalniz_doktora()
    {
        var doktor = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimEndokrinoloji);
        var liste = await Json(await doktor.GetAsync("/api/icd10"));
        Assert.Equal(Icd10Listesi.Tum.Count, liste.GetArrayLength());
        var hasta = await fabrika.GirisYapAsync("hasta", SenaryoIdleri.Ayse);
        Assert.Equal(HttpStatusCode.Forbidden, (await hasta.GetAsync("/api/icd10")).StatusCode);
    }

    /// <summary>Şikayet sohbetsiz randevu → kabul → çağır → başla (Endokrinoloji hekimi).</summary>
    private async Task<(HttpClient Doktor, Guid MuayeneId)> MuayenedeAsync(Guid hastaId)
    {
        var hasta = await fabrika.GirisYapAsync("hasta", hastaId);
        var randevuId = (await Json(await hasta.PostAsJsonAsync("/api/randevular", new { slotId = await BosSlotAsync() })))
            .GetProperty("id").GetGuid();
        var kabul = await fabrika.GirisYapAsync("kabul");
        (await kabul.PostAsync($"/api/randevular/{randevuId}/kabul", null)).EnsureSuccessStatusCode();
        var doktor = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimEndokrinoloji);
        (await doktor.PostAsync($"/api/randevular/{randevuId}/cagir", null)).EnsureSuccessStatusCode();
        (await doktor.PostAsync($"/api/randevular/{randevuId}/basla", null)).EnsureSuccessStatusCode();
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        return (doktor, await db.Muayeneler.Where(m => m.RandevuId == randevuId).Select(m => m.Id).SingleAsync());
    }

    private async Task<Guid> BosSlotAsync()
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var simdi = KlinikApiFabrikasi.Simdi.UtcDateTime;
        return await db.Slotlar.Where(s => s.HekimId == SenaryoIdleri.HekimEndokrinoloji && !s.Dolu && s.Baslangic > simdi)
            .OrderBy(s => s.Baslangic).Select(s => s.Id).FirstAsync();
    }

    private async Task<List<AiAuditLog>> DenetimlerAsync(Guid hastaId)
    {
        await using var kapsam = fabrika.Services.CreateAsyncScope();
        return await kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>().AiAuditLoglari.AsNoTracking()
            .Where(a => a.HastaId == hastaId && a.Ozellik == AiOzellik.TaniOnerisi).ToListAsync();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage yanit) =>
        JsonDocument.Parse(await yanit.Content.ReadAsStringAsync()).RootElement;

    [GeneratedRegex(@"^[A-Z]\d{2}(\.\d)?$")]
    private static partial Regex DsoBicimi();
}
