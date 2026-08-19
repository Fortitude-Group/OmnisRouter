using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OmnisRouter.Core.Abstractions;

namespace OmnisRouter.Store.Migrations.Sqlite;

/// <summary>
/// Design-time factory so `dotnet ef migrations add`/`database update` can build the model without
/// a full DI composition root. The real <see cref="ISecretCipher"/> is wired at runtime by
/// <c>AddOmnisStore</c>/<c>AddOmnisByok</c>; this pass-through stub only needs to satisfy the
/// <see cref="OmnisRouterDbContext"/> constructor for model-building — the converter delegates it
/// backs are never invoked while generating or applying a migration.
/// </summary>
public sealed class OmnisRouterSqliteDesignTimeDbContextFactory : IDesignTimeDbContextFactory<OmnisRouterDbContext>
{
    public OmnisRouterDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OmnisRouterDbContext>();
        optionsBuilder.UseSqlite(
            "Data Source=omnisrouter.designtime.db",
            sqlite => sqlite.MigrationsAssembly(
                typeof(OmnisRouterSqliteDesignTimeDbContextFactory).Assembly.GetName().Name));

        return new OmnisRouterDbContext(optionsBuilder.Options, new DesignTimeSecretCipher());
    }

    private sealed class DesignTimeSecretCipher : ISecretCipher
    {
        public byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData = default) =>
            plaintext.ToArray();

        public byte[] Decrypt(ReadOnlySpan<byte> blob, ReadOnlySpan<byte> associatedData = default) =>
            blob.ToArray();
    }
}
