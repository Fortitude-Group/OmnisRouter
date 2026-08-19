using OmnisRouter.Api.Routing;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Routing;

namespace OmnisRouter.Api.Endpoints;

/// <summary><c>POST /v1/messages</c> — Anthropic Messages drop-in routed endpoint (US4).</summary>
public static class MessagesEndpoint
{
    public static IEndpointRouteBuilder MapMessages(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/messages", (
            HttpContext http,
            IEnumerable<IFormatAdapter> adapters,
            IEnumerable<IUpstreamClient> upstreams,
            IRoutingPolicy policy,
            RoutingDefaults defaults,
            IProviderCredentialResolver credentials,
            IDecisionLog decisionLog,
            ICapabilityGuard guard,
            IImageMaterializer materializer,
            CancellationToken cancellationToken) =>
            RoutedRequestHandler.ExecuteAsync(http, ClientFormat.Anthropic, pathModel: null, forceStream: null,
                adapters, upstreams, policy, defaults, credentials, decisionLog, guard, materializer, cancellationToken));

        return app;
    }
}
