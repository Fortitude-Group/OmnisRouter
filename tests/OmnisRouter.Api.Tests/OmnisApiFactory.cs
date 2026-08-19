using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OmnisRouter.Store;

namespace OmnisRouter.Api.Tests;

/// <summary>
/// Test host for <see cref="Program"/>. Points the store at a private temp-file SQLite database
/// (never the developer's real <c>omnisrouter.db</c>) so each test is isolated.
/// </summary>
public class OmnisApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"omnisrouter-api-tests-{Guid.NewGuid():N}.db");

    /// <summary>The private temp-file SQLite database backing this factory's app instance. Exposed
    /// so tests can open a second, raw <c>Microsoft.Data.Sqlite</c> connection to assert on-disk
    /// column contents (e.g. encryption-at-rest) without going through the app's DbContext.</summary>
    public string DbPath => _dbPath;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // NOTE: the app reads the connection string at service-registration time (AddOmnisStore),
        // which runs BEFORE ConfigureAppConfiguration is applied — so re-point the DbContext in
        // ConfigureTestServices (runs AFTER the app's registrations) instead of via config.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<OmnisRouterDbContext>>();
            services.AddDbContext<OmnisRouterDbContext>(options =>
                options.UseSqlite($"Data Source={_dbPath}",
                    sqlite => sqlite.MigrationsAssembly("OmnisRouter.Store.Migrations.Sqlite")));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        // The test DB is a fresh temp file with no schema — apply migrations once so auth lookups
        // (RouterTokens) and /readyz (CanConnect) hit a real schema instead of "no such table".
        using (var scope = host.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<OmnisRouterDbContext>().Database.Migrate();
        }

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a lingering handle shouldn't fail the test run.
            }
        }
    }
}
