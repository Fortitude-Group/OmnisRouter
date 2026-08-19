using OmnisRouter.Routing;

namespace OmnisRouter.Api.Endpoints;

/// <summary><c>GET /v1/models</c> — advertises the candidate model pool (FR-016), OpenAI-list shaped.</summary>
public static class ModelsEndpoint
{
    public static IEndpointRouteBuilder MapModels(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/models", (RoutingDefaults defaults) =>
        {
            var data = defaults.CandidatePool
                .Select(m => new
                {
                    id = m.ModelId,
                    @object = "model",
                    owned_by = m.Provider.ToString().ToLowerInvariant(),
                })
                .ToArray();

            return Results.Json(new { @object = "list", data });
        });

        return app;
    }
}
