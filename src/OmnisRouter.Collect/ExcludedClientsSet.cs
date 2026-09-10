namespace OmnisRouter.Collect;

/// <summary>
/// The set of sources excluded from collection (see <see cref="CollectSource"/>), shared live between
/// the tray, which updates it as clients connect to or disconnect from the local router proxy, and the
/// collect engine, which reads it on each backfill and tick (US4). Those happen on different threads
/// (the UI thread and the engine's watch loop), so access is synchronised.
/// </summary>
public sealed class ExcludedClientsSet
{
    private readonly HashSet<string> _set = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Whether the named source is currently excluded.</summary>
    public bool Contains(string name)
    {
        lock (_gate)
        {
            return _set.Contains(name);
        }
    }

    /// <summary>Replace the excluded set with exactly <paramref name="names"/>.</summary>
    public void Set(IEnumerable<string> names)
    {
        lock (_gate)
        {
            _set.Clear();
            foreach (var name in names)
            {
                _set.Add(name);
            }
        }
    }
}
