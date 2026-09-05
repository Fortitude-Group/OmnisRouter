using System.Text.Json.Nodes;

namespace OmnisRouter.Collect;

/// <summary>Number of records the sink newly accepted versus those already present upstream.</summary>
public readonly record struct PostResult(int Accepted, int Duplicates);

/// <summary>
/// The seam that keeps the network out of <see cref="CollectEngine"/>. The real implementation posts
/// to OmnisVigil <c>/v1/ingest</c>; tests use an in-memory fake; dry-run uses <see cref="NullReceiptSink"/>.
/// </summary>
public interface IReceiptSink
{
    Task<PostResult> PostAsync(IReadOnlyList<JsonObject> batch, CancellationToken ct);
}

/// <summary>Dry-run sink: posts nothing, counts every record as newly accepted (matches the CLI's --dry-run).</summary>
public sealed class NullReceiptSink : IReceiptSink
{
    public Task<PostResult> PostAsync(IReadOnlyList<JsonObject> batch, CancellationToken ct)
        => Task.FromResult(new PostResult(batch.Count, 0));
}
