namespace OmnisRouter.CacheHygiene;

/// <summary>
/// The router's in-memory running total of cache hygiene, updated as each <see cref="CacheHygieneResult"/>
/// is produced. Process-lifetime (resets on restart); the "today" bucket resets at the local day
/// boundary. Thread-safe: the routed path records concurrently while the summary endpoint reads. Holds
/// no prompt bytes — only the scalar figures and the latest pricing stamp (Principle IV / content-free).
/// </summary>
public sealed class CacheHygieneTally
{
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Bucket _sinceStart = new();
    private Bucket _today = new();
    private DateOnly _todayDate;
    private PricingStamp? _pricing;

    public CacheHygieneTally(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _todayDate = LocalDate();
    }

    /// <summary>Fold one reportable (missed) result into both buckets. Off the hot path's critical work.</summary>
    public void Record(CacheHygieneResult result)
    {
        lock (_gate)
        {
            RollTodayIfNeeded();
            _sinceStart.Add(result);
            _today.Add(result);
            if (result.Pricing is { } p)
            {
                _pricing = p;
            }
        }
    }

    /// <summary>An immutable point-in-time view for the summary endpoint.</summary>
    public CacheHygieneSnapshot Snapshot(bool measurementEnabled)
    {
        lock (_gate)
        {
            RollTodayIfNeeded();
            return new CacheHygieneSnapshot(
                measurementEnabled,
                _sinceStart.ToPeriod(),
                _today.ToPeriod(),
                _pricing);
        }
    }

    private DateOnly LocalDate() => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

    private void RollTodayIfNeeded()
    {
        var now = LocalDate();
        if (now != _todayDate)
        {
            _today = new Bucket();
            _todayDate = now;
        }
    }

    private sealed class Bucket
    {
        private decimal _wasteGbp;
        private decimal _savedGbp;
        private long _recomputed;
        private long _saved;
        private int _misses;
        private int _recoveries;

        public void Add(CacheHygieneResult r)
        {
            _wasteGbp += r.WasteGbp;
            _savedGbp += r.SavedGbp;
            _recomputed += r.RecomputedTokens;
            _saved += r.SavedTokens;
            if (r.WasteGbp > 0m)
            {
                _misses++;      // avoidable miss (unavoidable causes price to 0)
            }

            if (r.SavedGbp > 0m)
            {
                _recoveries++;
            }
        }

        public CachePeriod ToPeriod() => new(_wasteGbp, _savedGbp, _recomputed, _saved, _misses, _recoveries);
    }
}

/// <summary>The figures for one period. All scalars; content-free.</summary>
public sealed record CachePeriod(
    decimal AvoidableWasteGbp,
    decimal RecoveredGbp,
    long RecomputedTokens,
    long SavedTokens,
    int MissCount,
    int RecoveryCount)
{
    public static readonly CachePeriod Empty = new(0m, 0m, 0, 0, 0, 0);
}

/// <summary>An immutable snapshot of the tally for serialisation.</summary>
public sealed record CacheHygieneSnapshot(
    bool MeasurementEnabled,
    CachePeriod SinceStart,
    CachePeriod Today,
    PricingStamp? Pricing);
