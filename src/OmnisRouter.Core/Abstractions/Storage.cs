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
}
