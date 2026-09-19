using System.Text.Json.Nodes;
using OmnisRouter.CacheHygiene;

namespace OmnisRouter.Api.Endpoints;

/// <summary>
/// <c>GET /v1/analytics/cache-hygiene/summary</c> — the content-free headline figures the tray shows
/// (contracts/cache-hygiene-summary.md, feature 005). Read O(1) from the in-memory
/// <see cref="CacheHygieneTally"/>; carries scalars and labels only, never prompt text, diff, or keys.
/// </summary>
public static class CacheHygieneSummaryEndpoint
{
    public static IEndpointRouteBuilder MapCacheHygieneSummary(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/analytics/cache-hygiene/summary", (CacheHygieneTally tally, CacheHygieneService service) =>
        {
            var snapshot = tally.Snapshot(service.MeasurementEnabled);
            return Results.Json(BuildSummary(snapshot, source: "routed"));
        });

        return app;
    }

    // Internal so a content-free / reconciliation test can assert the exact shape.
    internal static JsonObject BuildSummary(CacheHygieneSnapshot s, string source)
    {
        var summary = new JsonObject
        {
            ["measurement_enabled"] = s.MeasurementEnabled,
            ["source"] = source,
            ["since_start"] = BuildPeriod(s.SinceStart),
            ["today"] = BuildPeriod(s.Today),
        };

        // £ figures are only meaningful with their basis, so stamp it (constitution Principle XII).
        if (s.Pricing is { } p)
        {
            summary["pricing_version"] = p.PricingVersion;
            summary["fx_date"] = p.FxDate;
            summary["usd_gbp"] = decimal.ToDouble(p.UsdGbp);
            summary["shadow_price"] = p.ShadowPrice;
        }
        else
        {
            summary["pricing_version"] = null;
            summary["fx_date"] = null;
            summary["usd_gbp"] = null;
            summary["shadow_price"] = false;
        }

        return summary;
    }

    private static JsonObject BuildPeriod(CachePeriod p) => new()
    {
        ["avoidable_waste_gbp"] = decimal.ToDouble(p.AvoidableWasteGbp),
        ["recovered_gbp"] = decimal.ToDouble(p.RecoveredGbp),
        ["recomputed_tokens"] = p.RecomputedTokens,
        ["saved_tokens"] = p.SavedTokens,
        ["miss_count"] = p.MissCount,
        ["recovery_count"] = p.RecoveryCount,
    };
}
