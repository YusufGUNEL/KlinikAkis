namespace KlinikAkis.Domain.Varliklar;

// SPEC §5. Tüm Id'ler Guid; zamanlar UTC DateTime (Kind=Utc).

public sealed class Hasta
{
    public Guid Id { get; set; }
    public string TcKimlik { get; set; } = "";
    public string Ad { get; set; } = "";
    public string Soyad { get; set; } = "";
    public DateOnly DogumTarihi { get; set; }
    public Cinsiyet Cinsiyet { get; set; }
    public string Telefon { get; set; } = "";
    public string Eposta { get; set; } = "";
    public List<string> Alerjiler { get; set; } = [];
    public List<string> KronikHastaliklar { get; set; } = [];
    public List<string> KullandigiIlaclar { get; set; } = [];
    public bool AcikRizaAlindi { get; set; }
    public DateTime? AcikRizaZamani { get; set; }
    public string TakmaAd { get; set; } = "";
    public bool Sentetik { get; set; } = true;
}

public sealed class Brans
{
    public Guid Id { get; set; }
    public string Ad { get; set; } = "";
    public BransKodu Kod { get; set; }
}

public sealed class Hekim
{
    public Guid Id { get; set; }
    public string Ad { get; set; } = "";
    public string Unvan { get; set; } = "";
    public Guid BransId { get; set; }
    public Brans? Brans { get; set; }
    public string Oda { get; set; } = "";
}

public sealed class Slot
{
    public Guid Id { get; set; }
    public Guid HekimId { get; set; }
    public Hekim? Hekim { get; set; }
    public DateTime Baslangic { get; set; }
    public int SureDk { get; set; } = 10;
    public bool Dolu { get; set; }
}

public sealed class Randevu
{
    public Guid Id { get; set; }
    public Guid HastaId { get; set; }
    public Hasta? Hasta { get; set; }
    public Guid HekimId { get; set; }
    public Hekim? Hekim { get; set; }
    public Guid SlotId { get; set; }
    public Slot? Slot { get; set; }
    public RandevuDurumu Durum { get; set; } = RandevuDurumu.Planlandi;
    public string SiraNo { get; set; } = "";
    public Guid? OnAnamnezId { get; set; }
}

public sealed class Kabul
{
    public Guid Id { get; set; }
    public Guid RandevuId { get; set; }
    public DateTime KabulZamani { get; set; }
    public string TakipNo { get; set; } = "";
    public string KabulEden { get; set; } = "";
}

public sealed class OnAnamnez
{
    public Guid Id { get; set; }
    public Guid HastaId { get; set; }
    public Guid? RandevuId { get; set; }
    public string SohbetJson { get; set; } = "[]";
    public string? AiTaslakJson { get; set; }
    public AiKaynak? Kaynak { get; set; }
    /// <summary>Kart başlığı (SPEC §10.2): "AI taslağı · {model} · {saat}". Yedek/kuralda boş.</summary>
    public string? AiModel { get; set; }
    public DateTime? AiZamani { get; set; }
    /// <summary>Günlük kota ya da sağlayıcı 429'u: model çağrılmadı, taslak kural tabanlı; hekim kartında rozet (§9.9).</summary>
    public bool KotaDoldu { get; set; }
    public bool HastaOnayi { get; set; }
    public string? HastaNotu { get; set; }
    public string? HekimDuzeltmesi { get; set; }
    public OnAnamnezDurumu Durum { get; set; } = OnAnamnezDurumu.Sohbette;
    public Guid? OnaylayanHekimId { get; set; }
    public DateTime? OnayZamani { get; set; }
    public string? KirmiziBayrakKurali { get; set; }
}

public sealed class Muayene
{
    public Guid Id { get; set; }
    public Guid RandevuId { get; set; }
    public DateTime BaslangicZamani { get; set; }
    public string? HekimNotu { get; set; }
    public List<string> Tanilar { get; set; } = [];
    public MuayeneDurumu Durum { get; set; } = MuayeneDurumu.Acik;
}

public sealed class TahlilTanimi
{
    public Guid Id { get; set; }
    public string Ad { get; set; } = "";
    public string HastaDostuAd { get; set; } = "";
    public string LoincKodu { get; set; } = "";
    public string Birim { get; set; } = "";
    /// <summary>Cinsiyete göre ayrı değilse her iki cinsiyet için bu aralık kullanılır.</summary>
    public decimal AltSinir { get; set; }
    public decimal UstSinir { get; set; }
    public bool CinsiyeteGore { get; set; }
    /// <summary>CinsiyeteGore=true ise erkek aralığı; Alt/UstSinir kadın aralığıdır.</summary>
    public decimal? ErkekAltSinir { get; set; }
    public decimal? ErkekUstSinir { get; set; }
    public decimal? KritikAlt { get; set; }
    public decimal? KritikUst { get; set; }
    public string Not { get; set; } = "Örnek aralık, laboratuvara göre değişir.";
}

public sealed class TahlilSonucu
{
    public Guid Id { get; set; }
    public Guid HastaId { get; set; }
    public Guid TahlilTanimiId { get; set; }
    public TahlilTanimi? TahlilTanimi { get; set; }
    public decimal Deger { get; set; }
    public DateTime Zaman { get; set; }
    public Isaret Isaret { get; set; }
    public string? AiAciklamaTaslak { get; set; }
    public string? YayinlananAciklama { get; set; }
    public bool HastayaYayinlandi { get; set; }
    public DateTime? YayinZamani { get; set; }
    public Guid? YayinlayanHekimId { get; set; }
}

public sealed class Epikriz
{
    public Guid Id { get; set; }
    public Guid MuayeneId { get; set; }
    public string AiTaslak { get; set; } = "";
    public string? SonMetin { get; set; }
    public AiKaynak Kaynak { get; set; }
    public EpikrizDurumu Durum { get; set; } = EpikrizDurumu.Taslak;
    public Guid? OnaylayanHekimId { get; set; }
}

public sealed class OutboxMesaji
{
    public Guid Id { get; set; }
    public PaketTipi PaketTipi { get; set; }
    public string HastaTakmaAdi { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public OutboxDurumu Durum { get; set; } = OutboxDurumu.Bekliyor;
    public int Deneme { get; set; }
    public DateTime Zaman { get; set; }
    public string? Hata { get; set; }
}

public sealed class AiAuditLog
{
    public Guid Id { get; set; }
    public DateTime Zaman { get; set; }
    public AiOzellik Ozellik { get; set; }
    /// <summary>"Modele giden veri" paneli (GET /ai/denetim?hastaId=) hastaya göre süzer; modele gitmez.</summary>
    public Guid? HastaId { get; set; }
    public string Saglayici { get; set; } = "";
    public string Model { get; set; } = "";
    public string IstemSurumu { get; set; } = "";
    public string MaskeliGirdi { get; set; } = "";
    public string GirdiHash { get; set; } = "";
    public string? CiktiJson { get; set; }
    public int GecikmeMs { get; set; }
    public bool Basarili { get; set; }
    public bool YedegeDustu { get; set; }
    public string? Neden { get; set; }
}

public sealed class ErisimKaydi
{
    public Guid Id { get; set; }
    public DateTime Zaman { get; set; }
    public string KullaniciRol { get; set; } = "";
    public string KullaniciAd { get; set; } = "";
    public Guid HastaId { get; set; }
    public string Eylem { get; set; } = "";
}
