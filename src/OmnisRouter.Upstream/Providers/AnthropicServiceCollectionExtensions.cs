using Microsoft.Extensions.DependencyInjection;
using OmnisRouter.Core.Abstractions;

namespace OmnisRouter.Upstream.Providers;

public static class AnthropicServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AnthropicUpstreamClient"/> as <see cref="IUpstreamClient"/> behind a typed
    /// <see cref="HttpClient"/> with socket/DNS-friendly pooling and no client-level timeout (per-call
    /// cancellation drives the deadline instead — research.md R3).
    /// </summary>
    public static IServiceCollection AddOmnisAnthropicUpstream(
        this IServiceCollection services, Action<AnthropicUpstreamOptions>? configureOptions = null)
    {
        var options = new AnthropicUpstreamOptions();
        configureOptions?.Invoke(options);
        services.AddSingleton(options);

        services.AddHttpClient<IUpstreamClient, AnthropicUpstreamClient>(client =>
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
