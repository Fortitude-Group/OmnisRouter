using Microsoft.Extensions.DependencyInjection;
using OmnisRouter.Core.Abstractions;

namespace OmnisRouter.Store.Pricing;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IPricingBook"/>, loading the pinned dated snapshot from <c>config/pricing</c>.</summary>
    public static IServiceCollection AddOmnisPricing(
        this IServiceCollection services, Action<PricingBookOptions>? configure = null)
    {
        var options = new PricingBookOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IPricingBook, PricingBook>();

        return services;
    }
}
