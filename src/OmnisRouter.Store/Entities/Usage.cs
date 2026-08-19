using OmnisRouter.Core.Model;

namespace OmnisRouter.Store.Entities;

/// <summary>Aggregated spend/savings for the dashboard, one row per tenant/date/provider/model.</summary>
public sealed class Usage
{
    public required string Id { get; set; }
    public required string TenantId { get; set; }
    public DateOnly Date { get; set; }
    public Provider Provider { get; set; }
    public required string ModelId { get; set; }
    public long Requests { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal CostUsd { get; set; }

    /// <summary>Savings basis: what this usage would have cost on the configured strong-default model.</summary>
    public decimal CostVsBigUsd { get; set; }
}
