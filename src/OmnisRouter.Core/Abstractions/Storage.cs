using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;

namespace OmnisRouter.Core.Abstractions;

/// <summary>Append-only, content-free routing decision log with streaming export (FR-009).</summary>
public interface IDecisionLog
{
    Task AppendAsync(DecisionLogEntry entry, CancellationToken cancellationToken);

    IAsyncEnumerable<DecisionLogEntry> ExportAsync(DecisionQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Durable high-water mark for the OmnisVigil receipt pusher: the id of the last decision-log entry
/// successfully pushed. Persisting it lets the pusher resume after a restart without re-sending the
/// whole log (and Vigil dedupes on id anyway, so a resume is at-least-once, never lossy).
/// </summary>
public interface IVigilPushCursor
{
    /// <summary>The last pushed entry id for the tenant, or null if nothing has been pushed yet.</summary>
    Task<string?> GetAsync(string tenantId, CancellationToken cancellationToken);

    Task SetAsync(string tenantId, string lastPushedId, CancellationToken cancellationToken);
}

/// <summary>Cost estimation from the pinned, dated pricing snapshot (FR-016, Principle XII).</summary>
public interface IPricingBook
{
    /// <summary>Snapshot date (e.g. "2026-08-15"), surfaced in receipts for explainability.</summary>
    string SnapshotDate { get; }

    /// <summary>Date of the USD→GBP rate in the snapshot (cache-hygiene £ figures cite it). Defaults to empty for books without FX.</summary>
    string FxDate => string.Empty;

    /// <summary>The snapshot's USD→GBP rate. £ = USD × this. Defaults to 0 (no GBP path).</summary>
    decimal UsdGbp => 0m;

    decimal EstimateUsd(ModelRef model, int inputTokens, int outputTokens);

    /// <summary>
    /// The cache-write premium per token in USD for a model: (cache-write rate − cache-read rate) / 1000.
    /// This is the per-token cost of a cache miss over a hit — what an avoidable miss wastes and a fix
    /// saves (cache hygiene, FR-006/FR-011). Zero when the model has no separate cache rates or is unknown.
    /// Defaults to 0 for books that do not price cache economics.
    /// </summary>
    decimal CacheWritePremiumUsdPerToken(ModelRef model) => 0m;

    /// <summary>
    /// Actual cost from a completed request's full token accounting, including cache economics
    /// (cache read charged at the snapshot's cache-read rate, cache creation at the cache-write
    /// rate). Cache-heavy agent traffic makes this materially different from the input/output-only
    /// estimate, so it is the figure the savings ledger and the OmnisVigil receipt use.
    /// </summary>
    decimal EstimateUsd(ModelRef model, Usage usage);
}
