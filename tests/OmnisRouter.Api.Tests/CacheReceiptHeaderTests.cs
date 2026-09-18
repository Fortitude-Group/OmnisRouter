using Microsoft.AspNetCore.Http;
using OmnisRouter.Api.Endpoints;
using OmnisRouter.CacheHygiene;

namespace OmnisRouter.Api.Tests;

/// <summary>The receipt cache-block header mapping (US1, contracts/receipt-cache-block.md).</summary>
public class CacheReceiptHeaderTests
{
    private static readonly PricingStamp Stamp = new("2026-08-15", "2026-08-15", 0.79m, ShadowPrice: false);

    [Fact]
    public void An_avoidable_miss_writes_cause_and_waste_headers()
    {
        var http = new DefaultHttpContext();
        RoutedRequestHandler.WriteCacheHeaders(http.Response, new CacheHygieneResult
        {
            Missed = true,
            Cause = CauseClass.CrlfDrift,
            Avoidable = true,
            RecomputedTokens = 1234,
            WasteGbp = 0.0123m,
            Pricing = Stamp,
        });

        var h = http.Response.Headers;
        Assert.Equal("crlf_drift", h["X-Omnis-Cache-Cause"]);
        Assert.Equal("true", h["X-Omnis-Cache-Avoidable"]);
        Assert.Equal("1234", h["X-Omnis-Cache-Recomputed-Tokens"]);
        Assert.Equal("0.0123", h["X-Omnis-Cache-Waste-Gbp"]);
        Assert.Equal("2026-08-15", h["X-Omnis-Cache-Pricing-Version"]);
        Assert.Equal("false", h["X-Omnis-Cache-Shadow"]);
        Assert.False(h.ContainsKey("X-Omnis-Cache-Fix"));   // no fix ran
    }

    [Fact]
    public void A_fix_writes_the_saving_headers()
    {
        var http = new DefaultHttpContext();
        RoutedRequestHandler.WriteCacheHeaders(http.Response, new CacheHygieneResult
        {
            Missed = true,
            Cause = CauseClass.CrlfDrift,
            Avoidable = true,
            FixApplied = FixClass.LineEnding,
            SavedTokens = 1200,
            SavedGbp = 0.011m,
            Pricing = Stamp,
        });

        var h = http.Response.Headers;
        Assert.Equal("line_ending", h["X-Omnis-Cache-Fix"]);
        Assert.Equal("1200", h["X-Omnis-Cache-Saved-Tokens"]);
        Assert.Equal("0.011", h["X-Omnis-Cache-Saved-Gbp"]);
    }
}
