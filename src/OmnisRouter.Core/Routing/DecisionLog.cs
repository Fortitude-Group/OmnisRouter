using OmnisRouter.Core.Model;

namespace OmnisRouter.Core.Routing;

/// <summary>Terminal outcome of a routed request, recorded in the decision log.</summary>
public enum RequestOutcome
{
    Success,
    UpstreamError,
    Cancelled,
}

/// <summary>
/// One persisted routing-decision record. Content-free by construction: it stores a non-reversible
/// <see cref="RequestHash"/>, never prompt or response text, and never keys (FR-014). Also used as the
/// EF entity by the store (which references Core), so Core stays a leaf project.
/// </summary>
public sealed record DecisionLogEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("n");
    public string TenantId { get; init; } = "default";
    public DateTimeOffset Timestamp { get; init; }
    public string? SessionId { get; init; }

    /// <summary>Non-reversible hash of the request; NOT prompt content.</summary>
    public required string RequestHash { get; init; }
    public ClientFormat ClientFormat { get; init; }
    public int ClusterId { get; init; }

    public Provider ChosenProvider { get; init; }
    public required string ChosenModelId { get; init; }

    public double Confidence { get; init; }
    public double Top1Sim { get; init; }
    public double Top2Sim { get; init; }
    public double Margin { get; init; }

    public RoutingDecisionKind Decision { get; init; }
    public RoutingReason Reason { get; init; }
    public required string PolicyVersion { get; init; }

    public decimal EstCostUsd { get; init; }
    public decimal EstCostDeltaVsBigUsd { get; init; }
    public bool SessionPinApplied { get; init; }

    public RequestOutcome Outcome { get; init; } = RequestOutcome.Success;
    public int LatencyMs { get; init; }
}

/// <summary>Filter for exporting the decision log.</summary>
public sealed record DecisionQuery
{
    public string TenantId { get; init; } = "default";
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public int? ClusterId { get; init; }
    public RoutingDecisionKind? Decision { get; init; }
    public Provider? Provider { get; init; }
    public int Limit { get; init; } = 10_000;
    public string? Cursor { get; init; }
}
