using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Routing;
using OmnisRouter.Store.Entities;

namespace OmnisRouter.Store;

/// <summary>
/// EF Core store for OmnisRouter persisted entities (see specs/001-omnisrouter/data-model.md).
/// The <see cref="ISecretCipher"/> injected here closes over the <c>ProviderKey.ApiKey</c>
/// encrypted-column <see cref="ValueConverter{TModel,TProvider}"/> at model-build time.
/// </summary>
public sealed class OmnisRouterDbContext : DbContext
{
    private readonly ISecretCipher _secretCipher;

    public OmnisRouterDbContext(DbContextOptions<OmnisRouterDbContext> options, ISecretCipher secretCipher)
        : base(options)
    {
        _secretCipher = secretCipher;
    }

    public DbSet<Install> Installs => Set<Install>();
    public DbSet<ProviderKey> ProviderKeys => Set<ProviderKey>();
    public DbSet<RouterToken> RouterTokens => Set<RouterToken>();
    public DbSet<Usage> Usages => Set<Usage>();
    public DbSet<SessionPin> SessionPins => Set<SessionPin>();
    public DbSet<DecisionLogEntry> DecisionLogEntries => Set<DecisionLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var secretCipher = _secretCipher;

        // Encrypt/Decrypt take ReadOnlySpan<byte>, which cannot appear inside an expression tree
        // (CS8640) — route through byte[]-signature static helpers so the converter lambdas stay
        // expression-tree-safe while the span usage happens in ordinary (non-tree) method bodies.
        var apiKeyConverter = new ValueConverter<string, byte[]>(
            plaintext => EncryptApiKey(secretCipher, plaintext),
            ciphertext => DecryptApiKey(secretCipher, ciphertext));

        modelBuilder.Entity<Install>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).IsRequired();
            entity.HasIndex(e => e.TenantId);
        });

        modelBuilder.Entity<ProviderKey>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Encrypted-column mapping: ApiKey (string, plaintext in-memory) persists as the
            // AES-256-GCM blob (keyVersion‖nonce‖tag‖ciphertext). Not queryable — lookups are by
            // (TenantId, Provider) or Id only, never by key content (see data-model.md invariants).
            entity.Property(e => e.ApiKey)
                .HasConversion(apiKeyConverter)
                .HasColumnName("ApiKeyEncrypted")
                .IsRequired();

            entity.HasIndex(e => new { e.TenantId, e.Provider });
        });

        modelBuilder.Entity<RouterToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.HashedToken).IsUnique();
            entity.HasIndex(e => e.TenantId);
        });

        modelBuilder.Entity<Usage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.Date, e.Provider, e.ModelId });
            entity.Property(e => e.CostUsd).HasColumnType("decimal(18,8)");
            entity.Property(e => e.CostVsBigUsd).HasColumnType("decimal(18,8)");
        });

        modelBuilder.Entity<SessionPin>(entity =>
        {
            entity.HasKey(e => e.SessionKey);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.ExpiresAt);
        });

        modelBuilder.Entity<DecisionLogEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.TenantId);
            entity.Property(e => e.EstCostUsd).HasColumnType("decimal(18,8)");
            entity.Property(e => e.EstCostDeltaVsBigUsd).HasColumnType("decimal(18,8)");
        });
    }

    private static byte[] EncryptApiKey(ISecretCipher cipher, string plaintext) =>
        cipher.Encrypt(Encoding.UTF8.GetBytes(plaintext));

    private static string DecryptApiKey(ISecretCipher cipher, byte[] ciphertext) =>
        Encoding.UTF8.GetString(cipher.Decrypt(ciphertext));
}
