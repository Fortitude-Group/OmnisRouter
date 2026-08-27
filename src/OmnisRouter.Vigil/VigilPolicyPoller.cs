using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OmnisRouter.Vigil;

/// <summary>
/// Polls OmnisVigil for the current policy on a background timer and applies it to
/// <see cref="VigilPolicyState"/>. Never runs on the request path. Fail-safe: if a poll fails or the
/// collector is unreachable, the last known policy stays in force, so a runaway is still capped and
/// an org kill stays engaged through an outage.
/// </summary>
internal sealed class VigilPolicyPoller : BackgroundService
{
    private readonly IVigilPolicyClient _client;
    private readonly VigilPolicyState _state;
    private readonly OmnisVigilOptions _options;
    private readonly ILogger<VigilPolicyPoller> _logger;
    private string? _etag;

    public VigilPolicyPoller(
        IVigilPolicyClient client,
        VigilPolicyState state,
        OmnisVigilOptions options,
        ILogger<VigilPolicyPoller> logger)
    {
        _client = client;
        _state = state;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = TimeSpan.FromSeconds(Math.Max(1, _options.PolicyPollSeconds));
        using var timer = new PeriodicTimer(period);

        // Poll once immediately so a fresh process picks up caps and kill state without waiting a full period.
        await PollAsync(stoppingToken).ConfigureAwait(false);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await PollAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            var fetch = await _client.FetchAsync(_etag, cancellationToken).ConfigureAwait(false);
            _etag = fetch.ETag;
            if (fetch is { Changed: true, Policy: { } policy })
            {
                _state.Update(policy);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fail-safe: keep enforcing the last known policy.
            _logger.LogWarning(ex, "OmnisVigil policy poll failed; keeping the last known policy");
        }
    }
}
