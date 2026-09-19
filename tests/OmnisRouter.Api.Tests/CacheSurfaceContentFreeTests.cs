using System.Text.Json.Nodes;
using OmnisRouter.Api.Endpoints;
using OmnisRouter.CacheHygiene;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;

namespace OmnisRouter.Api.Tests;

/// <summary>
/// SC-003 / content-free gate (feature 005): the tray-facing surfaces carry scalars and closed-set
/// labels only. No prompt text, request content, diff, or key can appear. Enforced by walking the exact
/// JSON both endpoints emit, the same posture as the 004 outbound-record test.
/// </summary>
public class CacheSurfaceContentFreeTests
{
    private sealed class FakePricing : IPricingBook
    {
        public string SnapshotDate => "2026-08-15";
        public decimal EstimateUsd(ModelRef model, int inputTokens, int outputTokens) => 0m;
        public decimal EstimateUsd(ModelRef model, Usage usage) => 0m;
    }

    private static readonly HashSet<string> FixWireNames = ["line_ending", "trailing_whitespace", "tool_ordering"];

    [Fact]
    public void Summary_leaves_are_scalars_and_short_labels_only()
    {
        var snap = new CacheHygieneSnapshot(
            true,
            new CachePeriod(0.04m, 0.01m, 400, 100, 3, 1),
            new CachePeriod(0.02m, 0.01m, 200, 100, 2, 1),
            new PricingStamp("2026-08-15", "2026-08-15", 0.79m, false));

        var o = CacheHygieneSummaryEndpoint.BuildSummary(snap, "routed");

        // Every string leaf is a short label/date (a prompt byte would blow past this); no nested arrays.
        AssertContentFree(o);
    }

    [Fact]
    public void Fix_state_lists_only_the_known_wire_fix_names()
    {
        var svc = new CacheHygieneService(new FakePricing(), new CacheHygieneOptions { EnabledFixes = [FixClass.LineEnding] });

        var o = CacheHygieneFixesEndpoint.BuildState(svc);

        foreach (var key in new[] { "local", "effective" })
        {
            foreach (var name in o[key]!.AsArray())
            {
                Assert.Contains(name!.GetValue<string>(), FixWireNames);
            }
        }

        Assert.False(o["policy_overrides"]!.GetValue<bool>());   // no policy in this test
    }

    private static void AssertContentFree(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var kv in obj)
                {
                    if (kv.Value is not null)
                    {
                        AssertContentFree(kv.Value);
                    }
                }

                break;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    if (item is not null)
                    {
                        AssertContentFree(item);
                    }
                }

                break;
            case JsonValue value:
                if (value.TryGetValue<string>(out var s))
                {
                    // Labels and date/version stamps are short; a leaked prompt fragment would not be.
                    Assert.True(s.Length <= 24, $"string leaf too long to be a label: '{s}'");
                }

                break;
        }
    }
}
