using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlinikAkis.Domain;
using KlinikAkis.Infrastructure.Seed;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlinikAkis.Tests;

/// <summary>
/// SPEC §11 test 8 ve §10.3: hasta yalnızca yayınlanmış sonucu ve hekimin yayınladığı metni görür; AI taslağı
/// hastaya hiç gitmez; başka hastanın verisi 403. Kritik değer bilgilendirme seçimi olmadan yayınlanmaz.
/// Her test farklı hasta/sonuç kullanır (sınıf içi testler aynı veritabanını paylaşır).
/// </summary>
public sealed class TahlilTestleri(KlinikApiFabrikasi fabrika) : IClassFixture<KlinikApiFabrikasi>
{
    [Fact]
    public async Task Yayinlanmamis_sonuc_ve_AI_taslagi_hastaya_gorunmez_yalnizca_hekimin_yayinladigi_metin_gorunur()
    {
        var hasta = await fabrika.GirisYapAsync("hasta", SenaryoIdleri.Ayse);
        var doktor = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimEndokrinoloji);
        var hba1c = await SonSonucAsync(doktor, SenaryoIdleri.Ayse, "4548-4");

        // Yayından önce: bu sabahki 7,4 yok; önceki vizitlerde yayınlanmış 6,6 → 7,0 görünür.
        var once = await SonuclarAsync(hasta);
        var oncekiHba1c = Test(once, "4548-4");
        Assert.Equal(7.0m, oncekiHba1c.GetProperty("deger").GetDecimal());
        Assert.Equal([6.6m, 7.0m], Trend(oncekiHba1c));
        Assert.False(Test(once, "1558-6").ValueKind == JsonValueKind.Object, "Bu sabahki açlık glukozu yayınlanmadı.");

        // AI taslağı üretildi ama yayınlanmadı: hasta ucunda ne değer ne taslak metni var.
        var taslak = await doktor.PostAsync($"/api/tahliller/{hba1c}/aciklama", null);
        taslak.EnsureSuccessStatusCode();
        var taslakMetni = JsonDocument.Parse(await taslak.Content.ReadAsStringAsync()).RootElement.GetProperty("taslak").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(taslakMetni));
        var hamAra = await hasta.GetStringAsync("/api/hasta/me/sonuclar");
        Assert.DoesNotContain(taslakMetni, hamAra, StringComparison.Ordinal);
        Assert.Equal(7.0m, Test(await SonuclarAsync(hasta), "4548-4").GetProperty("deger").GetDecimal());

        // Hekim düzenleyip yayınlar: hasta hekimin metnini görür, taslağı değil.
        const string hekimMetni = "Şeker ortalamanız hedefin biraz üstünde ve son aylarda artıyor. Kontrolde birlikte konuşalım.";
        (await doktor.PostAsJsonAsync("/api/tahliller/yayinla", new { sonuclar = new[] { new { sonucId = hba1c, aciklama = hekimMetni } } }))
            .EnsureSuccessStatusCode();

        var hamSon = await hasta.GetStringAsync("/api/hasta/me/sonuclar");
        Assert.DoesNotContain(taslakMetni, hamSon, StringComparison.Ordinal);
        var son = Test(JsonDocument.Parse(hamSon).RootElement, "4548-4");
        Assert.Equal(7.4m, son.GetProperty("deger").GetDecimal());
        Assert.Equal("Yuksek", son.GetProperty("isaret").GetString());
        Assert.Equal(hekimMetni, son.GetProperty("aciklama").GetString());
        Assert.Equal([6.6m, 7.0m, 7.4m], Trend(son));
        Assert.False(son.TryGetProperty("taslak", out _));

        // Yayınlanmış sonuca yeniden yayın ve yeni taslak yok.
        Assert.Equal(HttpStatusCode.Conflict, (await doktor.PostAsJsonAsync("/api/tahliller/yayinla",
            new { sonuclar = new[] { new { sonucId = hba1c, aciklama = "tekrar" } } })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await doktor.PostAsync($"/api/tahliller/{hba1c}/aciklama", null)).StatusCode);
    }

    [Fact]
    public async Task Hasta_baska_hastanin_verisine_ve_hekim_uclarina_erisemez_403()
    {
        var doktor = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimDahiliye1);
        var zeynepHb = await SonSonucAsync(doktor, SenaryoIdleri.Zeynep, "718-7");
        var ayse = await fabrika.GirisYapAsync("hasta", SenaryoIdleri.Ayse);

        Assert.Equal(HttpStatusCode.Forbidden, (await ayse.GetAsync($"/api/hastalar/{SenaryoIdleri.Zeynep}/tahliller")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ayse.PostAsync($"/api/tahliller/{zeynepHb}/aciklama", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ayse.PostAsJsonAsync("/api/tahliller/yayinla",
            new { sonuclar = new[] { new { sonucId = zeynepHb, aciklama = "x" } }, bilgilendirme = "Bilgilendirilecek" })).StatusCode);

        // Kendi ucu kendi verisi: Ayşe'nin listesinde Zeynep'in testleri (ferritin) hiç yok.
        Assert.Equal(JsonValueKind.Undefined, Test(await SonuclarAsync(ayse), "2276-4").ValueKind);
        // Kimliksiz istek de reddedilir.
        Assert.Equal(HttpStatusCode.Unauthorized, (await fabrika.CreateClient().GetAsync("/api/hasta/me/sonuclar")).StatusCode);
    }

    [Fact]
    public async Task Kritik_deger_bilgilendirme_secimi_olmadan_yayinlanmaz_secim_erisim_kaydina_ve_105_outbox_yazilir()
    {
        var doktor = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimDahiliye1);
        var hb = await SonSonucAsync(doktor, SenaryoIdleri.Zeynep, "718-7");
        var istek = new { sonucId = hb, aciklama = "Kan değeriniz çok düşük. Doktorunuz sizinle iletişime geçecek." };

        var secimsiz = await doktor.PostAsJsonAsync("/api/tahliller/yayinla", new { sonuclar = new[] { istek } });
        Assert.Equal(HttpStatusCode.BadRequest, secimsiz.StatusCode);
        Assert.Contains("bilgilendirme", await secimsiz.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        (await doktor.PostAsJsonAsync("/api/tahliller/yayinla",
            new { sonuclar = new[] { istek }, bilgilendirme = "TelefonlaBilgilendirildi" })).EnsureSuccessStatusCode();

        var erisim = await doktor.GetStringAsync($"/api/hastalar/{SenaryoIdleri.Zeynep}/erisim");
        Assert.Contains("Kritik değer (Hemoglobin ‼ KD): hasta telefonla bilgilendirildi", erisim, StringComparison.Ordinal);
        Assert.Contains("Tahlil sonucunu hastaya yayınladı: Hemoglobin", erisim, StringComparison.Ordinal);

        await using var kapsam = fabrika.Services.CreateAsyncScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KlinikDbContext>();
        var takmaAd = await db.Hastalar.Where(h => h.Id == SenaryoIdleri.Zeynep).Select(h => h.TakmaAd).FirstAsync();
        var paket = await db.OutboxMesajlari.AsNoTracking().SingleAsync(o => o.HastaTakmaAdi == takmaAd);
        Assert.Equal(PaketTipi.LabSonuc, paket.PaketTipi);
        Assert.Equal(OutboxDurumu.Bekliyor, paket.Durum);
        Assert.Contains("718-7", paket.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Zeynep", paket.PayloadJson, StringComparison.Ordinal);

        var zeynep = await fabrika.GirisYapAsync("hasta", SenaryoIdleri.Zeynep);
        Assert.Equal("KritikDusuk", Test(await SonuclarAsync(zeynep), "718-7").GetProperty("isaret").GetString());
    }

    [Fact]
    public async Task Bos_aciklama_ve_iki_hastanin_sonucu_birlikte_yayinlanmaz()
    {
        var doktor = await fabrika.GirisYapAsync("doktor", SenaryoIdleri.HekimEndokrinoloji);
        var elifTsh = await SonSonucAsync(doktor, SenaryoIdleri.Elif, "3016-3");
        var zeynepFerritin = await SonSonucAsync(doktor, SenaryoIdleri.Zeynep, "2276-4");

        Assert.Equal(HttpStatusCode.BadRequest, (await doktor.PostAsJsonAsync("/api/tahliller/yayinla",
            new { sonuclar = new[] { new { sonucId = elifTsh, aciklama = "  " } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await doktor.PostAsJsonAsync("/api/tahliller/yayinla",
            new { sonuclar = new[] { new { sonucId = elifTsh, aciklama = "a" }, new { sonucId = zeynepFerritin, aciklama = "b" } } })).StatusCode);
    }

    private static async Task<Guid> SonSonucAsync(HttpClient doktor, Guid hastaId, string loinc)
    {
        var satirlar = JsonDocument.Parse(await doktor.GetStringAsync($"/api/hastalar/{hastaId}/tahliller")).RootElement;
        return satirlar.EnumerateArray().Single(s => s.GetProperty("loincKodu").GetString() == loinc)
            .GetProperty("son").GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> SonuclarAsync(HttpClient hasta) =>
        JsonDocument.Parse(await hasta.GetStringAsync("/api/hasta/me/sonuclar")).RootElement;

    /// <summary>Teste ait satır; yoksa Undefined.</summary>
    private static JsonElement Test(JsonElement liste, string loinc) =>
        liste.EnumerateArray().FirstOrDefault(s => s.GetProperty("loincKodu").GetString() == loinc);

    private static decimal[] Trend(JsonElement satir) =>
        [.. satir.GetProperty("trend").EnumerateArray().Select(t => t.GetProperty("deger").GetDecimal())];
}
