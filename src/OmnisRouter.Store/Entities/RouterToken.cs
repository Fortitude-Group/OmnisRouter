namespace OmnisRouter.Store.Entities;

/// <summary>Client-facing auth token for calling the router. The raw token is never stored — only its hash.</summary>
public sealed class RouterToken
{
    public required string Id { get; set; }
    public required string TenantId { get; set; }
    public required string HashedToken { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
