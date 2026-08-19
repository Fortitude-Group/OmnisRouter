using OmnisRouter.Core.Model;

namespace OmnisRouter.Routing.Model;

/// <summary>One candidate model in a cluster's policy row (ranked cheapest→strongest).</summary>
public sealed record PolicyCandidate(Provider Provider, string ModelId, double PredictedQuality, int RankByCost);

/// <summary>A cluster's ranked candidate list. Index 0 (rank 1) is the cheapest-capable choice.</summary>
public sealed record ClusterPolicy(int ClusterId, IReadOnlyList<PolicyCandidate> Candidates);

/// <summary>
/// The loaded, versioned routing model: unit-length centroids + the per-cluster policy table.
/// Immutable; loaded once at startup by <see cref="RoutingModelLoader"/> and shared across requests.
/// </summary>
public sealed class RoutingModel
{
    public required string PolicyVersion { get; init; }
    public required int K { get; init; }
    public required int Dim { get; init; }

    /// <summary>k × dim unit-length centroids (row-major).</summary>
    public required float[][] Centroids { get; init; }

    /// <summary>Policy rows indexed by cluster id (0..k-1).</summary>
    public required IReadOnlyList<ClusterPolicy> Clusters { get; init; }
}
