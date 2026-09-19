using OmnisRouter.Api.Endpoints;
using OmnisRouter.CacheHygiene;

namespace OmnisRouter.Api.Tests;

public class CacheSummaryEndpointTests
{
    private static readonly HashSet<string> AllowedKeys =
    [
        "measurement_enabled", "source", "since_start", "today",
        "pricing_version", "fx_date", "usd_gbp", "shadow_price",
    ];

    private static readonly HashSet<string> AllowedPeriodKeys =
    [
        "avoidable_waste_gbp", "recovered_gbp", "recomputed_tokens", "saved_tokens", "miss_count", "recovery_count",
    ];

    [Fact]
    public void BuildSummary_maps_periods_and_pricing_and_reconciles()
    {
        var snap = new CacheHygieneSnapshot(
            MeasurementEnabled: true,
            SinceStart: new CachePeriod(0.04m, 0.01m, 400, 100, 3, 1),
            Today: new CachePeriod(0.02m, 0.01m, 200, 100, 2, 1),
            Pricing: new PricingStamp("2026-08-15", "2026-08-15", 0.79m, ShadowPrice: false));

        var o = CacheHygieneSummaryEndpoint.BuildSummary(snap, "routed");

        Assert.True(o["measurement_enabled"]!.GetValue<bool>());
        Assert.Equal("routed", o["source"]!.GetValue<string>());
        Assert.Equal(0.02, o["today"]!["avoidable_waste_gbp"]!.GetValue<double>());
        Assert.Equal(0.01, o["today"]!["recovered_gbp"]!.GetValue<double>());
        Assert.Equal(3, o["since_start"]!["miss_count"]!.GetValue<int>());
        Assert.Equal(100L, o["today"]!["saved_tokens"]!.GetValue<long>());
        Assert.Equal("2026-08-15", o["pricing_version"]!.GetValue<string>());
        Assert.Equal(0.79, o["usd_gbp"]!.GetValue<double>());
        Assert.False(o["shadow_price"]!.GetValue<bool>());
    }

    [Fact]
    public void BuildSummary_emits_only_allowlisted_scalar_keys()
    {
        var snap = new CacheHygieneSnapshot(
            true,
            new CachePeriod(0.04m, 0.01m, 400, 100, 3, 1),
            new CachePeriod(0.02m, 0.01m, 200, 100, 2, 1),
            new PricingStamp("2026-08-15", "2026-08-15", 0.79m, false));

        var o = CacheHygieneSummaryEndpoint.BuildSummary(snap, "routed");

        foreach (var kv in o)
        {
            Assert.Contains(kv.Key, AllowedKeys);
        }

        foreach (var period in new[] { "since_start", "today" })
        {
            foreach (var kv in o[period]!.AsObject())
            {
                Assert.Contains(kv.Key, AllowedPeriodKeys);
            }
        }
    }

    [Fact]
    public void Fresh_start_has_no_pricing_and_zero_periods()
    {
        var snap = new CacheHygieneSnapshot(true, CachePeriod.Empty, CachePeriod.Empty, null);

        var o = CacheHygieneSummaryEndpoint.BuildSummary(snap, "routed");

        Assert.Null(o["pricing_version"]);
        Assert.Equal(0.0, o["today"]!["avoidable_waste_gbp"]!.GetValue<double>());
        Assert.Equal(0, o["since_start"]!["miss_count"]!.GetValue<int>());
        Assert.False(o["shadow_price"]!.GetValue<bool>());
    }

    [Fact]
    public void Measurement_off_is_reported()
    {
        var snap = new CacheHygieneSnapshot(false, CachePeriod.Empty, CachePeriod.Empty, null);

        var o = CacheHygieneSummaryEndpoint.BuildSummary(snap, "routed");

        Assert.False(o["measurement_enabled"]!.GetValue<bool>());
    }
}
