namespace OmnisRouter.Store.Entities;

/// <summary>One row per tenant: the id of the last decision-log entry pushed to OmnisVigil.</summary>
public sealed class VigilPushCursor
{
    public required string TenantId { get; set; }
    public required string LastPushedId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
