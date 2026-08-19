using System.Text.Json;
using OmnisRouter.Api.Routing;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Routing;

namespace OmnisRouter.Api.Endpoints;

/// <summary>
/// <c>POST /v1/route</c> — returns the full routing decision for a request WITHOUT calling any
/// upstream provider and without incurring cost (FR-008). The transparency surface.
/// </summary>
public static class RouteEndpoint
{
    private const string DefaultTenant = "default";

    public static IEndpointRouteBuilder MapRoute(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/route", async (
            HttpContext http,
            IEnumerable<IFormatAdapter> adapters,
            IEnumerable<IUpstreamClient> upstreams,
            IRoutingPolicy policy,
            RoutingDefaults defaults,
            CancellationToken cancellationToken) =>
        {
            var adapter = adapters.FirstOrDefault(a => a.Format == ClientFormat.OpenAI)
                ?? throw new OmnisException(500, "adapter_unavailable", "OpenAI adapter is not registered.");

            JsonElement body;
            try
            {
                body = await http.Request.ReadFromJsonAsync<JsonElement>(cancellationToken);
            }
            catch (JsonException)
            {
                throw new OmnisException(400, "invalid_request", "Request body is not valid JSON.");
            }

            var request = adapter.ToInternal(body);
            var routingContext = RoutingPipeline.BuildContext(upstreams, defaults, DefaultTenant, out _);

            // Decide only — no upstream dispatch, no credential lookup, no cost incurred.
            var decision = policy.Decide(request, routingContext);
            return Results.Json(ReceiptJson.ToJson(decision));
        });

        return app;
    }
}
