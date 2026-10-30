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
        var json = Yanitla?.Invoke(kullaniciMetni) ?? HazirYanit(kullaniciMetni);
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

    private static string HazirYanit(string metin)
    {
        var k = TurkceMetin.Katla(metin);
        bool Var(params string[] kokler) => kokler.Any(kok => TurkceMetin.KokVar(k, kok));

        object yanit = Var("su ic", "idrar", "susa")
            ? Ozet("Birkaç haftadır çok su içme, sık idrara çıkma ve halsizlik.", ["Çok su içme", "Sık idrar", "Halsizlik"],
                BransKodu.Endokrinoloji, BransKodu.Dahiliye, "Susama ve sık idrar, bilinen diyabet takibiyle uyumlu.")
            : Var("bogaz", "kulak")
                ? Ozet("Boğaz ağrısı ve ateş.", ["Boğaz ağrısı", "Ateş"],
                    BransKodu.Kbb, BransKodu.Dahiliye, "Boğaz ağrısı ve ateş kulak burun boğaz muayenesi gerektirir.")
                : Ozet("Genel şikayet; ayrıntı sohbette.", ["Genel şikayet"],
                    BransKodu.Dahiliye, null, "Belirgin bir bölüm işareti yok; genel değerlendirme.");
        return JsonSerializer.Serialize(yanit, Json);
    }

    private static object Ozet(string ozet, string[] semptomlar, BransKodu oneri, BransKodu? alternatif, string gerekce) => new
    {
        ozet,
        semptomlar,
        sure = "Sohbette belirtildi",
        siddet = "Orta",
        onerilenBrans = oneri.ToString(),
        alternatifBrans = alternatif?.ToString(),
        gerekce,
        aciliyet = "Rutin",
        doktoraSorular = Array.Empty<string>(),
        kaynaklar = new[] { new { madde = semptomlar[0], sohbetAdimi = 1 } },
    };
}
