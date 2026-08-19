using OmnisRouter.Core.Model;

namespace OmnisRouter.Store.Entities;

/// <summary>BYOK provider credential. <see cref="ApiKey"/> is mapped through an encrypted-column
/// <see cref="Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter{TModel,TProvider}"/>
/// in <c>OmnisRouterDbContext.OnModelCreating</c> so the plaintext is never round-tripped as text
/// outside the process; only the ciphertext (<c>keyVersion‖nonce‖tag‖ciphertext</c>) lands in the column.</summary>
public sealed class ProviderKey
{
    public required string Id { get; set; }
    public required string TenantId { get; set; }
    public Provider Provider { get; set; }
    public required string Label { get; set; }

    /// <summary>Plaintext key in-memory only; persisted encrypted via the EF <c>ValueConverter</c>.</summary>
    public required string ApiKey { get; set; }

    public int KeyVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}
