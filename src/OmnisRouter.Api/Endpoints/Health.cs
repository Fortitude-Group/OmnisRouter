using Microsoft.EntityFrameworkCore;
using OmnisRouter.Store;

namespace OmnisRouter.Api.Endpoints;

/// <summary>Liveness/readiness probes. Both unauthenticated (exempted in <c>RouterTokenAuthMiddleware</c>).</summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapOmnisHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Liveness: always 200 once the process is up and serving requests.
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        // Readiness: can the store actually be reached (DB connection open-able)?
        app.MapGet("/readyz", async (OmnisRouterDbContext db, CancellationToken cancellationToken) =>
        {
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? Results.Ok(new { status = "ready" })
                : Results.Json(new { status = "not_ready" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        return app;
    }
}
