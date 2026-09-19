using System.Linq;
using OmnisRouter.Api.Endpoints;
using OmnisRouter.CacheHygiene;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;

namespace OmnisRouter.Api.Tests;

public class CacheFixesEndpointTests
{
    private sealed class FakePricing : IPricingBook
    {
        public string SnapshotDate => "2026-08-15";
        public decimal EstimateUsd(ModelRef model, int inputTokens, int outputTokens) => 0m;
        public decimal EstimateUsd(ModelRef model, Usage usage) => 0m;
    }

    private sealed class StubPolicy(bool? verdict) : IFixPolicy
    {
        public bool? IsFixEnabled(FixClass fix) => verdict;
    }

    private static CacheHygieneService Service(IFixPolicy? policy, params FixClass[] localEnabled) =>
        new(new FakePricing(), new CacheHygieneOptions { EnabledFixes = [.. localEnabled] }, policy);

    private static List<string> Names(System.Text.Json.Nodes.JsonNode? array) =>
        array!.AsArray().Select(n => n!.GetValue<string>()).ToList();

    [Fact]
    public void BuildState_reports_local_and_effective_wire_names()
    {
        var o = CacheHygieneFixesEndpoint.BuildState(Service(policy: null, FixClass.LineEnding));

        Assert.Equal(["line_ending"], Names(o["local"]));
        Assert.Equal(["line_ending"], Names(o["effective"]));
        Assert.False(o["policy_overrides"]!.GetValue<bool>());
    }

    [Fact]
    public void Policy_override_wins_and_is_reported()
    {
        // Local enables nothing; the policy forces every fix on.
        var o = CacheHygieneFixesEndpoint.BuildState(Service(new StubPolicy(true)));

        Assert.Empty(Names(o["local"]));
        var effective = Names(o["effective"]);
        Assert.Contains("line_ending", effective);
        Assert.Contains("trailing_whitespace", effective);
        Assert.Contains("tool_ordering", effective);
        Assert.True(o["policy_overrides"]!.GetValue<bool>());
    }

    [Fact]
    public void SetLocalEnabledFixes_changes_effective_when_no_policy()
    {
        var svc = Service(policy: null);
        svc.SetLocalEnabledFixes([FixClass.ToolOrdering]);

        var o = CacheHygieneFixesEndpoint.BuildState(svc);

        Assert.Contains("tool_ordering", Names(o["effective"]));
    }
}
