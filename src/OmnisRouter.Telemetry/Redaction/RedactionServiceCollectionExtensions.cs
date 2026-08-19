using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OmnisRouter.Telemetry.Redaction;

/// <summary>
/// DI wiring for the belt-and-suspenders redaction layer.
/// </summary>
public static class RedactionServiceCollectionExtensions
{
    /// <summary>
    /// Decorates every currently-registered <see cref="ILoggerProvider"/> with a
    /// <see cref="RedactingLoggerProvider"/>, so every logging sink the host writes through (console,
    /// OTLP exporter, etc.) has secret-shaped substrings scrubbed from formatted messages. Call this
    /// AFTER logging providers (e.g. <c>AddOmnisTelemetry</c>, <c>AddConsole</c>) have been
    /// registered — providers added afterward are not retroactively wrapped.
    /// </summary>
    public static IServiceCollection AddOmnisRedaction(this IServiceCollection services)
    {
        var providerDescriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(ILoggerProvider))
            .ToList();

        foreach (var descriptor in providerDescriptors)
        {
            services.Remove(descriptor);
            services.Add(ServiceDescriptor.Describe(
                typeof(ILoggerProvider),
                provider => new RedactingLoggerProvider((ILoggerProvider)CreateInstance(descriptor, provider)),
                descriptor.Lifetime));
        }

        return services;
    }

    private static object CreateInstance(ServiceDescriptor descriptor, IServiceProvider provider)
    {
        if (descriptor.ImplementationInstance is not null)
        {
            return descriptor.ImplementationInstance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return descriptor.ImplementationFactory(provider);
        }

        return ActivatorUtilities.GetServiceOrCreateInstance(provider, descriptor.ImplementationType!);
    }
}
