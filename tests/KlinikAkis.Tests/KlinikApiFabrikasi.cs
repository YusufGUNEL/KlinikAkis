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
        builder.ConfigureTestServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(new SabitZaman(Simdi))));
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
