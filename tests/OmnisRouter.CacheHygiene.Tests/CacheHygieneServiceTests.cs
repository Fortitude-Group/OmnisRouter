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

    [Fact]
    public void First_request_in_a_lineage_reports_nothing()
    {
        var svc = Service();
        Assert.Null(svc.Analyse(Cached("sys\nprompt"), Model, new Usage { CacheCreationTokens = 100 }));
    }

    [Fact]
    public void A_crlf_only_change_in_the_next_request_is_measured()
    {
        var svc = Service();
        svc.Analyse(Cached("you are a bot\nbe helpful"), Model, new Usage { CacheReadTokens = 500 });   // establishes the lineage

        var r = svc.Analyse(Cached("you are a bot\r\nbe helpful"), Model, new Usage { CacheCreationTokens = 500 });

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
        Assert.Null(svc.Analyse(noCache, Model, new Usage { CacheCreationTokens = 100 }));
    }

    [Fact]
    public void Measurement_off_reports_nothing()
    {
        var svc = Service(measurement: false);
        svc.Analyse(Cached("a\nb"), Model, new Usage());
        Assert.Null(svc.Analyse(Cached("a\r\nb"), Model, new Usage { CacheCreationTokens = 10 }));
    }

    [Fact]
    public void Fails_open_when_the_analysis_throws()
    {
        var svc = Service(new ThrowingPricing());
        svc.Analyse(Cached("a\nb"), Model, new Usage());
        // The pricing book throws inside Analyse; the service must swallow it and return null, never throw.
        var r = svc.Analyse(Cached("a\r\nb"), Model, new Usage { CacheCreationTokens = 10 });
        Assert.Null(r);
    }
}
