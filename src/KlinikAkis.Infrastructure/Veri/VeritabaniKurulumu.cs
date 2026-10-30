using KlinikAkis.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlinikAkis.Infrastructure.Veri;

public static class VeritabaniKurulumu
{
    /// <summary><paramref name="saglayici"/> = <c>Sqlite</c> (yerel) | <c>Postgres</c> (Neon) (SPEC §3).</summary>
    public static IServiceCollection AddKlinikVeri(this IServiceCollection services, string saglayici, string baglanti)
    {
        if (string.Equals(saglayici, "Sqlite", StringComparison.OrdinalIgnoreCase))
            services.AddDbContext<KlinikDbContext>(o => o.UseSqlite(baglanti));
        else if (string.Equals(saglayici, "Postgres", StringComparison.OrdinalIgnoreCase))
            services.AddDbContext<KlinikDbContext>(o => o.UseNpgsql(baglanti));
        else
            throw new NotSupportedException($"Db:Provider '{saglayici}' desteklenmiyor (Sqlite | Postgres).");

        services.AddScoped<DemoSifirlayici>();
        return services;
    }
}
