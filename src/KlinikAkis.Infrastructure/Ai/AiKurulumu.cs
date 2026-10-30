using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace KlinikAkis.Infrastructure.Ai;

public static class AiKurulumu
{
    /// <summary>
    /// <c>AI:Provider</c>: şimdilik yalnızca <c>Fake</c>; Anthropic ve Ollama Aşama 5/10'da.
    /// Desteklenmeyen değer uygulamayı düşürmez, Fake kullanılır (demo asla kırılmaz, SPEC §1.2); uyarıyı Program.cs yazar.
    /// </summary>
    public static IServiceCollection AddKlinikAi(this IServiceCollection services)
    {
        services.AddSingleton<FakeChatClient>();
        services.AddSingleton<IChatClient>(sp => sp.GetRequiredService<FakeChatClient>());
        services.AddScoped<AiHatti>();
        return services;
    }

    public static bool Destekleniyor(string? saglayici) =>
        string.IsNullOrEmpty(saglayici) || string.Equals(saglayici, FakeChatClient.Saglayici, StringComparison.OrdinalIgnoreCase);
}
