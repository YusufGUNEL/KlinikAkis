using Bogus;
using KlinikAkis.Domain;
using KlinikAkis.Domain.Varliklar;
using KlinikAkis.Infrastructure.Veri;

namespace KlinikAkis.Infrastructure.Seed;

/// <summary>
/// Demo verisi (SPEC §6). Tekrarlanabilir: senaryo varlıkları sabit Id'li ve elle yazılmış, dolgu veriler
/// sabit Bogus seed'iyle, diğer Id'ler <see cref="DeterministikGuid"/> ile. Saate bağlı alanlar (slotlar,
/// tahlil zamanları) verilen "şimdi"ye göre üretilir; aynı "şimdi" ile her çalıştırma aynı veriyi verir.
/// </summary>
public static class DemoVeriTohumu
{
    private const int BogusSeed = 20261103;
    private const int DolguHastaSayisi = 40;
    private const int SlotSaati = 8;
    private const int SlotDk = 10;

    public static SeedSayimlari Yukle(KlinikDbContext db, DateTime utcSimdi)
    {
        var branslar = Branslar();
        var hekimler = Hekimler(branslar);
        var hastalar = SenaryoHastalari(utcSimdi);
        hastalar.AddRange(DolguHastalar(utcSimdi));
        TakmaAdlariAta(hastalar);
        var tanimlar = TahlilTanimlari();
        var sonuclar = SenaryoTahlilleri(tanimlar, hastalar, utcSimdi);
        var slotlar = Slotlar(hekimler, utcSimdi);
        var randevular = DolguRandevular(slotlar, hekimler, hastalar);

        db.Branslar.AddRange(branslar);
        db.Hekimler.AddRange(hekimler);
        db.Hastalar.AddRange(hastalar);
        db.TahlilTanimlari.AddRange(tanimlar);
        db.TahlilSonuclari.AddRange(sonuclar);
        db.Slotlar.AddRange(slotlar);
        db.Randevular.AddRange(randevular);

        return new SeedSayimlari(branslar.Count, hekimler.Count, hastalar.Count, slotlar.Count,
            randevular.Count, sonuclar.Count);
    }

    private static List<Brans> Branslar()
    {
        (BransKodu Kod, string Ad)[] liste =
        [
            (BransKodu.Dahiliye, "Dahiliye"), (BransKodu.Kbb, "Kulak Burun Boğaz Hastalıkları"),
            (BransKodu.Kardiyoloji, "Kardiyoloji"), (BransKodu.Noroloji, "Nöroloji"),
            (BransKodu.Goz, "Göz Hastalıkları"), (BransKodu.Ortopedi, "Ortopedi ve Travmatoloji"),
            (BransKodu.CocukSagligi, "Çocuk Sağlığı ve Hastalıkları"), (BransKodu.KadinDogum, "Kadın Hastalıkları ve Doğum"),
            (BransKodu.Dermatoloji, "Dermatoloji"), (BransKodu.Uroloji, "Üroloji"),
            (BransKodu.GogusHastaliklari, "Göğüs Hastalıkları"), (BransKodu.Endokrinoloji, "Endokrinoloji"),
            (BransKodu.GenelCerrahi, "Genel Cerrahi"), (BransKodu.Psikiyatri, "Psikiyatri"),
            (BransKodu.Ftr, "Fiziksel Tıp ve Rehabilitasyon"), (BransKodu.Gastroenteroloji, "Gastroenteroloji"),
            (BransKodu.Nefroloji, "Nefroloji"), (BransKodu.Romatoloji, "Romatoloji"),
            (BransKodu.Hematoloji, "Hematoloji"), (BransKodu.Onkoloji, "Tıbbi Onkoloji"),
            (BransKodu.EnfeksiyonHastaliklari, "Enfeksiyon Hastalıkları"), (BransKodu.BeyinCerrahisi, "Beyin ve Sinir Cerrahisi"),
            (BransKodu.KalpDamarCerrahisi, "Kalp ve Damar Cerrahisi"), (BransKodu.PlastikCerrahi, "Plastik Cerrahi"),
            (BransKodu.CocukCerrahisi, "Çocuk Cerrahisi"), (BransKodu.CocukPsikiyatrisi, "Çocuk ve Ergen Ruh Sağlığı ve Hastalıkları"),
            (BransKodu.Algoloji, "Algoloji"), (BransKodu.Geriatri, "Geriatri"),
            (BransKodu.AileHekimligi, "Aile Hekimliği"), (BransKodu.AgizDisSagligi, "Ağız ve Diş Sağlığı"),
        ];
        return [.. liste.Select(b => new Brans { Id = BransId(b.Kod), Kod = b.Kod, Ad = b.Ad })];
    }

    public static Guid BransId(BransKodu kod) => DeterministikGuid.Uret("brans", kod);

    private static List<Hekim> Hekimler(List<Brans> branslar) =>
    [
        Hekim(SenaryoIdleri.HekimEndokrinoloji, "Selin Arslan", BransKodu.Endokrinoloji, "3"),
        Hekim(SenaryoIdleri.HekimDahiliye1, "Murat Demir", BransKodu.Dahiliye, "5"),
        Hekim(SenaryoIdleri.HekimDahiliye2, "Gülay Şahin", BransKodu.Dahiliye, "6"),
        Hekim(SenaryoIdleri.HekimKbb, "Kerem Aydın", BransKodu.Kbb, "8"),
        Hekim(SenaryoIdleri.HekimCocukSagligi, "Deniz Yılmaz", BransKodu.CocukSagligi, "12"),
        Hekim(SenaryoIdleri.HekimKardiyoloji, "Okan Çelik", BransKodu.Kardiyoloji, "2"),
    ];

    private static Hekim Hekim(Guid id, string ad, BransKodu brans, string oda) =>
        new() { Id = id, Ad = ad, Unvan = "Uzm. Dr.", BransId = BransId(brans), Oda = oda };

    private static List<Hasta> SenaryoHastalari(DateTime utcSimdi)
    {
        var rizaZamani = IstanbulSaati.Utc(IstanbulSaati.Bugun(utcSimdi).AddDays(-180), new TimeOnly(10, 0));
        return
        [
            new()
            {
                Id = SenaryoIdleri.Ayse, TcKimlik = TcKimlik.SaglamaEkle("991000001"),
                Ad = "Ayşe", Soyad = "Kaya", DogumTarihi = new DateOnly(1972, 3, 14), Cinsiyet = Cinsiyet.K,
                Telefon = "+90 532 555 01 01", Eposta = "ayse.kaya@ornek.test",
                Alerjiler = ["Penisilin"], KronikHastaliklar = ["Tip 2 diyabet"],
                KullandigiIlaclar = ["Metformin 1000 mg 2x1"],
                AcikRizaAlindi = true, AcikRizaZamani = rizaZamani, TakmaAd = "P-7F3A",
            },
            new()
            {
                Id = SenaryoIdleri.Mehmet, TcKimlik = TcKimlik.SaglamaEkle("991000002"),
                Ad = "Mehmet", Soyad = "Tekin", DogumTarihi = new DateOnly(1988, 6, 2), Cinsiyet = Cinsiyet.E,
                Telefon = "+90 533 555 02 02", Eposta = "mehmet.tekin@ornek.test",
                AcikRizaAlindi = true, AcikRizaZamani = rizaZamani,
            },
            new()
            {
                Id = SenaryoIdleri.Zeynep, TcKimlik = TcKimlik.SaglamaEkle("991000003"),
                Ad = "Zeynep", Soyad = "Aksoy", DogumTarihi = new DateOnly(1997, 1, 20), Cinsiyet = Cinsiyet.K,
                Telefon = "+90 535 555 03 03", Eposta = "zeynep.aksoy@ornek.test",
                AcikRizaAlindi = true, AcikRizaZamani = rizaZamani,
            },
            new()
            {
                Id = SenaryoIdleri.Hasan, TcKimlik = TcKimlik.SaglamaEkle("991000004"),
                Ad = "Hasan", Soyad = "Yıldız", DogumTarihi = new DateOnly(1965, 8, 11), Cinsiyet = Cinsiyet.E,
                Telefon = "+90 536 555 04 04", Eposta = "hasan.yildiz@ornek.test",
                KronikHastaliklar = ["Hipertansiyon"], KullandigiIlaclar = ["Amlodipin 5 mg 1x1"],
                AcikRizaAlindi = true, AcikRizaZamani = rizaZamani,
            },
            new()
            {
                Id = SenaryoIdleri.Elif, TcKimlik = TcKimlik.SaglamaEkle("991000005"),
                Ad = "Elif", Soyad = "Sarı", DogumTarihi = new DateOnly(1981, 4, 5), Cinsiyet = Cinsiyet.K,
                Telefon = "+90 537 555 05 05", Eposta = "elif.sari@ornek.test",
                AcikRizaAlindi = true, AcikRizaZamani = rizaZamani,
            },
            new()
            {
                Id = SenaryoIdleri.Can, TcKimlik = TcKimlik.SaglamaEkle("991000006"),
                Ad = "Can", Soyad = "Doğan", DogumTarihi = new DateOnly(2019, 5, 17), Cinsiyet = Cinsiyet.E,
                // Çocuk hasta: iletişim bilgisi veliye aittir (veli adına başvuru, SPEC §6).
                Telefon = "+90 538 555 06 06", Eposta = "veli.dogan@ornek.test",
                AcikRizaAlindi = true, AcikRizaZamani = rizaZamani,
            },
        ];
    }

    private static List<Hasta> DolguHastalar(DateTime utcSimdi)
    {
        var f = new Faker("tr") { Random = new Randomizer(BogusSeed) };
        string[] alerjiHavuzu = ["Penisilin", "Aspirin", "Sülfonamid", "Lateks", "Polen"];
        string[] kronikHavuzu = ["Hipertansiyon", "Astım", "Hipotiroidi", "Tip 2 diyabet", "KOAH"];
        var enErken = new DateOnly(1945, 1, 1).DayNumber;
        var enGec = new DateOnly(2008, 12, 31).DayNumber;

        var liste = new List<Hasta>(DolguHastaSayisi);
        for (var i = 1; i <= DolguHastaSayisi; i++)
        {
            var cinsiyet = f.Random.Bool() ? Cinsiyet.K : Cinsiyet.E;
            var ad = f.Name.FirstName(cinsiyet == Cinsiyet.K ? Bogus.DataSets.Name.Gender.Female : Bogus.DataSets.Name.Gender.Male);
            liste.Add(new Hasta
            {
                Id = DeterministikGuid.Uret("dolgu-hasta", i),
                TcKimlik = TcKimlik.SaglamaEkle($"99{2000000 + i}"),
                Ad = ad,
                Soyad = f.Name.LastName(),
                DogumTarihi = DateOnly.FromDayNumber(f.Random.Int(enErken, enGec)),
                Cinsiyet = cinsiyet,
                Telefon = $"+90 5{f.Random.Int(30, 59)} 555 {f.Random.Int(10, 99)} {f.Random.Int(10, 99)}",
                Eposta = $"hasta{i:D2}@ornek.test",
                Alerjiler = f.Random.Bool(0.15f) ? [f.PickRandom(alerjiHavuzu)] : [],
                KronikHastaliklar = f.Random.Bool(0.3f) ? [f.PickRandom(kronikHavuzu)] : [],
                AcikRizaAlindi = true, AcikRizaZamani = IstanbulSaati.Utc(IstanbulSaati.Bugun(utcSimdi).AddDays(-f.Random.Int(1, 720)), new TimeOnly(9, 0)),
            });
        }
        return liste;
    }

    /// <summary>Takma ad (P-XXXX) kayıtta sabittir; çakışırsa tuz artırılarak deterministik olarak çözülür.</summary>
    private static void TakmaAdlariAta(List<Hasta> hastalar)
    {
        var kullanilan = hastalar.Where(h => h.TakmaAd != "").Select(h => h.TakmaAd).ToHashSet(StringComparer.Ordinal);
        foreach (var hasta in hastalar.Where(h => h.TakmaAd == ""))
        {
            for (var tuz = 0; ; tuz++)
            {
                var aday = "P-" + DeterministikGuid.Uret("takma-ad", hasta.Id, tuz).ToString("N")[..4].ToUpperInvariant();
                if (kullanilan.Add(aday)) { hasta.TakmaAd = aday; break; }
            }
        }
    }

    private static List<TahlilTanimi> TahlilTanimlari() =>
    [
        new() { Id = SenaryoIdleri.HbA1c, Ad = "HbA1c", HastaDostuAd = "Şeker ortalaması", LoincKodu = "4548-4",
            Birim = "%", AltSinir = 4.0m, UstSinir = 5.6m },
        new() { Id = SenaryoIdleri.AclikGlukozu, Ad = "Glukoz (açlık)", HastaDostuAd = "Açlık kan şekeri", LoincKodu = "1558-6",
            Birim = "mg/dL", AltSinir = 70m, UstSinir = 100m, KritikAlt = 40m, KritikUst = 400m },
        new() { Id = SenaryoIdleri.Hemoglobin, Ad = "Hemoglobin", HastaDostuAd = "Kan değeri (hemoglobin)", LoincKodu = "718-7",
            Birim = "g/dL", AltSinir = 12.0m, UstSinir = 15.5m, CinsiyeteGore = true, ErkekAltSinir = 13.5m, ErkekUstSinir = 17.5m,
            KritikAlt = 7.0m, KritikUst = 20.0m },
        new() { Id = SenaryoIdleri.Ferritin, Ad = "Ferritin", HastaDostuAd = "Demir deposu", LoincKodu = "2276-4",
            Birim = "ng/mL", AltSinir = 15m, UstSinir = 150m, CinsiyeteGore = true, ErkekAltSinir = 30m, ErkekUstSinir = 400m },
        new() { Id = SenaryoIdleri.Tsh, Ad = "TSH", HastaDostuAd = "Tiroid hormonu (TSH)", LoincKodu = "3016-3",
            Birim = "mIU/L", AltSinir = 0.4m, UstSinir = 4.0m },
    ];

    private static List<TahlilSonucu> SenaryoTahlilleri(List<TahlilTanimi> tanimlar, List<Hasta> hastalar, DateTime utcSimdi)
    {
        // "Bu sabah 08:15" (İstanbul); şimdi 08:15'ten önceyse bir önceki günün sabahı.
        var sabah = IstanbulSaati.Utc(IstanbulSaati.Bugun(utcSimdi), new TimeOnly(8, 15));
        if (sabah > utcSimdi) sabah = sabah.AddDays(-1);

        var tanim = tanimlar.ToDictionary(t => t.Id);
        var cinsiyet = hastalar.ToDictionary(h => h.Id, h => h.Cinsiyet);
        var liste = new List<TahlilSonucu>();

        void Ekle(Guid hastaId, Guid tanimId, decimal deger, DateTime zaman)
        {
            var t = tanim[tanimId];
            liste.Add(new TahlilSonucu
            {
                Id = DeterministikGuid.Uret("tahlil", hastaId, tanimId, liste.Count),
                HastaId = hastaId, TahlilTanimiId = tanimId, Deger = deger, Zaman = zaman,
                Isaret = TahlilDegerlendirici.Degerlendir(t, cinsiyet[hastaId], deger),
            });
        }

        // Senaryo 1 — Ayşe: HbA1c 6,6 → 7,0 → 7,4 (bu sabah), açlık glukozu 128, Hb 13,1.
        Ekle(SenaryoIdleri.Ayse, SenaryoIdleri.HbA1c, 6.6m, sabah.AddMonths(-6));
        Ekle(SenaryoIdleri.Ayse, SenaryoIdleri.HbA1c, 7.0m, sabah.AddMonths(-3));
        Ekle(SenaryoIdleri.Ayse, SenaryoIdleri.HbA1c, 7.4m, sabah);
        Ekle(SenaryoIdleri.Ayse, SenaryoIdleri.AclikGlukozu, 128m, sabah);
        Ekle(SenaryoIdleri.Ayse, SenaryoIdleri.Hemoglobin, 13.1m, sabah);

        // Senaryo 3 — Zeynep: Hb düşük, ferritin düşük.
        Ekle(SenaryoIdleri.Zeynep, SenaryoIdleri.Hemoglobin, 10.2m, sabah.AddMinutes(10));
        Ekle(SenaryoIdleri.Zeynep, SenaryoIdleri.Ferritin, 8m, sabah.AddMinutes(10));

        // Senaryo 5 — Elif: TSH yüksek.
        Ekle(SenaryoIdleri.Elif, SenaryoIdleri.Tsh, 7.8m, sabah.AddMinutes(20));

        return liste;
    }

    /// <summary>Şimdiden 10 dk'ya yukarı yuvarlanarak sonraki 8 saat, her hekim için 10 dk'lık slotlar.</summary>
    private static List<Slot> Slotlar(List<Hekim> hekimler, DateTime utcSimdi)
    {
        var adim = TimeSpan.FromMinutes(SlotDk).Ticks;
        var ilk = new DateTime((utcSimdi.Ticks + adim - 1) / adim * adim, DateTimeKind.Utc);
        var adet = SlotSaati * 60 / SlotDk;

        var liste = new List<Slot>(hekimler.Count * adet);
        for (var h = 0; h < hekimler.Count; h++)
        {
            var r = new Randomizer(BogusSeed + h + 1);
            for (var i = 0; i < adet; i++)
            {
                liste.Add(new Slot
                {
                    // Id sıra numarasından türetilir; saat şimdiye göre kaysa da Id aynı kalır.
                    Id = DeterministikGuid.Uret("slot", hekimler[h].Id, i),
                    HekimId = hekimler[h].Id,
                    Baslangic = ilk.AddMinutes(i * SlotDk),
                    SureDk = SlotDk,
                    Dolu = r.Bool(0.35f),
                });
            }
        }
        return liste;
    }

    /// <summary>Dolu slotlara dolgu hastalardan randevu; sıra no branş + İstanbul günü içinde artar.</summary>
    private static List<Randevu> DolguRandevular(List<Slot> slotlar, List<Hekim> hekimler, List<Hasta> hastalar)
    {
        var dolgu = hastalar.Where(h => !SenaryoHastasiMi(h.Id)).ToList();
        var brans = hekimler.ToDictionary(h => h.Id, h => h.BransId);
        var r = new Randomizer(BogusSeed + 100);
        var sayac = new Dictionary<(Guid, DateOnly), int>();
        var liste = new List<Randevu>();

        foreach (var slot in slotlar.Where(s => s.Dolu).OrderBy(s => s.Baslangic).ThenBy(s => s.Id))
        {
            var anahtar = (brans[slot.HekimId], IstanbulSaati.Bugun(slot.Baslangic));
            var sira = sayac.GetValueOrDefault(anahtar) + 1;
            sayac[anahtar] = sira;
            liste.Add(new Randevu
            {
                Id = DeterministikGuid.Uret("randevu", slot.Id),
                HastaId = r.ListItem(dolgu).Id,
                HekimId = slot.HekimId,
                SlotId = slot.Id,
                Durum = RandevuDurumu.Planlandi,
                SiraNo = SiraNo.Olustur(sira),
            });
        }
        return liste;
    }

    private static bool SenaryoHastasiMi(Guid id) =>
        id == SenaryoIdleri.Ayse || id == SenaryoIdleri.Mehmet || id == SenaryoIdleri.Zeynep ||
        id == SenaryoIdleri.Hasan || id == SenaryoIdleri.Elif || id == SenaryoIdleri.Can;
}

public sealed record SeedSayimlari(int Brans, int Hekim, int Hasta, int Slot, int Randevu, int TahlilSonucu);
