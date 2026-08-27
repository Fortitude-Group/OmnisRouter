using Microsoft.EntityFrameworkCore;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Store.Entities;

namespace OmnisRouter.Store.Logging;

/// <summary>EF-backed <see cref="IVigilPushCursor"/>: one persisted high-water mark per tenant.</summary>
public sealed class SqlVigilPushCursor : IVigilPushCursor
{
    private readonly OmnisRouterDbContext _dbContext;

    public SqlVigilPushCursor(OmnisRouterDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string?> GetAsync(string tenantId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.VigilPushCursors
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);
        return row?.LastPushedId;
    }

    public async Task SetAsync(string tenantId, string lastPushedId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.VigilPushCursors
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            _dbContext.VigilPushCursors.Add(new VigilPushCursor
            {
                TenantId = tenantId,
                LastPushedId = lastPushedId,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            row.LastPushedId = lastPushedId;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
