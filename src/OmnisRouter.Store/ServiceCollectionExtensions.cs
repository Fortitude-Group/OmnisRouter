using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Store.Logging;

namespace OmnisRouter.Store;

public static class ServiceCollectionExtensions
{
    /// <summary>Assembly name that must hold the SQLite migrations (<c>OmnisRouter.Store.Migrations.Sqlite</c>).</summary>
    public const string SqliteMigrationsAssembly = "OmnisRouter.Store.Migrations.Sqlite";

    /// <summary>Assembly name that must hold the Npgsql migrations (<c>OmnisRouter.Store.Migrations.Npgsql</c>).</summary>
    public const string NpgsqlMigrationsAssembly = "OmnisRouter.Store.Migrations.Npgsql";

    /// <summary>
    /// Registers <see cref="OmnisRouterDbContext"/> and <see cref="IDecisionLog"/>, selecting the
    /// database provider from <c>Database:Provider</c> config (<c>Sqlite</c> [default] or
    /// <c>Npgsql</c>/<c>Postgres</c>), wired to the matching per-provider migrations assembly so
    /// <c>dotnet ef database update</c> and startup migration both find the right assembly.
    /// </summary>
    public static IServiceCollection AddOmnisStore(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "Sqlite";
        var connectionString = configuration.GetConnectionString("Default")
            ?? configuration["Database:ConnectionString"]
            ?? DefaultConnectionString(provider);

        services.AddDbContext<OmnisRouterDbContext>(options =>
        {
            if (IsNpgsql(provider))
            {
                options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(NpgsqlMigrationsAssembly));
            }
            else
            {
                options.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(SqliteMigrationsAssembly));
            }
        });

        services.AddScoped<IDecisionLog, SqlDecisionLog>();

        return services;
    }

    private static bool IsNpgsql(string provider) =>
        provider.Equals("Npgsql", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase);

    private static string DefaultConnectionString(string provider) =>
        IsNpgsql(provider)
            ? "Host=localhost;Database=omnisrouter"
            : "Data Source=omnisrouter.db";
}
