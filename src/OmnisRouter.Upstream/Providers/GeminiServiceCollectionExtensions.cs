using Microsoft.Extensions.DependencyInjection;
using OmnisRouter.Core.Abstractions;

namespace OmnisRouter.Upstream.Providers;

public static class GeminiServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="GeminiUpstreamClient"/> as <see cref="IUpstreamClient"/> behind a typed
    /// <see cref="HttpClient"/> with socket/DNS-friendly pooling and no client-level timeout (per-call
    /// cancellation drives the deadline instead — research.md R3).
    /// </summary>
    public static IServiceCollection AddOmnisGeminiUpstream(
        this IServiceCollection services, Action<GeminiUpstreamOptions>? configureOptions = null)
    {
        var options = new GeminiUpstreamOptions();
        configureOptions?.Invoke(options);
        services.AddSingleton(options);

        services.AddHttpClient<IUpstreamClient, GeminiUpstreamClient>(client =>
            {
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 100,
            });

        return services;
    }
}
