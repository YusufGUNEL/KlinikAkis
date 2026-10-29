using KlinikAkis.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlinikAkis.Infrastructure.Veri;

public static class VeritabaniKurulumu
{
    /// <summary><paramref name="saglayici"/> = <c>Sqlite</c> | <c>Postgres</c> (SPEC §3). Postgres Aşama 3'te eklenir.</summary>
    public static IServiceCollection AddKlinikVeri(this IServiceCollection services, string saglayici, string baglanti)
    {
        if (!string.Equals(saglayici, "Sqlite", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Db:Provider '{saglayici}' henüz desteklenmiyor (Postgres Aşama 3'te).");

        services.AddDbContext<KlinikDbContext>(o => o.UseSqlite(baglanti));
        services.AddScoped<DemoSifirlayici>();
        return services;
    }
}
