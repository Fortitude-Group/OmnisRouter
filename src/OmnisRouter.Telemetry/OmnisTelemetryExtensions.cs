using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OmnisRouter.Telemetry;

/// <summary>
/// Wires up OpenTelemetry tracing and metrics for OmnisRouter: an OTLP exporter, a resource
/// named <see cref="OmnisTelemetry.ServiceName"/>, and DI-registered <see cref="ActivitySource"/>
/// / <see cref="Meter"/> instances that other projects can inject to emit under the shared names.
/// </summary>
public static class OmnisTelemetryExtensions
{
    /// <summary>
    /// Configures OpenTelemetry tracing + metrics on <paramref name="builder"/>, reading the OTLP
    /// endpoint from configuration key <c>Otlp:Endpoint</c> (falling back to the standard
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> environment variable / SDK default when unset).
    /// </summary>
    public static IHostApplicationBuilder AddOmnisTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOmnisTelemetry(builder.Configuration);
        return builder;
    }

    /// <summary>
    /// Configures OpenTelemetry tracing + metrics directly on a service collection for hosts that
    /// don't use <see cref="IHostApplicationBuilder"/>.
    /// </summary>
    public static IServiceCollection AddOmnisTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var otlpEndpoint = configuration["Otlp:Endpoint"];

        services.AddSingleton(_ => new ActivitySource(OmnisTelemetry.ActivitySourceName));
        services.AddSingleton(_ => new Meter(OmnisTelemetry.MeterName));
        services.AddSingleton<RoutingMetrics>();

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName: OmnisTelemetry.ServiceName))
            .WithTracing(tracing => tracing
                .AddSource(OmnisTelemetry.ActivitySourceName)
                .AddOtlpExporter(otlp => ConfigureOtlpEndpoint(otlp, otlpEndpoint)))
            .WithMetrics(metrics => metrics
                .AddMeter(OmnisTelemetry.MeterName)
                .AddOtlpExporter(otlp => ConfigureOtlpEndpoint(otlp, otlpEndpoint)));

        return services;
    }

    private static void ConfigureOtlpEndpoint(OtlpExporterOptions options, string? endpoint)
    {
        // Leave the exporter's own defaults (and OTEL_EXPORTER_OTLP_ENDPOINT) in place when
        // no explicit endpoint is configured, per standard OTel env-var behavior.
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            options.Endpoint = new Uri(endpoint);
        }
    }
}
