namespace KlinikAkis.Domain;

public enum Cinsiyet { K, E }

/// <summary>Bölüm kodu (ASCII). LLM çıktısı ve eşlemeler bu enum üzerinden doğrulanır.</summary>
public enum BransKodu
{
    Dahiliye, Kbb, Kardiyoloji, Noroloji, Goz, Ortopedi, CocukSagligi, KadinDogum, Dermatoloji, Uroloji,
    GogusHastaliklari, Endokrinoloji, GenelCerrahi, Psikiyatri, Ftr, Gastroenteroloji, Nefroloji, Romatoloji,
    Hematoloji, Onkoloji, EnfeksiyonHastaliklari, BeyinCerrahisi, KalpDamarCerrahisi, PlastikCerrahi,
    CocukCerrahisi, CocukPsikiyatrisi, Algoloji, Geriatri, AileHekimligi, AgizDisSagligi
}

public enum RandevuDurumu { Planlandi, KabulEdildi, Cagrildi, Muayenede, Tamamlandi, Iptal, Gelmedi }

public enum OnAnamnezDurumu { Sohbette, KirmiziBayrak, Taslak, Onaylandi, Duzenlendi, Reddedildi }

public enum AiKaynak { Llm, Yedek, Kural }

public enum Isaret { Normal, Yuksek, Dusuk, KritikYuksek, KritikDusuk }

public enum MuayeneDurumu { Acik, Kapali }

public enum EpikrizDurumu { Taslak, Onaylandi, Reddedildi }

public enum PaketTipi { HastaKayit = 101, Muayene = 103, LabSonuc = 105 }

public enum OutboxDurumu { Bekliyor, Gonderildi, IsKuraliHatasi }

public enum AiOzellik { OnAnamnez, TahlilAciklama, Epikriz, TaniOnerisi }

public enum TaniOnerisiDurumu { Taslak, Onaylandi, Reddedildi }

/// <summary>Ön anamnez şiddeti (SPEC §9.5); LLM şemasında enum.</summary>
public enum Siddet { Hafif, Orta, Siddetli }

public enum Aciliyet { Rutin, Oncelikli }

/// <summary>Kritik değer hastaya yayınlanırken hekimin zorunlu seçimi (SPEC §10.3); erişim kaydına yazılır.</summary>
public enum BilgilendirmeSecimi { TelefonlaBilgilendirildi, Bilgilendirilecek }
