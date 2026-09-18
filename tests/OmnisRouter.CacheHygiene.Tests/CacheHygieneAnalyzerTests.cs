using System.Text;
using OmnisRouter.CacheHygiene;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;

namespace OmnisRouter.CacheHygiene.Tests;

public class CacheHygieneAnalyzerTests
{
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);
    private static readonly ModelRef Model = new(Provider.Anthropic, "claude-haiku-4-5");

    // Premium 0.001 USD/token, rate 0.8 => £ per token = 0.0008.
    private sealed class FakePricing : IPricingBook
    {
        public string SnapshotDate => "2026-08-15";
        public string FxDate => "2026-08-15";
        public decimal UsdGbp => 0.8m;
        public decimal CacheWritePremiumUsdPerToken(ModelRef model) => 0.001m;
        public decimal EstimateUsd(ModelRef model, int inputTokens, int outputTokens) => 0m;
        public decimal EstimateUsd(ModelRef model, Usage usage) => 0m;
    }

    private static CacheHygieneAnalyzer NewAnalyzer() => new(new FakePricing());

    [Fact]
    public void First_in_lineage_is_no_miss()
    {
        var r = NewAnalyzer().Analyse(B("anything"), null, previousPrefix: null, null,
            new Usage(), Model, BillingModel.PayAsYouGo);
        Assert.False(r.Missed);
    }

    [Fact]
    public void Identical_prefix_is_no_miss()
    {
        var r = NewAnalyzer().Analyse(B("same"), null, B("same"), null, new Usage(), Model, BillingModel.PayAsYouGo);
        Assert.False(r.Missed);
    }

    [Fact]
    public void Avoidable_miss_prices_the_waste()
    {
        var r = NewAnalyzer().Analyse(
            currentPrefixRaw: B("hello\r\nworld"),
            currentPrefixNormalised: null,
            previousPrefix: B("hello\nworld"),
            appliedFixes: null,
            usage: new Usage { CacheCreationTokens = 1000 },
            Model, BillingModel.PayAsYouGo);

        Assert.True(r.Missed);
        Assert.Equal(CauseClass.CrlfDrift, r.Cause);
        Assert.True(r.Avoidable);
        Assert.Equal(1000, r.RecomputedTokens);
        Assert.Equal(0.8m, r.WasteGbp);     // 1000 * 0.001 * 0.8
        Assert.Equal(0, r.SavedTokens);
        Assert.Null(r.FixApplied);
        Assert.False(r.Pricing!.ShadowPrice);
    }

    [Fact]
    public void Unavoidable_miss_is_not_priced()
    {
        var r = NewAnalyzer().Analyse(
            B("you are a terse bot"), null, B("you are a helpful bot"), null,
            new Usage { CacheCreationTokens = 1000 }, Model, BillingModel.PayAsYouGo);

        Assert.Equal(CauseClass.GenuineEdit, r.Cause);
        Assert.False(r.Avoidable);
        Assert.Equal(1000, r.RecomputedTokens);
        Assert.Equal(0m, r.WasteGbp);
    }

    [Fact]
    public void A_fix_that_prevents_the_miss_prices_the_saving()
    {
        var prev = B("hello\nworld");
        var r = NewAnalyzer().Analyse(
            currentPrefixRaw: B("hello\r\nworld"),   // would have missed on CRLF
            currentPrefixNormalised: prev,            // the fix made it match
            previousPrefix: prev,
            appliedFixes: new HashSet<FixClass> { FixClass.LineEnding },
            usage: new Usage { CacheReadTokens = 1000 },   // real usage: it read (hit)
            Model, BillingModel.PayAsYouGo);

        Assert.True(r.Missed);
        Assert.Equal(CauseClass.CrlfDrift, r.Cause);
        Assert.Equal(FixClass.LineEnding, r.FixApplied);
        Assert.Equal(0, r.RecomputedTokens);
        Assert.Equal(0m, r.WasteGbp);
        Assert.Equal(1000, r.SavedTokens);
        Assert.Equal(0.8m, r.SavedGbp);     // 1000 * 0.001 * 0.8
    }

    [Fact]
    public void Subscription_figures_are_shadow_priced()
    {
        var r = NewAnalyzer().Analyse(
            B("a\r\nb"), null, B("a\nb"), null,
            new Usage { CacheCreationTokens = 100 }, Model, BillingModel.Subscription);

        Assert.True(r.Pricing!.ShadowPrice);
    }
}
