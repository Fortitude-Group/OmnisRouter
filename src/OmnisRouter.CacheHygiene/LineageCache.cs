namespace OmnisRouter.CacheHygiene;

/// <summary>
/// Holds the previous prefix per lineage, in memory only, long enough to diff the next request against
/// it (spec: keep nothing persistent). Bounded by entry count and age, LRU-evicted, thread-safe.
/// The lineage key is the caller's session identity, else a stable-structure hash (research D4).
/// </summary>
public sealed class LineageCache
{
    private readonly int _maxEntries;
    private readonly TimeSpan _maxAge;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    // key -> (prefix, node in LRU list). Front of the list = most recently used.
    private readonly Dictionary<string, LinkedListNode<Entry>> _map;
    private readonly LinkedList<Entry> _lru = new();

    public LineageCache(int maxEntries, TimeSpan maxAge, TimeProvider? clock = null)
    {
        _maxEntries = Math.Max(1, maxEntries);
        _maxAge = maxAge;
        _clock = clock ?? TimeProvider.System;
        _map = new Dictionary<string, LinkedListNode<Entry>>(StringComparer.Ordinal);
    }

    public int Count
    {
        get { lock (_gate) { return _map.Count; } }
    }

    /// <summary>The lineage's previous prefix, if present and not aged out.</summary>
    public bool TryGetPrevious(string lineageKey, out byte[] prefix)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(lineageKey, out var node))
            {
                if (IsExpired(node.Value.SeenUtc))
                {
                    Remove(node);
                }
                else
                {
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                    prefix = node.Value.Prefix;
                    return true;
                }
            }
        }

        prefix = [];
        return false;
    }

    /// <summary>Record the current prefix as this lineage's latest.</summary>
    public void Store(string lineageKey, byte[] prefix)
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            if (_map.TryGetValue(lineageKey, out var existing))
            {
                existing.Value = new Entry(lineageKey, prefix, now);
                _lru.Remove(existing);
                _lru.AddFirst(existing);
            }
            else
            {
                var node = _lru.AddFirst(new Entry(lineageKey, prefix, now));
                _map[lineageKey] = node;
            }

            EvictExpired(now);
            while (_map.Count > _maxEntries && _lru.Last is { } oldest)
            {
                Remove(oldest);
            }
        }
    }

    private bool IsExpired(DateTimeOffset seenUtc) => _clock.GetUtcNow() - seenUtc > _maxAge;

    private void EvictExpired(DateTimeOffset now)
    {
        var node = _lru.Last;
        while (node is not null && now - node.Value.SeenUtc > _maxAge)
        {
            var prev = node.Previous;
            Remove(node);
            node = prev;
        }
    }

    private void Remove(LinkedListNode<Entry> node)
    {
        _lru.Remove(node);
        _map.Remove(node.Value.Key);
    }

    private struct Entry(string key, byte[] prefix, DateTimeOffset seenUtc)
    {
        public string Key { get; } = key;
        public byte[] Prefix { get; } = prefix;
        public DateTimeOffset SeenUtc { get; } = seenUtc;
    }
}
