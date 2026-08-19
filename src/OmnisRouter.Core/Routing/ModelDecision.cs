using OmnisRouter.Core.Model;

namespace OmnisRouter.Core.Routing;

/// <summary>Whether the router picked the cheapest-capable candidate or escalated.</summary>
public enum RoutingDecisionKind
{
    Routed,
    Escalated,
}

/// <summary>Why a particular model was chosen.</summary>
public enum RoutingReason
{
    CheapestCapable,
    ConfidenceBelowFloor,
    LowConfidenceCluster,
    CapabilityGuardrail,
    SessionPinned,
}

/// <summary>Why session pinning did or did not apply.</summary>
public enum SessionPinReason
{
    WarmCache,
    ClusterChangedUnpinned,
}

/// <summary>A candidate that was considered, with predicted quality and cost delta vs the chosen model.</summary>
public sealed record Alternative(ModelRef Model, double PredictedQuality, decimal EstCostUsd, decimal EstCostDeltaUsd);

/// <summary>
/// The routing decision for one request. Surfaced (compactly) via response headers, returned in full
/// by <c>POST /v1/route</c>, and persisted content-free to the decision log. Matches
/// contracts/routing-receipt.schema.json — the stable, versioned public receipt contract.
/// </summary>
public sealed record ModelDecision
{
    public required ModelRef Chosen { get; init; }
    public required string PolicyVersion { get; init; }
    public int ClusterId { get; init; }

    public double Confidence { get; init; }
    public double ConfidenceFloor { get; init; }
    public double Top1CosineSim { get; init; }
    public double Top2CosineSim { get; init; }
    public double Margin => Top1CosineSim - Top2CosineSim;

    public RoutingDecisionKind Decision { get; init; } = RoutingDecisionKind.Routed;
    public RoutingReason Reason { get; init; } = RoutingReason.CheapestCapable;

    public IReadOnlyList<Alternative> Alternatives { get; init; } = [];

    public decimal EstCostUsd { get; init; }
    public decimal EstCostDeltaVsBigUsd { get; init; }

    public bool SessionPinApplied { get; init; }
    public SessionPinReason? SessionPinReason { get; init; }

    /// <summary>Date of the pinned pricing snapshot used for cost math.</summary>
    public string? PricingSnapshotDate { get; init; }

    /// <summary>Non-fatal capability degradation surfaced to the caller (e.g. "image_detail_dropped").</summary>
    public string? CapabilityNotice { get; init; }
}
