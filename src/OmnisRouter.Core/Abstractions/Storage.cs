using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;

namespace OmnisRouter.Core.Abstractions;

/// <summary>Append-only, content-free routing decision log with streaming export (FR-009).</summary>
public interface IDecisionLog
{
    Task AppendAsync(DecisionLogEntry entry, CancellationToken cancellationToken);

    IAsyncEnumerable<DecisionLogEntry> ExportAsync(DecisionQuery query, CancellationToken cancellationToken);
}

/// <summary>Cost estimation from the pinned, dated pricing snapshot (FR-016, Principle XII).</summary>
public interface IPricingBook
{
    /// <summary>Snapshot date (e.g. "2026-08-15"), surfaced in receipts for explainability.</summary>
    string SnapshotDate { get; }

    decimal EstimateUsd(ModelRef model, int inputTokens, int outputTokens);

    /// <summary>
    /// Actual cost from a completed request's full token accounting, including cache economics
    /// (cache read charged at the snapshot's cache-read rate, cache creation at the cache-write
    /// rate). Cache-heavy agent traffic makes this materially different from the input/output-only
    /// estimate, so it is the figure the savings ledger and the OmnisVigil receipt use.
    /// </summary>
    decimal EstimateUsd(ModelRef model, Usage usage);
}
