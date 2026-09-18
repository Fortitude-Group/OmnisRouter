using System.Text.Json.Nodes;
using OmnisRouter.Api.Endpoints;
using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;
using OmnisRouter.Vigil;

namespace OmnisRouter.Api.Tests;

/// <summary>
/// SC-002 / FR-014: the outbound <c>cache_waste</c> block carries scalars and closed-set labels only.
/// No prompt bytes, no diff, no key ever reaches an outbound record. This mirrors the allowlist posture
/// of <see cref="IngestRecordMapperTests"/> but drills into the cache block specifically, and checks
/// both serialisers (receipts-up to Vigil and the local NDJSON export) emit an identical, content-free
/// shape.
/// </summary>
public class CacheContentFreeTests
{
    // The frozen cache_waste allowlist (docs/contracts/omnisvigil-ingest-record.schema.json).
    private static readonly HashSet<string> CacheFields =
    [
        "cause_class", "avoidable", "recomputed_tokens", "waste_gbp", "fix_applied",
        "saved_tokens", "saved_gbp", "pricing_version", "fx_date", "usd_gbp", "shadow_price",
    ];

    private static readonly HashSet<string> CauseLabels =
    [
        "crlf_drift", "trailing_whitespace", "volatile_header", "timestamp_injection",
        "concat_order_change", "tool_definition_churn", "model_change", "system_prompt_change",
        "genuine_edit",
    ];

    private static readonly HashSet<string> FixLabels = ["line_ending", "trailing_whitespace", "tool_ordering"];

    // An entry whose cache figures are populated, and whose content-bearing surrogates would leak if
    // the mapper ever widened past the allowlist. The strings here are deliberately label-shaped: a
    // free-text leak would show up as a value outside the closed sets below.
    private static DecisionLogEntry AnalysedEntry() => new()
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
        CacheCause = "crlf_drift",
        CacheAvoidable = true,
        CacheRecomputedTokens = 1234,
        CacheWasteGbp = 0.0123m,
        CacheFixApplied = "line_ending",
        CacheSavedTokens = 1200,
        CacheSavedGbp = 0.0110m,
        CachePricingVersion = "2026-08-15",
        CacheFxDate = "2026-08-15",
        CacheUsdGbp = 0.79m,
        CacheShadowPrice = false,
    };

    [Fact]
    public void CacheWaste_emits_only_allowlisted_scalar_fields()
    {
        var block = IngestRecordMapper.ToRecord(AnalysedEntry(), "router-1", emitCacheWaste: true)["cache_waste"]!.AsObject();

        AssertContentFree(block);
    }

    [Fact]
    public void Both_serialisers_emit_an_identical_cache_block()
    {
        var entry = AnalysedEntry();

        var upstream = IngestRecordMapper.ToRecord(entry, "router-1", emitCacheWaste: true)["cache_waste"]!.AsObject();
        var local = AnalyticsDecisionsEndpoint.BuildCacheWaste(entry)!;

        AssertContentFree(local);
        Assert.Equal(upstream.ToJsonString(), local.ToJsonString());
    }

    private static void AssertContentFree(JsonObject block)
    {
        foreach (var (key, value) in block)
        {
            // Every key is on the frozen allowlist.
            Assert.Contains(key, CacheFields);

            // Every value is a scalar (JsonValue) or an explicit null; never a nested object or array
            // that could smuggle bytes.
            Assert.True(value is null or JsonValue, $"cache_waste.{key} must be a scalar");
        }

        // The only string-typed fields are closed-set labels/dates, never free text.
        Assert.Contains(block["cause_class"]!.GetValue<string>(), CauseLabels);

        if (block["fix_applied"] is { } fix)
        {
            Assert.Contains(fix.GetValue<string>(), FixLabels);
        }

        // pricing_version / fx_date are date-shaped stamps, not prose.
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", block["pricing_version"]!.GetValue<string>());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", block["fx_date"]!.GetValue<string>());
    }
}
