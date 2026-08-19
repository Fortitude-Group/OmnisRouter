using Microsoft.EntityFrameworkCore;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Store;

namespace OmnisRouter.Api.Routing;

/// <summary>Resolves the operator's BYOK credential for a chosen model's provider (decrypted at read).</summary>
public interface IProviderCredentialResolver
{
    Task<ProviderCredential> ResolveAsync(string tenantId, Provider provider, CancellationToken cancellationToken);
}

/// <summary>
/// Reads the operator-supplied provider key from the store (the store's value converter decrypts
/// <c>ApiKey</c> transparently). Throws a non-leaking 400 when no key exists — never falls back to
/// an unauthorized key (spec edge case + FR-013).
/// </summary>
public sealed class ProviderCredentialResolver(OmnisRouterDbContext db) : IProviderCredentialResolver
{
    public async Task<ProviderCredential> ResolveAsync(string tenantId, Provider provider, CancellationToken cancellationToken)
    {
        // Order client-side: SQLite can't ORDER BY a DateTimeOffset column in SQL.
        var keys = await db.ProviderKeys
            .Where(k => k.TenantId == tenantId && k.Provider == provider)
            .ToListAsync(cancellationToken);
        var key = keys.OrderByDescending(k => k.CreatedAt).FirstOrDefault();

        if (key is null)
        {
            throw new OmnisException(400, "no_provider_key",
                $"No API key configured for provider '{provider}'. Add one before routing to it.");
        }

        return new ProviderCredential(provider, key.ApiKey);
    }
}
