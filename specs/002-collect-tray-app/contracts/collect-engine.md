# Contract: CollectEngine + IReceiptSink + CollectionStatus

The public surface of `OmnisRouter.Collect`. This is what the CLI renderer and the tray both consume. Kept intentional and documented (Principle II). Signatures are indicative, not final code.

## IReceiptSink

The seam that removes the network from the engine.

```csharp
public interface IReceiptSink
{
    // Post a batch; return how many were newly accepted vs already-present (deduped upstream).
    Task<PostResult> PostAsync(IReadOnlyList<ReceiptRecord> batch, CancellationToken ct);
}

public readonly record struct PostResult(int Accepted, int Duplicates);
```

- **Default implementation** `HttpReceiptSink` — the current `PostBatchAsync` logic verbatim (POST `/v1/ingest`, Bearer key, `{schema_version:1, records:[…]}`), so wire behaviour is unchanged.
- **Test implementation** — an in-memory fake that records batches and can be told to fail, for idempotency/error-path tests.

## CollectEngine

```csharp
public sealed class CollectEngine
{
    public CollectEngine(CollectEngineOptions options, IReceiptSink sink, IClock clock, ICollectLog log);

    // Current snapshot + change notifications.
    public CollectionStatus Status { get; }
    public event Action<CollectionStatus>? StatusChanged;

    // Run backfill then (if watch) the tail loop until cancelled. Never writes to Console.
    public Task RunAsync(CancellationToken ct);

    // Tray controls. Idempotent.
    public void Pause();
    public void Resume();
}
```

**Guarantees**:
- `RunAsync` performs the backfill (respecting the `Since`/`--all` window), publishes `Backfilling` with progress, then transitions to `Watching` when `watch` is set (else completes).
- De-duplication by message id is identical to today: an id posted once is never posted again within the process, and a failed tick returns its ids to the unseen set so they retry (no double count, no drop — FR-022).
- A post failure sets `State = Error` + `LastError`, keeps ticking, and clears the error on the next success.
- All time/date reads go through `IClock`; the "today" rollover is therefore deterministic under test.
- No member writes to `Console`. Diagnostics go to `ICollectLog`.

## CollectionStatus

Immutable record; see data-model.md for fields and transitions. Republished on every change; consumers read `Status` or subscribe to `StatusChanged`.

## Renderers (consumers, not part of the library surface)

- **`ConsoleCollectRunner`** (in `OmnisRouter.Api`): subscribes to `StatusChanged` and reproduces today's exact CLI output, including the backfill progress line and the `[HH:mm:ss] +N new receipt(s) (session total …)` watch line. This preserves FR-020 output byte-for-byte.
- **Tray** (in `OmnisRouter.Tray`): binds icon/tooltip/popup to `Status`. No collection logic.

## Backwards compatibility

- The `omnisrouter collect` command and every flag keep working (FR-020); `Program.cs` still branches on `args[0] == "collect"` and now calls `ConsoleCollectRunner` which drives `CollectEngine`.
- Nothing in `OmnisRouter.Api`'s HTTP surface changes.
