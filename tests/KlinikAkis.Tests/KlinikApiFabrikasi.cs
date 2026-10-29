using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KlinikAkis.Tests;

/// <summary>Her fabrika kendi geçici SQLite dosyasını ve sabit saatini kullanır.</summary>
public sealed class KlinikApiFabrikasi : WebApplicationFactory<Program>
{
    /// <summary>29.10.2026 14:00 İstanbul.</summary>
    public static readonly DateTimeOffset Simdi = new(2026, 10, 29, 11, 0, 0, TimeSpan.Zero);

    public string DbYolu { get; } = Path.Combine(Path.GetTempPath(), $"klinikakis-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DbYolu}");
        builder.UseSetting("Jwt:Key", "test-anahtari-yalnizca-testlerde-kullanilir-32+");
        builder.ConfigureTestServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(new SabitZaman(Simdi))));
    }

    /// <summary>/api/demo/login ile token alır; token'ı Authorization başlığına koyan istemci döner.</summary>
    public async Task<HttpClient> GirisYapAsync(string rol, Guid? kimlikId = null)
    {
        var istemci = CreateClient();
        var yanit = await istemci.PostAsJsonAsync("/api/demo/login", new { rol, kimlikId });
        yanit.EnsureSuccessStatusCode();
        var token = JsonDocument.Parse(await yanit.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString();
        istemci.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return istemci;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        File.Delete(DbYolu);
    }

    private sealed class SabitZaman(DateTimeOffset simdi) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => simdi;
    }
}
