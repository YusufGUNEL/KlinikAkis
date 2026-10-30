namespace KlinikAkis.Domain;

/// <summary>Kural adları denetim kaydına (AiAuditLog.Neden, OnAnamnez.KirmiziBayrakKurali) bu adla yazılır.</summary>
public enum KirmiziBayrakKurali
{
    GogusAgrisiTerleme,
    GogusAgrisi,
    InmeBulgusu,
    AgirNefesDarligi,
    BilincKaybi,
    KendineZarar,
    GebelikKanama,
    BebekAtes,
}

public sealed record KirmiziBayrak(KirmiziBayrakKurali Kural, string Mesaj);

/// <summary>
/// Kırmızı bayrak kuralları (SPEC §9.3). LLM'den önce, maskelenmemiş ham metin üzerinde sunucu içinde çalışır.
/// Kök eşleşmesi katlanmış metinde yapılır: "GÖĞÜS", "göğsüm", "gogsum" aynı köke düşer.
/// Olumsuzlama ("göğüs ağrım yok") ayırt edilmez: fazla tetiklemek güvenli taraftır (bilinçli karar).
/// Sohbetin tüm cevapları birlikte verilir; "göğsüm ağrıyor" 1. adımda, "terledim" 6. adımda yazılsa da yakalanır.
/// </summary>
public static class KirmiziBayrakMotoru
{
    public const string AcilMesaji = "Belirtileriniz acil olabilir. Hemen 112'yi arayın ya da en yakın acile başvurun.";

    private static readonly string[] Gogus = ["gogus", "gogs", "kalp", "kalb"];
    private static readonly string[] Agri = ["agri", "aci", "sanci", "sikis", "baski", "yanma"];
    private static readonly string[] TerlemeYayilma =
        ["terl", "ter bas", "soguk ter", "kola yayil", "koluma yayil", "sol kol", "cene", "sirtima yayil"];
    // Yüz/ağız + kayma ayrı köklerle aranır: "yüzümde kayma", "babamın yüzü kaydı", "ağzı kaydı" hepsi yakalanır.
    private static readonly string[] YuzAgiz = ["yuz", "agz", "agiz"];
    private static readonly string[] Kayma = ["kay"];
    private static readonly string[] Inme =
        ["konusma boz", "konusmam boz", "konusmasi boz", "konusamiyor", "konusamadi", "konusmakta zor",
         "dilim dolas", "dili dolas", "tek taraf", "bir tarafim", "sol taraf", "sag taraf", "uyus", "felc",
         "kolum tutmuyor", "bacagim tutmuyor"];
    private static readonly string[] NefesDarligi =
        ["nefes alami", "nefes alama", "nefesim kesil", "nefes darl", "nefesim dar", "nefes almakta zor", "bogul",
         "soluk alami"];
    private static readonly string[] Bilinc = ["bayil", "bayg", "bilinc", "kendimden gec", "kendini kaybet"];
    private static readonly string[] KendineZararKokleri =
        ["intihar", "kendimi oldur", "olmek isti", "kendime zarar", "canima kiy", "yasamak istemi"];
    private static readonly string[] Gebelik = ["gebe", "hamile"];
    // Tek başına "kan" sözcüğü ("kan geldi") ayrıca tam sözcük olarak aranır; kök olarak "kan" "kanepe"yi de yakalardı.
    private static readonly string[] Kanama = ["kana", "kani", "lekelen"];
    // "38 °C ve üstü" seçeneği katlanınca "38 c ve ustu" olur; "38,5" → "38 5". "38 °C altı" ateş sayılmaz.
    private static readonly string[] Ates =
        ["ates", "38 c ve ustu", "38 ve ustu", "38 derece", "38 5", "38 6", "38 7", "38 8", "38 9", "39", "40", "41"];

    /// <param name="metinler">Sohbetteki tüm cevaplar (serbest metin + seçilen buton etiketleri).</param>
    /// <param name="yasAy">Hastanın ay cinsinden yaşı (3 ay altı bebekte ateş kuralı için).</param>
    public static KirmiziBayrak? Degerlendir(IEnumerable<string?> metinler, int yasAy)
    {
        var metin = TurkceMetin.Katla(string.Join(" . ", metinler));
        if (metin.Length == 0) return null;

        bool Herhangi(string[] kokler) => kokler.Any(k => TurkceMetin.KokVar(metin, k));

        // Öncelik sırası: en özgül kural adı denetim kaydına yazılsın (göğüs ağrısı + terleme > yalnız göğüs ağrısı).
        KirmiziBayrakKurali? kural = null;
        var gogusAgrisi = Herhangi(Gogus) && Herhangi(Agri);
        if (gogusAgrisi && Herhangi(TerlemeYayilma)) kural = KirmiziBayrakKurali.GogusAgrisiTerleme;
        else if (Herhangi(KendineZararKokleri)) kural = KirmiziBayrakKurali.KendineZarar;
        else if (Herhangi(Inme) || (Herhangi(YuzAgiz) && Herhangi(Kayma))) kural = KirmiziBayrakKurali.InmeBulgusu;
        else if (Herhangi(NefesDarligi)) kural = KirmiziBayrakKurali.AgirNefesDarligi;
        else if (Herhangi(Bilinc)) kural = KirmiziBayrakKurali.BilincKaybi;
        else if (Herhangi(Gebelik) && (Herhangi(Kanama) || TurkceMetin.SozcukVar(metin, "kan"))) kural = KirmiziBayrakKurali.GebelikKanama;
        else if (yasAy < 3 && Herhangi(Ates))
            kural = KirmiziBayrakKurali.BebekAtes;
        else if (gogusAgrisi) kural = KirmiziBayrakKurali.GogusAgrisi;

        return kural is { } k ? new KirmiziBayrak(k, AcilMesaji) : null;
    }
}
