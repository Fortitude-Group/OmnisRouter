using OmnisRouter.Api.Routing;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Routing;

namespace OmnisRouter.Api.Endpoints;

/// <summary>
/// <c>POST /v1beta/models/{model}:{action}</c> — Gemini generateContent drop-in routed endpoint (US4).
/// The model + action share one path segment (e.g. <c>gemini-2.5-flash:generateContent</c>), so the
/// segment is captured whole and split on the last colon.
/// </summary>
public static class GeminiGenerateEndpoint
{
    public static IEndpointRouteBuilder MapGeminiGenerate(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1beta/models/{modelAction}", (
            string modelAction,
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
        {
            var colon = modelAction.LastIndexOf(':');
            var model = colon > 0 ? modelAction[..colon] : modelAction;
            var action = colon > 0 ? modelAction[(colon + 1)..] : "generateContent";
            var forceStream = string.Equals(action, "streamGenerateContent", StringComparison.OrdinalIgnoreCase);

            return RoutedRequestHandler.ExecuteAsync(http, ClientFormat.Gemini, pathModel: model, forceStream: forceStream,
                adapters, upstreams, policy, defaults, credentials, decisionLog, guard, materializer, cancellationToken);
        });

        return app;
    }
}
