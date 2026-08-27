using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Routing;

namespace OmnisRouter.Vigil;

/// <summary>
/// Background pusher: reads content-free decision-log rows past a persisted high-water mark and ships
/// them to the OmnisVigil collector in batches, stamping router_id. Durable at-least-once (the cursor
/// only advances on a 2xx, and Vigil dedupes on id) and fail-open (a failure never touches the request
/// path, the batch is simply retried next cycle).
/// </summary>
internal sealed class VigilReceiptPusher : BackgroundService
{
    private const string DefaultTenant = "default";

    private readonly IServiceScopeFactory _scopes;
    private readonly IHttpClientFactory _httpFactory;
    private readonly OmnisVigilOptions _options;
    private readonly RouterIdentity _identity;
    private readonly ILogger<VigilReceiptPusher> _logger;

    public VigilReceiptPusher(
        IServiceScopeFactory scopes,
        IHttpClientFactory httpFactory,
        OmnisVigilOptions options,
        RouterIdentity identity,
        ILogger<VigilReceiptPusher> logger)
    {
        _scopes = scopes;
        _httpFactory = httpFactory;
        _options = options;
        _identity = identity;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = TimeSpan.FromSeconds(Math.Max(1, _options.FlushSeconds));
        using var timer = new PeriodicTimer(period);
        do
        {
            try
            {
                await DrainAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Fail-open: never let a reporting error escape. Retry on the next tick.
                _logger.LogWarning(ex, "OmnisVigil receipt push cycle failed; retrying next tick");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>Pushes full batches until the log is drained or a batch is retained for retry.</summary>
    internal async Task DrainAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope = _scopes.CreateScope();
            var log = scope.ServiceProvider.GetRequiredService<IDecisionLog>();
            var cursor = scope.ServiceProvider.GetRequiredService<IVigilPushCursor>();

            var last = await cursor.GetAsync(DefaultTenant, ct).ConfigureAwait(false);
            var query = new DecisionQuery { TenantId = DefaultTenant, Cursor = last, Limit = _options.BatchSize };

            var batch = new List<DecisionLogEntry>();
            await foreach (var entry in log.ExportAsync(query, ct).ConfigureAwait(false))
            {
                batch.Add(entry);
            }

            if (batch.Count == 0)
            {
                return;
            }

            if (!await PushBatchAsync(batch, ct).ConfigureAwait(false))
            {
                return; // cursor not advanced -> same rows retried next cycle (at-least-once)
            }

            await cursor.SetAsync(DefaultTenant, batch[^1].Id, ct).ConfigureAwait(false);

            if (batch.Count < _options.BatchSize)
            {
                return; // drained
            }
        }
    }

    private async Task<bool> PushBatchAsync(IReadOnlyList<DecisionLogEntry> batch, CancellationToken ct)
    {
        var records = new JsonArray();
        foreach (var entry in batch)
        {
            records.Add(IngestRecordMapper.ToRecord(entry, _identity.RouterId));
        }

        var body = new JsonObject { ["schema_version"] = 1, ["records"] = records };

        var http = _httpFactory.CreateClient("OmnisVigil");
        using var request = new HttpRequestMessage(HttpMethod.Post, CombineUrl(_options.Endpoint!, "v1/ingest"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new("Bearer", _options.ProjectKey);

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        _logger.LogWarning(
            "OmnisVigil ingest returned {Status}; retaining {Count} receipts for retry",
            (int)response.StatusCode, batch.Count);
        return false;
    }

    private static string CombineUrl(string baseUrl, string path) =>
        $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
}
