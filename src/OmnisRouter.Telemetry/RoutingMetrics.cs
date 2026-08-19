using System.Diagnostics.Metrics;
using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;

namespace OmnisRouter.Telemetry;

/// <summary>
/// Typed routing metrics recorded under the shared <see cref="OmnisTelemetry.MeterName"/> meter.
/// Registered as a singleton via <see cref="OmnisTelemetryExtensions"/> — inject this rather than
/// creating instruments directly, so the app has one set of routing metrics.
/// </summary>
/// <remarks>
/// Tags are structural only (provider, model id, decision, cluster id). Never attach prompt text,
/// API keys, or any other request content/PII as a tag — that data must not become metric cardinality.
/// </remarks>
public sealed class RoutingMetrics
{
    private readonly Counter<long> _routedRequests;
    private readonly Histogram<double> _routingOverheadMs;

    public RoutingMetrics(Meter meter)
    {
        _routedRequests = meter.CreateCounter<long>(
            "omnisrouter.routing.requests",
            unit: "{request}",
            description: "Number of requests that went through the router, tagged by outcome.");

        _routingOverheadMs = meter.CreateHistogram<double>(
            "omnisrouter.routing.overhead",
            unit: "ms",
            description: "Time spent by the router making a routing decision, in milliseconds.");
    }

    /// <summary>Records one routed request. Call once per completed routing decision.</summary>
    public void RecordRoutedRequest(Provider provider, string modelId, RoutingDecisionKind decision, int clusterId)
    {
        _routedRequests.Add(
            1,
            new KeyValuePair<string, object?>("provider", ProviderTag(provider)),
            new KeyValuePair<string, object?>("model_id", modelId),
            new KeyValuePair<string, object?>("decision", DecisionTag(decision)),
            new KeyValuePair<string, object?>("cluster_id", clusterId));
    }

    /// <summary>Records the wall-clock overhead the router added for one request, in milliseconds.</summary>
    public void RecordRoutingOverhead(double milliseconds, Provider provider, string modelId, int clusterId)
    {
        _routingOverheadMs.Record(
            milliseconds,
            new KeyValuePair<string, object?>("provider", ProviderTag(provider)),
            new KeyValuePair<string, object?>("model_id", modelId),
            new KeyValuePair<string, object?>("cluster_id", clusterId));
    }

    private static string ProviderTag(Provider provider) => provider.ToString().ToLowerInvariant();

    private static string DecisionTag(RoutingDecisionKind decision) => decision.ToString().ToLowerInvariant();
}
