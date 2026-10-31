using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using KlinikAkis.Domain;
using Microsoft.Extensions.AI;

namespace KlinikAkis.Infrastructure.Ai;

/// <summary>
/// Ağsız, ücretsiz sahte sağlayıcı (SPEC §9.7). Senaryo şikayetlerine hazır §9.5 JSON'u döner.
/// Çağrı sayacı testlerde "LLM çağrılmadı" kanıtıdır; gecikme ve yanıt testlerde değiştirilebilir.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    public const string Saglayici = "Fake";
    public const string ModelAdi = "fake-onanamnez";

    private static readonly ChatClientMetadata Metadata = new(Saglayici, null, ModelAdi);
    // Denetim kaydında ve "modele giden veri" panelinde Türkçe harfler kaçırılmadan okunsun.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerOptions.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };
    private int _cagriSayisi;

    public int CagriSayisi => Volatile.Read(ref _cagriSayisi);
    public TimeSpan Gecikme { get; set; } = TimeSpan.FromMilliseconds(300);
    /// <summary>Testler için: istemdeki kullanıcı metninden yanıt üretir; null ise hazır senaryo yanıtı.</summary>
    public Func<string, string>? Yanitla { get; set; }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _cagriSayisi);
        if (Gecikme > TimeSpan.Zero)
            await Task.Delay(Gecikme, cancellationToken);

        var kullaniciMetni = string.Join("\n", messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text));
        var json = Yanitla?.Invoke(kullaniciMetni)
            ?? (options?.ResponseFormat as ChatResponseFormatJson)?.SchemaName switch
            {
                TahlilAciklamaYaniti.SemaAdi => TahlilYaniti(kullaniciMetni),
                EpikrizYaniti.SemaAdi => EpikrizYanitiUret(kullaniciMetni),
                TaniOnerisiYaniti.SemaAdi => TaniYaniti(kullaniciMetni),
                _ => HazirYanit(kullaniciMetni),
            };
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, json)) { ModelId = ModelAdi };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var yanit = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var guncelleme in yanit.ToChatResponseUpdates())
            yield return guncelleme;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null ? null
        : serviceType == typeof(ChatClientMetadata) ? Metadata
        : serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose() { }

    /// <summary>Tahlil açıklaması (§9.6): istemdeki "tahlil:" ve kodla hesaplanmış "işaret" satırından sade cümle.</summary>
    private static string TahlilYaniti(string metin)
    {
        string Satir(string anahtar) => metin.Split('\n')
            .FirstOrDefault(s => s.StartsWith(anahtar, StringComparison.Ordinal))?[anahtar.Length..].Trim() ?? "";
        var test = Satir("tahlil:");
        var isaret = Satir("işaret (kodla hesaplandı):");
        var degerler = Satir("son değerler (eskiden yeniye):").Split(" → ", StringSplitOptions.RemoveEmptyEntries);
        var aciklama = $"{test} sonucunuz {isaret}. "
            + (degerler.Length >= 2 ? $"Son ölçümleriniz sırasıyla {string.Join(" → ", degerler)}. " : "")
            + (isaret.Contains("kritik", StringComparison.Ordinal)
                ? "Doktorunuz sizinle iletişime geçecek."
                : "Bu sonucu bir sonraki görüşmede doktorunuzla konuşun.");
        return JsonSerializer.Serialize(new { aciklama }, Json);
    }

    /// <summary>Epikriz: istemdeki onaylı ön anamnez ve hekim notu bloklarından; yeni sayı ya da tanı eklemez.</summary>
    private static string EpikrizYanitiUret(string metin)
    {
        string Blok(string etiket)
        {
            var bas = metin.IndexOf($"<{etiket}>", StringComparison.Ordinal);
            var son = metin.IndexOf($"</{etiket}>", StringComparison.Ordinal);
            return bas < 0 || son < bas ? "" : metin[(bas + etiket.Length + 2)..son].Trim();
        }
        var anamnez = Blok("on_anamnez");
        var not = Blok("hekim_notu");
        var tahlilYok = metin.Contains("\n- yok\n", StringComparison.Ordinal);
        var disiVar = metin.Contains("▲", StringComparison.Ordinal) || metin.Contains("▼", StringComparison.Ordinal)
            || metin.Contains("‼", StringComparison.Ordinal);
        object yanit = new
        {
            oyku = anamnez.StartsWith("hekim onaylı ön anamnez yok", StringComparison.Ordinal)
                ? "Hasta poliklinik başvurusu; hekim onaylı ön anamnez yok."
                : $"Başvuru öyküsü: {anamnez}",
            degerlendirme = (not.StartsWith("hekim notu yok", StringComparison.Ordinal) ? "Muayene bulgusu girilmedi. " : $"Muayene: {not} ")
                + (tahlilYok ? "Tahlil sonucu yok." : disiVar ? "Referans dışı tahlil değerleri mevcut (işaretler kodla verildi)." : "Tahliller referans aralığında.")
                + " Tanı hekim tarafından belirlenecek.",
            plan = "Plan hekim tarafından belirlenecek.",
        };
        return JsonSerializer.Serialize(yanit, Json);
    }

    /// <summary>ICD-10 önerisi: istemdeki kronik hastalık, kodla işaretli tahlil ve ön anamnez satırlarından; yalnız listeden.</summary>
    private static string TaniYaniti(string metin)
    {
        var katli = TurkceMetin.Katla(metin);
        var oneriler = new List<object>();
        void Ekle(bool kosul, string kod, string gerekce)
        {
            if (kosul && oneriler.Count < TaniOnerisiYaniti.EnFazlaOneri) oneriler.Add(new { kod, gerekce });
        }
        Ekle(TurkceMetin.KokVar(katli, "tip 2 diyabet"), "E11.9", "Kayıttaki kronik hastalık: Tip 2 diyabet.");
        Ekle(metin.Contains("HbA1c:", StringComparison.Ordinal) && metin.Contains("▲ Y", StringComparison.Ordinal),
            "R73.9", "HbA1c ▲ Y (işaret kodla hesaplandı).");
        Ekle(TurkceMetin.KokVar(katli, "cok su ic"), "R63.1", "Girdide çok su içme geçiyor.");
        Ekle(TurkceMetin.KokVar(katli, "bogaz agri"), "R07.0", "Girdide boğaz ağrısı geçiyor.");
        Ekle(TurkceMetin.KokVar(katli, "kulak agri"), "H92.0", "Girdide kulak ağrısı geçiyor.");
        // Dayanak yoksa boş liste (istem kuralı); hekim Düzenle ile listeden seçer.
        return JsonSerializer.Serialize(new { oneriler }, Json);
    }

    private static string HazirYanit(string metin)
    {
        // "N. soru → cevap" satırları: belirti yalnızca hastanın cevabında geçiyorsa ve o adıma bağlı yazılır.
        var cevaplar = new List<(int Adim, string Ham, string Katli)>();
        foreach (var satir in metin.Split('\n'))
        {
            var nokta = satir.IndexOf(". ", StringComparison.Ordinal);
            var ok = satir.IndexOf(" → ", StringComparison.Ordinal);
            if (nokta <= 0 || ok < nokta || !int.TryParse(satir[..nokta], CultureInfo.InvariantCulture, out var adim)) continue;
            var ham = satir[(ok + 3)..];
            cevaplar.Add((adim, ham, TurkceMetin.Katla(ham)));
        }
        int? Adim(params string[] kokler) =>
            cevaplar.Where(c => kokler.Any(kok => TurkceMetin.KokVar(c.Katli, kok))).Select(c => (int?)c.Adim).FirstOrDefault();
        bool Var(params string[] kokler) => Adim(kokler) is not null;

        (string Madde, string[] Kokler)[] adaylar;
        BransKodu oneri;
        BransKodu? alternatif;
        string gerekce;
        if (Var("su ic", "idrar", "susa"))
        {
            adaylar = [("Çok su içme", ["su ic", "susa"]), ("Sık idrara çıkma", ["idrar"]), ("Halsizlik", ["halsiz", "yorgun"])];
            (oneri, alternatif, gerekce) = (BransKodu.Endokrinoloji, BransKodu.Dahiliye,
                "Çok su içme ve sık idrara çıkma endokrinoloji bölümünde değerlendirilir.");
        }
        else if (Var("kulak", "kulag"))
        {
            adaylar = [("Kulak ağrısı", ["kulak", "kulag"]), ("Ateş", ["ates", "38"])];
            // İstemdeki "çocuk: evet/hayır" satırını kod yazar; Fake de ona uyar (yetişkine Çocuk Sağlığı reddedilir).
            (oneri, alternatif, gerekce) = metin.Contains("çocuk: evet", StringComparison.Ordinal)
                ? (BransKodu.CocukSagligi, BransKodu.Kbb, "Çocuk hastada kulak ağrısı; önce Çocuk Sağlığı değerlendirmesi.")
                : (BransKodu.Kbb, (BransKodu?)BransKodu.Dahiliye, "Kulak ağrısı kulak burun boğaz muayenesi gerektirir.");
        }
        else if (Var("bogaz"))
        {
            adaylar = [("Boğaz ağrısı", ["bogaz"]), ("Yutkunma güçlüğü", ["yutkun"]), ("Ateş", ["ates", "38"])];
            (oneri, alternatif, gerekce) = (BransKodu.Kbb, BransKodu.Dahiliye,
                "Boğaz ağrısı kulak burun boğaz muayenesi gerektirir.");
        }
        else if (Var("kilo al", "usu"))
        {
            adaylar = [("Kilo alma", ["kilo al"]), ("Üşüme", ["usu"])];
            (oneri, alternatif, gerekce) = (BransKodu.Endokrinoloji, BransKodu.Dahiliye,
                "Kilo alma ve üşüme endokrinoloji bölümünde değerlendirilir.");
        }
        else
        {
            adaylar = [];
            (oneri, alternatif, gerekce) = (BransKodu.Dahiliye, null, "Belirgin bir bölüm işareti yok; genel değerlendirme.");
        }

        var maddeler = adaylar.Select(a => (a.Madde, Adim: Adim(a.Kokler))).Where(m => m.Adim is not null).ToList();
        if (maddeler.Count == 0 && cevaplar.Count > 0) maddeler.Add(("Genel şikayet", cevaplar[0].Adim));
        var sure = cevaplar.FirstOrDefault(c => c.Adim == 2).Ham;

        // Harf büyütüp küçültme yok (kültürsüz ToLower "İ/ı"yı bozar): maddeler olduğu gibi sıralanır.
        var ozet = maddeler.Count == 0 ? "Genel şikayet." : "Şikayetler: " + string.Join(", ", maddeler.Select(m => m.Madde)) + ".";
        object yanit = new
        {
            ozet,
            semptomlar = maddeler.Select(m => m.Madde).ToArray(),
            sure = string.IsNullOrWhiteSpace(sure) ? "Belirtilmedi" : sure,
            siddet = "Orta",
            onerilenBrans = oneri.ToString(),
            alternatifBrans = alternatif?.ToString(),
            gerekce,
            aciliyet = "Rutin",
            doktoraSorular = new[] { "Şikayet ne zaman başladı, giderek artıyor mu?" },
            kaynaklar = maddeler.Select(m => new { madde = m.Madde, sohbetAdimi = m.Adim }).ToArray(),
        };
        return JsonSerializer.Serialize(yanit, Json);
    }
}
