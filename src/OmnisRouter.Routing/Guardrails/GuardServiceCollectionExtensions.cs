using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Routing.Pinning;

namespace OmnisRouter.Routing.Guardrails;

public static class GuardServiceCollectionExtensions
{
    /// <summary>
    /// Registers the capability guard and session pinner: <see cref="ICapabilityGuard"/> →
    /// <see cref="CapabilityGuard"/> and <see cref="ISessionPinner"/> → <see cref="SessionPinner"/>,
    /// both singletons. If <paramref name="configureSessionPinner"/> leaves
    /// <see cref="SessionPinnerOptions.ServerSecret"/> unset, a random secret is generated at
    /// startup — note this is not stable across process restarts (fine for v1; a derived,
    /// non-header session key simply stops matching pre-restart pins after a restart).
    /// </summary>
    public static IServiceCollection AddOmnisRoutingGuards(
        this IServiceCollection services,
        Action<SessionPinnerOptions>? configureSessionPinner = null)
    {
        services.AddSingleton<ICapabilityGuard, CapabilityGuard>();

        services.AddSingleton(_ =>
        {
            var options = new SessionPinnerOptions();
            configureSessionPinner?.Invoke(options);

            if (string.IsNullOrEmpty(options.ServerSecret))
            {
                options.ServerSecret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            }

            return options;
        });

        services.AddSingleton<ISessionPinner, SessionPinner>();
        return services;
    }
}
