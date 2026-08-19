using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Routing;

namespace OmnisRouter.Store.Logging;

/// <summary>EF-backed <see cref="IDecisionLog"/>: append-only, content-free, streaming export (FR-009).</summary>
public sealed class SqlDecisionLog : IDecisionLog
{
    private readonly OmnisRouterDbContext _dbContext;

    public SqlDecisionLog(OmnisRouterDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AppendAsync(DecisionLogEntry entry, CancellationToken cancellationToken)
    {
        _dbContext.DecisionLogEntries.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<DecisionLogEntry> ExportAsync(
        DecisionQuery query, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Apply the filters that translate cleanly across providers in SQL. Timestamp
        // ordering/comparison is done client-side because SQLite cannot ORDER BY a DateTimeOffset.
        // NOTE (v1): this materializes the tenant's filtered rows; the scalable fix is an indexed
        // epoch-ms ordering column — tracked as a fast-follow.
        IQueryable<DecisionLogEntry> filtered = _dbContext.DecisionLogEntries
            .AsNoTracking()
            .Where(e => e.TenantId == query.TenantId);

        if (query.ClusterId is { } clusterId)
        {
            filtered = filtered.Where(e => e.ClusterId == clusterId);
        }

        if (query.Decision is { } decision)
        {
            filtered = filtered.Where(e => e.Decision == decision);
        }

        if (query.Provider is { } provider)
        {
            filtered = filtered.Where(e => e.ChosenProvider == provider);
        }

        var rows = await filtered.ToListAsync(cancellationToken).ConfigureAwait(false);

        IEnumerable<DecisionLogEntry> seq = rows;
        if (query.From is { } from)
        {
            seq = seq.Where(e => e.Timestamp >= from);
        }

        if (query.To is { } to)
        {
            seq = seq.Where(e => e.Timestamp <= to);
        }

        seq = seq.OrderBy(e => e.Timestamp).ThenBy(e => e.Id, StringComparer.Ordinal);

        if (!string.IsNullOrEmpty(query.Cursor))
        {
            var cursor = rows.FirstOrDefault(e => e.Id == query.Cursor);
            if (cursor is not null)
            {
                seq = seq.Where(e =>
                    e.Timestamp > cursor.Timestamp ||
                    (e.Timestamp == cursor.Timestamp && string.CompareOrdinal(e.Id, cursor.Id) > 0));
            }
        }

        foreach (var entry in seq.Take(query.Limit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
        }
    }
}
