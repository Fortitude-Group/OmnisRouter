using Microsoft.EntityFrameworkCore;
using OmnisRouter.Api.Routing;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Store;
using OmnisRouter.Store.Entities;

namespace OmnisRouter.Api.Tests;

/// <summary>
/// T056 — resolver-level test (no HTTP): <see cref="ProviderCredentialResolver"/> must never fall
/// back to a key configured for a different provider (FR-013) and must never leak the configured
/// key's value in its failure message.
/// </summary>
public sealed class MissingKeyResolverTests
{
    /// <summary>Trivial pass-through cipher — this test exercises resolver behavior, not
    /// encryption, so a real <c>ISecretCipher</c>/master key isn't needed to satisfy the
    /// <c>ProviderKey.ApiKey</c> value converter.</summary>
    private sealed class NoOpSecretCipher : ISecretCipher
    {
        public byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData = default) =>
            plaintext.ToArray();

        public byte[] Decrypt(ReadOnlySpan<byte> blob, ReadOnlySpan<byte> associatedData = default) =>
            blob.ToArray();
    }

    private sealed class TestDbContext : IDisposable
    {
        public readonly OmnisRouterDbContext Db;
        private readonly string _dbPath;

        public TestDbContext()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"omnisrouter-resolver-tests-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<OmnisRouterDbContext>()
                .UseSqlite($"Data Source={_dbPath}")
                .Options;
            Db = new OmnisRouterDbContext(options, new NoOpSecretCipher());
            Db.Database.EnsureCreated();
        }

        public void Dispose()
        {
            Db.Database.EnsureDeleted();
            Db.Dispose();
            if (File.Exists(_dbPath))
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

    private const string ProviderAKey = "sk-provider-a-only-secret";

    [Fact]
    public async Task ResolveAsync_for_an_unkeyed_provider_throws_a_non_leaking_OmnisException()
    {
        using var ctx = new TestDbContext();
        ctx.Db.ProviderKeys.Add(new ProviderKey
        {
            Id = Guid.NewGuid().ToString("n"),
            TenantId = "default",
            Provider = Provider.OpenAI,
            Label = "provider-a",
            ApiKey = ProviderAKey,
            KeyVersion = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.Db.SaveChangesAsync();

        var resolver = new ProviderCredentialResolver(ctx.Db);

        var ex = await Assert.ThrowsAsync<OmnisException>(
            () => resolver.ResolveAsync("default", Provider.Anthropic, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.DoesNotContain(ProviderAKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfiguredProvidersAsync_returns_only_the_provider_that_has_a_key()
    {
        using var ctx = new TestDbContext();
        ctx.Db.ProviderKeys.Add(new ProviderKey
        {
            Id = Guid.NewGuid().ToString("n"),
            TenantId = "default",
            Provider = Provider.OpenAI,
            Label = "provider-a",
            ApiKey = ProviderAKey,
            KeyVersion = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.Db.SaveChangesAsync();

        var resolver = new ProviderCredentialResolver(ctx.Db);
        var configured = await resolver.ConfiguredProvidersAsync("default", CancellationToken.None);

        Assert.Single(configured);
        Assert.Contains(Provider.OpenAI, configured);
        Assert.DoesNotContain(Provider.Anthropic, configured);
    }
}
