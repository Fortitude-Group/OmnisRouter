using Microsoft.EntityFrameworkCore;
using OmnisRouter.Store;

namespace OmnisRouter.Api.Auth;

/// <summary>
/// Requires <c>Authorization: Bearer &lt;router-token&gt;</c> on every request except the exempt
/// health/readiness/root probes. Validates by hashing the presented token
/// (<see cref="RouterTokenHasher"/>) and comparing against <c>RouterToken.HashedToken</c> in the
/// store (spec: contracts/wire-formats.md "Authentication"). Runs as plain (non-endpoint-routed)
/// middleware so it also protects paths that don't have a mapped endpoint yet (the US1 routing
/// pipeline lands later) — an unauthenticated request to e.g. <c>/v1/messages</c> is rejected here
/// before it would otherwise 404.
/// </summary>
public sealed class RouterTokenAuthMiddleware
{
    private static readonly string[] ExemptPaths = ["/health", "/readyz", "/"];

    private readonly RequestDelegate _next;

    public RouterTokenAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (IsExempt(path))
        {
            await _next(context);
            return;
        }

        if (!TryGetBearerToken(context.Request, out var rawToken))
        {
            throw new OmnisException(
                StatusCodes.Status401Unauthorized, "unauthorized", "Missing or malformed Authorization header.");
        }

        var dbContext = context.RequestServices.GetRequiredService<OmnisRouterDbContext>();
        var hashedToken = RouterTokenHasher.Hash(rawToken);

        var routerToken = await dbContext.RouterTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.HashedToken == hashedToken, context.RequestAborted);

        if (routerToken is null || routerToken.RevokedAt is not null)
        {
            throw new OmnisException(
                StatusCodes.Status401Unauthorized, "unauthorized", "Invalid or revoked router token.");
        }

        context.Items["OmnisTenantId"] = routerToken.TenantId;

        await _next(context);
    }

    private static bool IsExempt(string path) =>
        Array.Exists(ExemptPaths, p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

    private static bool TryGetBearerToken(HttpRequest request, out string token)
    {
        token = string.Empty;

        if (!request.Headers.TryGetValue("Authorization", out var values))
        {
            return false;
        }

        var header = values.ToString();
        const string prefix = "Bearer ";

        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = header[prefix.Length..].Trim();
        return token.Length > 0;
    }
}

public static class RouterTokenAuthMiddlewareExtensions
{
    /// <summary>Registers <see cref="RouterTokenAuthMiddleware"/>. Must run after error handling.</summary>
    public static IApplicationBuilder UseRouterTokenAuth(this IApplicationBuilder app) =>
        app.UseMiddleware<RouterTokenAuthMiddleware>();
}
