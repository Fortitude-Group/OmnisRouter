using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OmnisRouter.Store;

namespace OmnisRouter.Api.Tests;

/// <summary>
/// Test host for <see cref="Program"/>. Points the store at a private temp-file SQLite database
/// (never the developer's real <c>omnisrouter.db</c>) so the test suite is self-contained.
/// </summary>
public sealed class OmnisApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"omnisrouter-api-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
            });
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
