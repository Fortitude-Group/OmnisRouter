using OmnisRouter.CacheHygiene;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;

namespace OmnisRouter.CacheHygiene.Tests;

public class CacheHygieneServiceTests
{
    private static readonly ModelRef Model = new(Provider.Anthropic, "claude-haiku-4-5");

    private sealed class FakePricing : IPricingBook
    {
        public string SnapshotDate => "2026-08-15";
        public string FxDate => "2026-08-15";
        public decimal UsdGbp => 0.8m;
        public decimal CacheWritePremiumUsdPerToken(ModelRef model) => 0.001m;
        public decimal EstimateUsd(ModelRef model, int inputTokens, int outputTokens) => 0m;
        public decimal EstimateUsd(ModelRef model, Usage usage) => 0m;
    }

    private sealed class ThrowingPricing : IPricingBook
    {
        public string SnapshotDate => "x";
        public decimal EstimateUsd(ModelRef model, int inputTokens, int outputTokens) => 0m;
        public decimal EstimateUsd(ModelRef model, Usage usage) => 0m;
        public decimal CacheWritePremiumUsdPerToken(ModelRef model) => throw new InvalidOperationException("boom");
    }

    private static ChatRequest Cached(string system, string session = "s1") => new()
    {
        Messages = [],
        OriginFormat = ClientFormat.Anthropic,
        System = [new TextPart(system) { Cache = new CacheDirective() }],
        SessionId = session,
    };

    private static CacheHygieneService Service(IPricingBook? pricing = null, bool measurement = true) =>
        new(pricing ?? new FakePricing(), new CacheHygieneOptions { MeasurementEnabled = measurement });

    private static readonly IReadOnlySet<FixClass> NoFixes = new HashSet<FixClass>();

    // Measurement-only helper (no fix): the sent request is the raw request.
    private static CacheHygieneResult? Measure(CacheHygieneService s, ChatRequest r, Usage u) =>
        s.Analyse(r, r, NoFixes, Model, u);

    [Fact]
    public void First_request_in_a_lineage_reports_nothing()
    {
        var svc = Service();
        Assert.Null(Measure(svc, Cached("sys\nprompt"), new Usage { CacheCreationTokens = 100 }));
    }

    [Fact]
    public void A_crlf_only_change_in_the_next_request_is_measured()
    {
        var svc = Service();
        Measure(svc, Cached("you are a bot\nbe helpful"), new Usage { CacheReadTokens = 500 });   // establishes the lineage

        var r = Measure(svc, Cached("you are a bot\r\nbe helpful"), new Usage { CacheCreationTokens = 500 });

        Assert.NotNull(r);
        Assert.Equal(CauseClass.CrlfDrift, r!.Cause);
        Assert.True(r.Avoidable);
        Assert.Equal(500, r.RecomputedTokens);
        Assert.True(r.WasteGbp > 0m);
        Assert.False(r.Pricing!.ShadowPrice);   // routed traffic is pay-as-you-go
    }

    [Fact]
    public void A_non_cacheable_request_is_skipped()
    {
        var svc = Service();
        var noCache = new ChatRequest { Messages = [], OriginFormat = ClientFormat.Anthropic, System = [new TextPart("plain")] };
        Assert.Null(Measure(svc, noCache, new Usage { CacheCreationTokens = 100 }));
    }

    [Fact]
    public void Measurement_off_reports_nothing()
    {
        var svc = Service(measurement: false);
        Measure(svc, Cached("a\nb"), new Usage());
        Assert.Null(Measure(svc, Cached("a\r\nb"), new Usage { CacheCreationTokens = 10 }));
    }

    [Fact]
    public void Fails_open_when_the_analysis_throws()
    {
        var svc = Service(new ThrowingPricing());
        Measure(svc, Cached("a\nb"), new Usage());
        // The pricing book throws inside Analyse; the service must swallow it and return null, never throw.
        var r = Measure(svc, Cached("a\r\nb"), new Usage { CacheCreationTokens = 10 });
        Assert.Null(r);
    }

    [Fact]
    public void Fixes_are_off_by_default()
    {
        var svc = Service();
        var input = Cached("a\r\nb");
        var (sent, applied) = svc.Normalise(input);
        Assert.Empty(applied);
        Assert.Same(input, sent);   // nothing enabled -> request forwarded unchanged
    }

    [Fact]
    public void An_enabled_fix_turns_a_miss_into_a_measured_saving()
    {
        var svc = new CacheHygieneService(new FakePricing(),
            new CacheHygieneOptions { EnabledFixes = new() { FixClass.LineEnding } });

        // Establish the lineage with the LF prefix the provider cached.
        svc.Analyse(Cached("you are a bot\nbe kind"), Cached("you are a bot\nbe kind"), NoFixes, Model,
            new Usage { CacheReadTokens = 400 });

        // The next request arrives with CRLF; the enabled fix normalises it to LF before dispatch.
        var raw = Cached("you are a bot\r\nbe kind");
        var (sent, applied) = svc.Normalise(raw);
        Assert.Contains(FixClass.LineEnding, applied);

        var r = svc.Analyse(raw, sent, applied, Model, new Usage { CacheReadTokens = 400 });

        Assert.NotNull(r);
        Assert.Equal(CauseClass.CrlfDrift, r!.Cause);
        Assert.Equal(FixClass.LineEnding, r.FixApplied);
        Assert.Equal(400, r.SavedTokens);
        Assert.True(r.SavedGbp > 0m);
        Assert.Equal(0m, r.WasteGbp);
    }

    private sealed class StubFixPolicy(bool? verdict) : IFixPolicy
    {
        public bool? IsFixEnabled(FixClass fix) => verdict;
    }

    [Fact]
    public void A_control_plane_policy_can_force_a_fix_on_that_local_config_left_off()
    {
        // Local config enables nothing; the policy forces the fix on.
        var svc = new CacheHygieneService(new FakePricing(), new CacheHygieneOptions(), new StubFixPolicy(true));

        var (_, applied) = svc.Normalise(Cached("a\r\nb"));

        Assert.Contains(FixClass.LineEnding, applied);
    }

    [Fact]
    public void A_control_plane_policy_can_force_a_fix_off_that_local_config_turned_on()
    {
        // Local config enables the fix; the policy overrides it off fleet-wide.
        var svc = new CacheHygieneService(
            new FakePricing(),
            new CacheHygieneOptions { EnabledFixes = new() { FixClass.LineEnding } },
            new StubFixPolicy(false));

        var input = Cached("a\r\nb");
        var (sent, applied) = svc.Normalise(input);

        Assert.Empty(applied);
        Assert.Same(input, sent);   // policy forced off -> request forwarded unchanged
    }

    [Fact]
    public void With_no_policy_opinion_the_local_config_decides()
    {
        var svc = new CacheHygieneService(
            new FakePricing(),
            new CacheHygieneOptions { EnabledFixes = new() { FixClass.LineEnding } },
            new StubFixPolicy(null));   // policy defers

        var (_, applied) = svc.Normalise(Cached("a\r\nb"));

        Assert.Contains(FixClass.LineEnding, applied);
    }
}
