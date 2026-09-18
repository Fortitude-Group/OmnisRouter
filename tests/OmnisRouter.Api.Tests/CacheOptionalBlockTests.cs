using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;
using OmnisRouter.Vigil;

namespace OmnisRouter.Api.Tests;

/// <summary>
/// SC-007 / FR-015 / research D5: the <c>cache_waste</c> block is strictly optional. A record with no
/// analysis is byte-identical to a pre-004 record, and even when analysis ran the block is withheld
/// until emission is switched on, so an OmnisVigil that predates the field never sees it.
/// </summary>
public class CacheOptionalBlockTests
{
    private static DecisionLogEntry BaseEntry() => new()
    {
        Id = "req-1",
        TenantId = "default",
        Timestamp = DateTimeOffset.UnixEpoch,
        RequestHash = "hash",
        ClientFormat = ClientFormat.Anthropic,
        ClusterId = 1,
        ChosenProvider = Provider.Anthropic,
        ChosenModelId = "claude-haiku-4-5",
        Confidence = 0.9,
        PolicyVersion = "v5",
        EstCostUsd = 0.02m,
        EstCostDeltaVsBigUsd = -0.10m,
        Outcome = RequestOutcome.Success,
        LatencyMs = 30,
    };

    private static DecisionLogEntry Analysed()
    {
        var e = BaseEntry();
        return e with
        {
            CacheCause = "crlf_drift",
            CacheAvoidable = true,
            CacheRecomputedTokens = 1234,
            CacheWasteGbp = 0.0123m,
            CacheSavedTokens = 0,
            CacheSavedGbp = 0m,
            CachePricingVersion = "2026-08-15",
            CacheFxDate = "2026-08-15",
            CacheUsdGbp = 0.79m,
            CacheShadowPrice = false,
        };
    }

    [Fact]
    public void No_analysis_produces_a_record_identical_to_today()
    {
        var withFeature = IngestRecordMapper.ToRecord(BaseEntry(), "router-1", emitCacheWaste: true);
        var preFeature = IngestRecordMapper.ToRecord(BaseEntry(), "router-1", emitCacheWaste: false);

        // No cache_waste member at all (omitted, not a null value).
        Assert.False(withFeature.ContainsKey("cache_waste"));
        // Byte-identical to a record produced with the feature switched off entirely.
        Assert.Equal(preFeature.ToJsonString(), withFeature.ToJsonString());
    }

    [Fact]
    public void Analysis_present_but_emission_off_withholds_the_block()
    {
        var record = IngestRecordMapper.ToRecord(Analysed(), "router-1", emitCacheWaste: false);

        Assert.False(record.ContainsKey("cache_waste"));
    }

    [Fact]
    public void Analysis_present_and_emission_on_includes_the_block()
    {
        var record = IngestRecordMapper.ToRecord(Analysed(), "router-1", emitCacheWaste: true);

        Assert.True(record.ContainsKey("cache_waste"));
        Assert.Equal("crlf_drift", record["cache_waste"]!["cause_class"]!.GetValue<string>());
    }
}
