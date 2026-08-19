namespace OmnisRouter.Store.Entities;

/// <summary>One row per router instance/tenant.</summary>
public sealed class Install
{
    public required string Id { get; set; }
    public required string TenantId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>JSON blob: confidence floor override, strong-default model, cost_tier default.</summary>
    public string? Settings { get; set; }
}
