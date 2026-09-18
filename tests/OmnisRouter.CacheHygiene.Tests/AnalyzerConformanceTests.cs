using OmnisRouter.CacheHygiene;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;

namespace OmnisRouter.CacheHygiene.Tests;

/// <summary>
/// Cross-tool conformance (T013): OmnisRouter's analyzer is held to ProseWeightVisualizer's CacheScope
/// ground-truth vectors (ported under <c>vectors/</c>, T003), so the two implementations agree on the
/// cause of a divergence rather than drifting apart. The headline case is CRLF drift — the same prompt
/// re-sent with Windows line endings — which both tools must classify as avoidable and price as waste.
/// </summary>
public class AnalyzerConformanceTests
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

    private static byte[] Vector(string name)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "vectors", name));

    private static CacheHygieneAnalyzer NewAnalyzer() => new(new FakePricing());

    [Fact]
    public void Crlf_vector_pair_is_classified_as_avoidable_drift_and_priced()
    {
        var prev = Vector("lf_next.txt");        // the clean prefix the provider cached (LF)
        var next = Vector("crlf_prev.txt");      // the same content re-sent with a CR

        var r = NewAnalyzer().Analyse(
            currentPrefixRaw: next,
            currentPrefixNormalised: null,
            previousPrefix: prev,
            appliedFixes: null,
            usage: new Usage { CacheCreationTokens = 1000 },
            Model, BillingModel.PayAsYouGo);

        Assert.True(r.Missed);
        Assert.Equal(CauseClass.CrlfDrift, r.Cause);
        Assert.True(r.Avoidable);
        Assert.Equal(0.8m, r.WasteGbp);          // 1000 * 0.001 * 0.8
    }

    [Fact]
    public void Crlf_drift_is_classified_the_same_in_either_direction()
    {
        // Symmetry: it does not matter which side carried the CR — the cause is the same.
        var r = NewAnalyzer().Analyse(
            Vector("lf_next.txt"), null, Vector("crlf_prev.txt"), null,
            new Usage { CacheCreationTokens = 10 }, Model, BillingModel.PayAsYouGo);

        Assert.Equal(CauseClass.CrlfDrift, r.Cause);
    }

    [Fact]
    public void A_volatile_header_only_divergence_is_reported_conservatively()
    {
        // Until timestamp/volatile-header classification lands (a tracked refinement in Classify), a
        // header that differs only by its `updated:` date is treated as a genuine edit: unavoidable and
        // priced at £0. That never overstates avoidable waste, which is the property the ledger relies on.
        var withDate = Vector("volatile_header.md");
        var previous = System.Text.Encoding.UTF8.GetBytes("# Title\nupdated: 2026-01-01\nbody\n");

        var r = NewAnalyzer().Analyse(
            withDate, null, previous, null,
            new Usage { CacheCreationTokens = 500 }, Model, BillingModel.PayAsYouGo);

        Assert.True(r.Missed);
        Assert.False(r.Avoidable);
        Assert.Equal(0m, r.WasteGbp);
    }
}
