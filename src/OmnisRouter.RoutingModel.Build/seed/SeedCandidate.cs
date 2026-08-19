namespace OmnisRouter.RoutingModel.Build.Seed;

/// <summary>
/// A candidate model entry mirrored from <c>config/models.yaml</c> (capabilities) and
/// <c>config/pricing/2026-08-15.yaml</c> (cost), used to populate the seed policy table.
/// This is a hand-mirrored snapshot for the seed generator only — the reproducible offline
/// build (T065/T066) reads <c>config/models.yaml</c> and the pinned pricing snapshot directly.
/// Keep this list in sync with those files until then.
/// </summary>
internal sealed record SeedCandidate(
    string Provider,
    string ModelId,
    double PredictedQuality,
    double InputPer1k,
    double OutputPer1k)
{
    /// <summary>Total per-1k-token cost (input + output). Used only to rank candidates cheap→strong.</summary>
    public double TotalPer1k => InputPer1k + OutputPer1k;
}
