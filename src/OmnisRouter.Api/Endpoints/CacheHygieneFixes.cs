using System.Text.Json.Nodes;
using OmnisRouter.CacheHygiene;

namespace OmnisRouter.Api.Endpoints;

/// <summary>
/// <c>GET/PUT /v1/cache-hygiene/fixes</c> — read and set the byte-mutating fix classes at runtime from
/// the tray (contracts/cache-fixes-control.md, feature 005). The OmnisVigil policy override still wins:
/// PUT sets only the local layer, and the response reports the effective (policy-resolved) state.
/// </summary>
public static class CacheHygieneFixesEndpoint
{
    public static IEndpointRouteBuilder MapCacheHygieneFixes(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/cache-hygiene/fixes", (CacheHygieneService service) => Results.Json(BuildState(service)));

        app.MapPut("/v1/cache-hygiene/fixes", async (HttpContext http, CacheHygieneService service) =>
        {
            FixesRequest? body;
            try
            {
                body = await http.Request.ReadFromJsonAsync<FixesRequest>();
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.BadRequest(new { error = "invalid JSON body" });
            }

            if (body?.Enabled is null)
            {
                return Results.BadRequest(new { error = "body must be { \"enabled\": [...] }" });
            }

            var parsed = new HashSet<FixClass>();
            foreach (var name in body.Enabled)
            {
                if (!TryParseFix(name, out var fix))
                {
                    return Results.BadRequest(new { error = $"unknown fix '{name}'" });
                }

                parsed.Add(fix);
            }

            service.SetLocalEnabledFixes(parsed);
            return Results.Json(BuildState(service));
        });

        return app;
    }

    internal static JsonObject BuildState(CacheHygieneService service) => new()
    {
        ["local"] = ToWireArray(service.LocalEnabledFixes),
        ["effective"] = ToWireArray(service.EffectiveEnabledFixes()),
        ["policy_overrides"] = service.PolicyOverridesFixes,
    };

    private static JsonArray ToWireArray(IReadOnlyCollection<FixClass> fixes)
    {
        var arr = new JsonArray();
        foreach (var f in fixes)
        {
            arr.Add(f.Wire());
        }

        return arr;
    }

    private static bool TryParseFix(string wire, out FixClass fix)
    {
        switch (wire)
        {
            case "line_ending": fix = FixClass.LineEnding; return true;
            case "trailing_whitespace": fix = FixClass.TrailingWhitespace; return true;
            case "tool_ordering": fix = FixClass.ToolOrdering; return true;
            default: fix = default; return false;
        }
    }

    private sealed record FixesRequest(IReadOnlyList<string>? Enabled);
}
