using System.Text.Json.Nodes;

namespace OmnisRouter.Collect;

/// <summary>
/// The headless collect engine: reads Claude Code transcripts, prices the tokens, and posts
/// content-free receipts through an <see cref="IReceiptSink"/>. It backfills history, then (with
/// <c>Watch</c>) tails for new usage, de-duplicating by message id so nothing is double-counted.
///
/// It never touches the console. It publishes an observable <see cref="Status"/> and raises
/// <see cref="Emitted"/> for each meaningful step; the CLI renderer turns those into today's exact
/// console output, and the tray binds its icon and popup to the same status. This is the one place
/// the CLI and the tray share behaviour (FR-021), and the receipts it posts are identical to the
/// old CLI's (FR-022).
/// </summary>
public sealed class CollectEngine
{
    private readonly CollectEngineOptions _o;
    private readonly IReceiptSink _sink;
    private readonly IClock _clock;
    private readonly ICollectLog _log;

    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<JsonObject> _batch;
    private readonly List<string> _batchIds;   // ids parallel to _batch, for failure rollback

    private long _scanned;
    private long _unique;
    private int _accepted;
    private int _duplicates;
    private int _todayReceipts;
    private long _todayTokens;
    private long _pendingTokens;
    private DateOnly _todayDate;
    private DateTimeOffset? _lastPostUtc;
    private string? _lastError;
    private DateTimeOffset? _lastErrorUtc;
    private CollectState _state = CollectState.Idle;
    private volatile bool _paused;

    public CollectEngine(CollectEngineOptions options, IReceiptSink sink, string endpoint, IClock? clock = null, ICollectLog? log = null)
    {
        _o = options;
        _sink = sink;
        Endpoint = endpoint;
        _clock = clock ?? new SystemClock();
        _log = log ?? NullCollectLog.Instance;
        _batch = new List<JsonObject>(options.Batch);
        _batchIds = new List<string>(options.Batch);
        _todayDate = DateOnly.FromDateTime(_clock.LocalNow.DateTime);
        Status = Snapshot();
    }

    /// <summary>Target base URL, for display.</summary>
    public string Endpoint { get; }

    /// <summary>Current snapshot. Also delivered on every <see cref="Emitted"/> event.</summary>
    public CollectionStatus Status { get; private set; }

    /// <summary>Raised for each engine step. The renderer/tray subscribe; the engine never blocks on handlers.</summary>
    public event Action<CollectEvent>? Emitted;

    /// <summary>Pause collection. Idempotent. Usage written while paused is caught up on resume.</summary>
    public void Pause()
    {
        if (_paused)
        {
            return;
        }

        _paused = true;
        _state = CollectState.Paused;
        _log.Info("paused");
        Emit(CollectSignal.Paused);
    }

    /// <summary>Resume collection. Idempotent.</summary>
    public void Resume()
    {
        if (!_paused)
        {
            return;
        }

        _paused = false;
        _state = CollectState.Watching;
        _log.Info("resumed");
        Emit(CollectSignal.Resumed);
    }

    /// <summary>Backfill, then (if configured) watch until cancelled. Never writes to the console.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        _state = CollectState.Backfilling;
        _log.Info($"backfill starting: root={_o.Root} watch={_o.Watch}");
        Publish();

        await ProcessAsync(TranscriptReader.Read(_o.Root, _o.Since), ct).ConfigureAwait(false);
        await FlushAsync(ct, duringBackfill: true).ConfigureAwait(false);

        _log.Info($"backfill complete: unique={_unique} accepted={_accepted} duplicates={_duplicates}");
        Emit(CollectSignal.BackfillComplete);

        if (!_o.Watch)
        {
            _state = CollectState.Stopped;   // one-shot run finished; no watch line is emitted
            Publish();
            return;
        }

        _state = _paused ? CollectState.Paused : CollectState.Watching;
        Emit(CollectSignal.WatchStarted);
        await WatchAsync(ct).ConfigureAwait(false);

        _state = CollectState.Stopped;
        _log.Info("stopped watching");
        Emit(CollectSignal.Stopped);
    }

    private async Task WatchAsync(CancellationToken ct)
    {
        // Start slightly in the past so the first tick re-checks files touched around startup.
        var lastScan = _clock.UtcNow.AddSeconds(-_o.Interval);

        while (!ct.IsCancellationRequested)
        {
            // Yield so the loop is always asynchronous even at a zero interval (tests), and so a
            // caller awaiting RunAsync gets its task back promptly.
            await Task.Yield();

            var tickStart = _clock.UtcNow;

            if (!_paused)
            {
                var before = _accepted;
                try
                {
                    foreach (var file in TranscriptReader.EnumerateFiles(_o.Root))
                    {
                        if (SafeLastWrite(file) >= lastScan.AddSeconds(-5))
                        {
                            await ProcessAsync(TranscriptReader.ReadFile(file, _o.Since), ct).ConfigureAwait(false);
                        }
                    }

                    await FlushAsync(ct, duringBackfill: false).ConfigureAwait(false);
                    lastScan = tickStart;

                    if (_lastError is not null)
                    {
                        _lastError = null;   // a clean tick clears the error banner
                        _lastErrorUtc = null;
                    }

                    if (_state == CollectState.Error)
                    {
                        _state = CollectState.Watching;
                    }

                    var added = _accepted - before;
                    Publish();
                    if (added > 0)
                    {
                        Emit(CollectSignal.WatchTick, added: added);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    RollbackBatch();
                    _lastError = ex.Message.Split('\n')[0];
                    _lastErrorUtc = _clock.UtcNow;
                    _state = CollectState.Error;
                    _log.Error($"tick failed: {_lastError}");
                    Emit(CollectSignal.TickFailed, error: _lastError);
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_o.Interval), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProcessAsync(IEnumerable<UsageEntry> entries, CancellationToken ct)
    {
        foreach (var e in entries)
        {
            _scanned++;
            if (!_seen.Add(e.Id))
            {
                continue;
            }

            _unique++;
            var cost = ModelPrices.CostUsd(e.Model, e.InputTokens, e.OutputTokens, e.CacheReadTokens, e.CacheCreationTokens);
            _batch.Add(ReceiptRecord.From(e, cost));
            _batchIds.Add(e.Id);
            _pendingTokens += e.InputTokens + e.OutputTokens + e.CacheReadTokens + e.CacheCreationTokens;

            if (_batch.Count >= _o.Batch)
            {
                await FlushAsync(ct, duringBackfill: true).ConfigureAwait(false);
            }
        }
    }

    private async Task FlushAsync(CancellationToken ct, bool duringBackfill)
    {
        if (_batch.Count == 0)
        {
            return;
        }

        RollToday();
        var (a, d) = await _sink.PostAsync(_batch, ct).ConfigureAwait(false);
        _accepted += a;
        _duplicates += d;
        _todayReceipts += a;
        _todayTokens += _pendingTokens;
        _lastPostUtc = _clock.UtcNow;

        _pendingTokens = 0;
        _batch.Clear();
        _batchIds.Clear();
        Publish();

        if (duringBackfill)
        {
            Emit(CollectSignal.BackfillProgress);
        }
    }

    // A failed tick must not "consume" its entries: return their ids to the unseen set so the next
    // tick re-posts them. This is the current CLI's rollback behaviour, preserved (FR-022).
    private void RollbackBatch()
    {
        foreach (var id in _batchIds)
        {
            _seen.Remove(id);
        }

        _batch.Clear();
        _batchIds.Clear();
        _pendingTokens = 0;
    }

    private void RollToday()
    {
        var today = DateOnly.FromDateTime(_clock.LocalNow.DateTime);
        if (today != _todayDate)
        {
            _todayDate = today;
            _todayReceipts = 0;
            _todayTokens = 0;
        }
    }

    private static DateTime SafeLastWrite(string file)
    {
        try
        {
            return File.GetLastWriteTimeUtc(file);
        }
        catch (IOException)
        {
            return DateTime.MinValue;
        }
    }

    private CollectionStatus Snapshot() => new()
    {
        State = _state,
        Scanned = _scanned,
        Unique = _unique,
        SessionReceipts = _accepted,
        Duplicates = _duplicates,
        TodayReceipts = _todayReceipts,
        TodayTokens = _todayTokens,
        LastPostUtc = _lastPostUtc,
        LastError = _lastError,
        LastErrorUtc = _lastErrorUtc,
        Endpoint = Endpoint,
    };

    private void Publish() => Status = Snapshot();

    private void Emit(CollectSignal signal, int added = 0, string? error = null)
    {
        Publish();
        Emitted?.Invoke(new CollectEvent(signal, Status, added, error));
    }
}
