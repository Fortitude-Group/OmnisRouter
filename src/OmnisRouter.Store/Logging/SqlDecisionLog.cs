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
        IQueryable<DecisionLogEntry> results = _dbContext.DecisionLogEntries
            .AsNoTracking()
            .Where(e => e.TenantId == query.TenantId);

        if (query.From is { } from)
        {
            results = results.Where(e => e.Timestamp >= from);
        }

        if (query.To is { } to)
        {
            results = results.Where(e => e.Timestamp <= to);
        }

        if (query.ClusterId is { } clusterId)
        {
            results = results.Where(e => e.ClusterId == clusterId);
        }

        if (query.Decision is { } decision)
        {
            results = results.Where(e => e.Decision == decision);
        }

        if (query.Provider is { } provider)
        {
            results = results.Where(e => e.ChosenProvider == provider);
        }

        if (!string.IsNullOrEmpty(query.Cursor))
        {
            // Cursor = the Id of the last entry seen (ordered by Timestamp then Id).
            var cursorEntry = await _dbContext.DecisionLogEntries
                .AsNoTracking()
                .Where(e => e.Id == query.Cursor)
                .Select(e => new { e.Timestamp, e.Id })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (cursorEntry is not null)
            {
                results = results.Where(e =>
                    e.Timestamp > cursorEntry.Timestamp ||
                    (e.Timestamp == cursorEntry.Timestamp && string.Compare(e.Id, cursorEntry.Id) > 0));
            }
        }

        results = results
            .OrderBy(e => e.Timestamp)
            .ThenBy(e => e.Id)
            .Take(query.Limit);

        await foreach (var entry in results.AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return entry;
        }
    }
}
