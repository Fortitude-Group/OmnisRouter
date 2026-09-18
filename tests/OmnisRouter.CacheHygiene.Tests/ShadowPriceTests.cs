using System.Text;
using OmnisRouter.CacheHygiene;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;

namespace OmnisRouter.CacheHygiene.Tests;

/// <summary>
/// SC-005 / FR-016 / FR-017: the pounds are honest about the plan they came from. On a flat-rate
/// subscription a miss costs no extra bill, so the figure is a shadow price — what a miss <em>would</em>
/// cost — flagged so no consumer treats it as spend. On pay-as-you-go the same figure is a real bill.
/// Either way every £ carries the pricing snapshot and FX date that produced it.
/// </summary>
public class ShadowPriceTests
{
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);
    private static readonly ModelRef Model = new(Provider.Anthropic, "claude-haiku-4-5");

    // Premium 0.001 USD/token, rate 0.8 => £ per token = 0.0008.
    private sealed class FakePricing : IPricingBook
    {
        public string SnapshotDate => "2026-08-15";
        public string FxDate => "2026-07-01";
        public decimal UsdGbp => 0.8m;
        public decimal CacheWritePremiumUsdPerToken(ModelRef model) => 0.001m;
        public decimal EstimateUsd(ModelRef model, int inputTokens, int outputTokens) => 0m;
        public decimal EstimateUsd(ModelRef model, Usage usage) => 0m;
    }

    private static CacheHygieneResult AnAvoidableMiss(BillingModel billing) =>
        new CacheHygieneAnalyzer(new FakePricing()).Analyse(
            currentPrefixRaw: B("hello\r\nworld"),
            currentPrefixNormalised: null,
            previousPrefix: B("hello\nworld"),
            appliedFixes: null,
            usage: new Usage { CacheCreationTokens = 1000 },
            Model, billing);

    [Fact]
    public void Subscription_marks_the_figure_a_shadow_not_a_bill()
    {
        var r = AnAvoidableMiss(BillingModel.Subscription);

        Assert.True(r.Pricing!.ShadowPrice);
        // The shadow figure is still computed — it shows what the miss would have cost — it is simply
        // flagged as not-a-bill so the dashboard never sums it into real spend.
        Assert.Equal(0.8m, r.WasteGbp);
        Assert.True(r.Avoidable);
    }

    [Fact]
    public void PayAsYouGo_is_a_real_bill()
    {
        var r = AnAvoidableMiss(BillingModel.PayAsYouGo);

        Assert.False(r.Pricing!.ShadowPrice);
        Assert.Equal(0.8m, r.WasteGbp);
    }

    [Theory]
    [InlineData(BillingModel.PayAsYouGo)]
    [InlineData(BillingModel.Subscription)]
    public void Every_figure_carries_the_pricing_and_fx_stamp(BillingModel billing)
    {
        var r = AnAvoidableMiss(billing);

        Assert.Equal("2026-08-15", r.Pricing!.PricingVersion);
        Assert.Equal("2026-07-01", r.Pricing.FxDate);
        Assert.Equal(0.8m, r.Pricing.UsdGbp);
    }
}
