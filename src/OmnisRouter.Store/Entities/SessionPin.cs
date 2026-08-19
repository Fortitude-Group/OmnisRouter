using OmnisRouter.Core.Model;

namespace OmnisRouter.Store.Entities;

/// <summary>
/// Keeps upstream prompt caches warm across turns of a session. May be served from a durable table
/// (as here) or an in-memory/embedded cache; the data-model documents both as valid.
/// </summary>
public sealed class SessionPin
{
    /// <summary>Client <c>X-Omnis-Session-Id</c> or derived HMAC key; primary key.</summary>
    public required string SessionKey { get; set; }

    public required string TenantId { get; set; }
    public Provider PinnedProvider { get; set; }
    public required string PinnedModelId { get; set; }
    public int ClusterId { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
