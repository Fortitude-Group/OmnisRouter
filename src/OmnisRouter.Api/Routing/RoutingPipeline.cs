using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Routing;

namespace OmnisRouter.Api.Routing;

/// <summary>Shared routing setup used by both the routed endpoint and the decision-only endpoint.</summary>
public static class RoutingPipeline
{
    /// <summary>
    /// Builds a routing context restricted to providers that are BOTH reachable (an upstream client is
    /// registered) AND usable (the tenant has a BYOK key configured) — the router never picks a model
    /// it can't actually call. Returns the provider→client map for dispatch. Throws 503 if nothing is
    /// routable.
    /// </summary>
    public static RoutingContext BuildContext(
        IEnumerable<IUpstreamClient> upstreams,
        RoutingDefaults defaults,
        string tenantId,
        IReadOnlySet<Provider> keyedProviders,
        out IReadOnlyDictionary<Provider, IUpstreamClient> upstreamByProvider)
    {
        var map = upstreams
            .GroupBy(u => u.Provider)
            .ToDictionary(g => g.Key, g => g.First());
        upstreamByProvider = map;

        var pool = defaults.CandidatePool
            .Where(m => map.ContainsKey(m.Provider) && keyedProviders.Contains(m.Provider))
            .ToList();
        if (pool.Count == 0)
        {
            throw new OmnisException(503, "no_routable_models",
                "No candidate models are both reachable (upstream client) and usable (a BYOK key is configured).");
        }

        var strongDefault = pool.Contains(defaults.StrongDefault) ? defaults.StrongDefault : pool[^1];
        return new RoutingContext { TenantId = tenantId, CandidatePool = pool, StrongDefault = strongDefault };
    }
}
