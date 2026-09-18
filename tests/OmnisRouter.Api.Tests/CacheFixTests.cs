using OmnisRouter.CacheHygiene;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;

namespace OmnisRouter.Api.Tests;

/// <summary>
/// US3 fix behaviour end-to-end through <see cref="CacheHygieneService"/>: an enabled fix turns a
/// cache write into a read and the receipt shows the saving; fixes are off unless the operator turns
/// them on; and a fix is skipped (the request forwarded unchanged) whenever the normaliser cannot prove
/// the rewrite safe.
/// </summary>
public class CacheFixTests
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

    private static ChatRequest CachedSystem(string system, string session = "s1") => new()
    {
        Messages = [],
        OriginFormat = ClientFormat.Anthropic,
        System = [new TextPart(system) { Cache = new CacheDirective() }],
        SessionId = session,
    };

    private static CacheHygieneService Service(params FixClass[] enabled) =>
        new(new FakePricing(), new CacheHygieneOptions { EnabledFixes = [.. enabled] });

    [Fact]
    public void An_enabled_trailing_whitespace_fix_turns_write_into_read_and_shows_the_saving()
    {
        var svc = Service(FixClass.TrailingWhitespace);

        // The provider cached the clean prefix on the first request (a read next time).
        svc.Analyse(CachedSystem("keep me\nlean"), CachedSystem("keep me\nlean"), new HashSet<FixClass>(), Model,
            new Usage { CacheReadTokens = 300 });

        // The next request arrives with trailing whitespace; the fix strips it back to the cached form.
        var raw = CachedSystem("keep me   \nlean");
        var (sent, applied) = svc.Normalise(raw);
        Assert.Contains(FixClass.TrailingWhitespace, applied);

        var r = svc.Analyse(raw, sent, applied, Model, new Usage { CacheReadTokens = 300 });

        Assert.NotNull(r);
        Assert.Equal(FixClass.TrailingWhitespace, r!.FixApplied);
        Assert.Equal(300, r.SavedTokens);
        Assert.True(r.SavedGbp > 0m);
        Assert.Equal(0m, r.WasteGbp);
    }

    [Fact]
    public void With_no_fix_enabled_the_receipt_shows_no_fix()
    {
        var svc = Service();   // nothing enabled

        svc.Analyse(CachedSystem("a\nb"), CachedSystem("a\nb"), new HashSet<FixClass>(), Model,
            new Usage { CacheReadTokens = 100 });

        var raw = CachedSystem("a\r\nb");
        var (sent, applied) = svc.Normalise(raw);
        Assert.Empty(applied);   // off by default

        var r = svc.Analyse(raw, sent, applied, Model, new Usage { CacheCreationTokens = 100 });

        Assert.NotNull(r);
        Assert.Null(r!.FixApplied);   // the miss is measured, but nothing was fixed
    }

    [Fact]
    public void A_fix_is_skipped_when_safety_cannot_be_shown()
    {
        var svc = Service(FixClass.ToolOrdering);

        var raw = new ChatRequest
        {
            Messages = [],
            OriginFormat = ClientFormat.Anthropic,
            System = [new TextPart("sys") { Cache = new CacheDirective() }],
            Tools =
            [
                new Tool("beta", "b", "{this is not json"),
                new Tool("alpha", "a", """{"a":1}"""),
            ],
            SessionId = "s1",
        };

        var (sent, _) = svc.Normalise(raw);

        // The unparseable schema is forwarded byte-for-byte — never half-rewritten.
        Assert.Equal("{this is not json", sent.Tools.Single(t => t.Name == "beta").JsonSchema);
    }
}
