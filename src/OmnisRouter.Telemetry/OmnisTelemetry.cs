namespace OmnisRouter.Telemetry;

/// <summary>
/// Shared identifiers for OmnisRouter's OpenTelemetry signals. Other projects emit traces and
/// metrics under these names (e.g. <c>new ActivitySource(OmnisTelemetry.ActivitySourceName)</c>)
/// so everything lands under one resource in the collector.
/// </summary>
public static class OmnisTelemetry
{
    /// <summary>The <c>service.name</c> resource attribute for every OmnisRouter process.</summary>
    public const string ServiceName = "omnisrouter";

    /// <summary>Name of the shared <see cref="System.Diagnostics.ActivitySource"/> for tracing.</summary>
    public const string ActivitySourceName = "OmnisRouter";

    /// <summary>Name of the shared <see cref="System.Diagnostics.Metrics.Meter"/> for metrics.</summary>
    public const string MeterName = "OmnisRouter";
}
