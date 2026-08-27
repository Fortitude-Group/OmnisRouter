using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OmnisRouter.Vigil;

/// <summary>Registers the OmnisVigil integration (options, router identity, and later the receipt sink and policy poller).</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOmnisVigil(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new OmnisVigilOptions();
        configuration.GetSection(OmnisVigilOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        // router_id: an explicit config value wins, otherwise the machine name, which is stable for
        // the lifetime of a deployment and needs no extra configuration to get sensible attribution.
        var routerId = string.IsNullOrWhiteSpace(options.RouterId)
            ? Environment.MachineName
            : options.RouterId!;
        services.AddSingleton(new RouterIdentity(routerId));

        return services;
    }
}
