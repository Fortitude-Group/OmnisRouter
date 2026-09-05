namespace OmnisRouter.Collect;

/// <summary>The lifecycle state the tray icon and popup reflect.</summary>
public enum CollectState
{
    /// <summary>Configured, not yet started.</summary>
    Idle,

    /// <summary>Backfilling history.</summary>
    Backfilling,

    /// <summary>Tailing for new usage.</summary>
    Watching,

    /// <summary>Paused by the user.</summary>
    Paused,

    /// <summary>The last post failed; the loop keeps retrying.</summary>
    Error,

    /// <summary>The engine has stopped (process exiting).</summary>
    Stopped,
}

/// <summary>
/// Immutable snapshot of what the collect engine is doing. Both the console renderer and the tray
/// read this; the engine publishes a fresh instance on every change. See
/// <c>specs/002-collect-tray-app/data-model.md</c>.
/// </summary>
public sealed record CollectionStatus
{
    /// <summary>Current lifecycle state.</summary>
    public CollectState State { get; init; } = CollectState.Idle;

    /// <summary>Transcript entries scanned this session (dedup denominator).</summary>
    public long Scanned { get; init; }

    /// <summary>Unique entries seen this session (before upstream de-dup).</summary>
    public long Unique { get; init; }

    /// <summary>Receipts newly accepted upstream this session (the old "session total").</summary>
    public int SessionReceipts { get; init; }

    /// <summary>Receipts reported already-present upstream this session.</summary>
    public int Duplicates { get; init; }

    /// <summary>Receipts accepted since local midnight (resets at the day boundary).</summary>
    public int TodayReceipts { get; init; }

    /// <summary>Tokens priced since local midnight.</summary>
    public long TodayTokens { get; init; }

    /// <summary>When the last batch was accepted; null before the first.</summary>
    public DateTimeOffset? LastPostUtc { get; init; }

    /// <summary>Message of the most recent failed tick; cleared on the next success.</summary>
    public string? LastError { get; init; }

    /// <summary>When <see cref="LastError"/> was recorded.</summary>
    public DateTimeOffset? LastErrorUtc { get; init; }

    /// <summary>Target base URL, for display.</summary>
    public string Endpoint { get; init; } = "";
}

/// <summary>What the engine just did. The console renderer switches on <see cref="Signal"/>; the tray reads <see cref="Status"/>.</summary>
public enum CollectSignal
{
    /// <summary>A backfill batch flushed (drives the CLI progress line).</summary>
    BackfillProgress,

    /// <summary>Backfill finished (drives the CLI summary).</summary>
    BackfillComplete,

    /// <summary>The watch loop has started.</summary>
    WatchStarted,

    /// <summary>A watch tick posted new receipts (<see cref="CollectEvent.Added"/> &gt; 0).</summary>
    WatchTick,

    /// <summary>A watch tick failed and will retry.</summary>
    TickFailed,

    /// <summary>Collection paused.</summary>
    Paused,

    /// <summary>Collection resumed.</summary>
    Resumed,

    /// <summary>The engine stopped.</summary>
    Stopped,
}

/// <summary>An engine event: the signal, the current snapshot, and any signal-specific extras.</summary>
public sealed record CollectEvent(CollectSignal Signal, CollectionStatus Status, int Added = 0, string? Error = null);
