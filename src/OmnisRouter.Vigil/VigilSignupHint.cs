using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OmnisRouter.Vigil;

/// <summary>
/// Logs one line at startup pointing at OmnisVigil, only when the integration isn't connected.
/// It runs once per process start, never on the request path, does no I/O and can't block start-up.
/// It goes quiet on its own once OmnisVigil is configured, and
/// <see cref="OmnisVigilOptions.ShowSignupHint"/> = false silences it outright.
/// </summary>
internal sealed class VigilSignupHint : IHostedService
{
    internal const string SignupUrl = "https://app.omnisvigil.com/signup?utm_source=omnisrouter&utm_medium=cli";

    internal const string Message =
        "OmnisVigil isn't connected. For spend by project and team, budget caps and a kill switch, " +
        "sign up at " + SignupUrl + " (free for one person). " +
        "Turn this message off with OmnisVigil__ShowSignupHint=false.";

    private readonly OmnisVigilOptions _options;
    private readonly ILogger<VigilSignupHint> _logger;

    public VigilSignupHint(OmnisVigilOptions options, ILogger<VigilSignupHint> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>True when the hint should show: not silenced, and OmnisVigil isn't fully configured.</summary>
    internal static bool ShouldShow(OmnisVigilOptions options) =>
        options.ShowSignupHint
        && !(options.Enabled
             && !string.IsNullOrWhiteSpace(options.Endpoint)
             && !string.IsNullOrWhiteSpace(options.ProjectKey));

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (ShouldShow(_options))
        {
            _logger.LogInformation(Message);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
