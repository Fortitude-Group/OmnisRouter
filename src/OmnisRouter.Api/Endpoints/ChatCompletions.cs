using OmnisRouter.Api.Routing;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Routing;

namespace OmnisRouter.Api.Endpoints;

/// <summary><c>POST /v1/chat/completions</c> — OpenAI drop-in routed endpoint (US1).</summary>
public static class ChatCompletionsEndpoint
{
    public static IEndpointRouteBuilder MapChatCompletions(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/chat/completions", (
            HttpContext http,
            IEnumerable<IFormatAdapter> adapters,
            IEnumerable<IUpstreamClient> upstreams,
            IRoutingPolicy policy,
            RoutingDefaults defaults,
            IProviderCredentialResolver credentials,
            IDecisionLog decisionLog,
            ICapabilityGuard guard,
            IImageMaterializer materializer,
            IPricingBook pricing,
            CancellationToken cancellationToken) =>
            RoutedRequestHandler.ExecuteAsync(http, ClientFormat.OpenAI, pathModel: null, forceStream: null,
                adapters, upstreams, policy, defaults, credentials, decisionLog, guard, materializer, pricing, cancellationToken));

        return app;
    }
}
