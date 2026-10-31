using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace KlinikAkis.Infrastructure.Ai;

/// <param name="Saglayici">Etkin sağlayıcı (<c>Gemini</c> | <c>Fake</c>); denetim kaydına yazılır.</param>
/// <param name="Uyari">Yapılandırma istenenden farklı uygulandıysa (anahtar yok vb.) başlangıçta günlüğe yazılır.</param>
public sealed record AiAyarlari(string Saglayici, string Model, int GunlukLimit, TimeSpan ZamanAsimi, string? Uyari = null)
{
    public const string Gemini = "Gemini";
    public const string VarsayilanGeminiModeli = "gemini-3.5-flash-lite";
    /// <summary>Gemini'nin OpenAI uyumlu ucu (ai.google.dev/gemini-api/docs/openai).</summary>
    public static readonly Uri GeminiUcu = new("https://generativelanguage.googleapis.com/v1beta/openai/");
}

public static class AiKurulumu
{
    /// <summary>
    /// <c>AI:Provider</c> = <c>Gemini</c> | <c>Fake</c> (Ollama Aşama 10'da aynı OpenAI uyumlu yolla). Anahtar
    /// <c>GEMINI_API_KEY</c> (user-secrets / ortam değişkeni). Anahtar yoksa ya da sağlayıcı tanınmıyorsa uygulama
    /// düşmez, Fake kullanılır (demo asla kırılmaz, SPEC §1.2); uyarıyı Program.cs yazar.
    /// </summary>
    public static AiAyarlari AddKlinikAi(this IServiceCollection services, IConfiguration config)
    {
        var ayar = Ayarlar(config, out var anahtar);
        services.AddSingleton(ayar);
        services.AddSingleton<FakeChatClient>();
        services.AddSingleton(sp => new GunlukKota(sp.GetRequiredService<TimeProvider>(), ayar.GunlukLimit));
        services.AddChatClient(sp => ayar.Saglayici == AiAyarlari.Gemini
                ? GeminiIstemcisi(anahtar!, ayar.Model)
                : sp.GetRequiredService<FakeChatClient>())
            .UseLogging();
        services.AddScoped<AiHatti>();
        return ayar;
    }

    private static AiAyarlari Ayarlar(IConfiguration config, out string? anahtar)
    {
        var istenen = config["AI:Provider"] ?? FakeChatClient.Saglayici;
        var limit = Sayi(config["AI:GunlukLimit"], 300);
        var zamanAsimi = TimeSpan.FromSeconds(Math.Max(1, Sayi(config["AI:ZamanAsimiSn"], 15)));
        anahtar = config["GEMINI_API_KEY"];

        if (string.Equals(istenen, AiAyarlari.Gemini, StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(anahtar)
                ? Fake(limit, zamanAsimi, "AI:Provider=Gemini ama GEMINI_API_KEY tanımlı değil; Fake sağlayıcı kullanılıyor. " +
                    "Anahtar için: dotnet user-secrets set \"GEMINI_API_KEY\" <anahtar> --project src/KlinikAkis.Api")
                : new AiAyarlari(AiAyarlari.Gemini, config["AI:Model"] ?? AiAyarlari.VarsayilanGeminiModeli, limit, zamanAsimi);

        return Fake(limit, zamanAsimi, string.Equals(istenen, FakeChatClient.Saglayici, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"AI:Provider '{istenen}' desteklenmiyor; Fake sağlayıcı kullanılıyor.");
    }

    private static int Sayi(string? deger, int varsayilan) =>
        int.TryParse(deger, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var s) && s >= 0
            ? s : varsayilan;

    private static AiAyarlari Fake(int limit, TimeSpan zamanAsimi, string? uyari) =>
        new(FakeChatClient.Saglayici, FakeChatClient.ModelAdi, limit, zamanAsimi, uyari);

    private static IChatClient GeminiIstemcisi(string anahtar, string model) =>
        new OpenAIClient(new ApiKeyCredential(anahtar), new OpenAIClientOptions
            {
                Endpoint = AiAyarlari.GeminiUcu,
                // 429'da SDK kendi başına beklemesin: kota dolduysa hemen kural tabanlı yedeğe geçilir (§9.9),
                // geçici hatada 15 sn bütçe tekrar denemeyle tükenmesin, yedeğe düşülür.
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            })
            .GetChatClient(model)
            .AsIChatClient();
}
