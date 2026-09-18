using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;

namespace OmnisRouter.CacheHygiene;

/// <summary>
/// The pure cache-hygiene cost core (research D1–D7). Given a lineage's previous prefix, the current
/// prefix (raw, and normalised if a fix ran), and the real <see cref="Usage"/>, it finds the first
/// divergent byte, classifies the cause, and prices the waste or the saving in stamped GBP. No I/O, no
/// network, no console — it throws only into the caller's fail-open boundary. Deterministic given its
/// arguments and the injected <see cref="IPricingBook"/>.
///
/// The classifier asks each provably-safe normaliser "would this make the two prefixes equal?", so the
/// cause and the fix that resolves it are one and the same (spec FR-002/FR-008).
/// </summary>
public sealed class CacheHygieneAnalyzer
{
    private readonly IPricingBook _pricing;

    public CacheHygieneAnalyzer(IPricingBook pricing) => _pricing = pricing;

    private static readonly IReadOnlySet<FixClass> NoFixes = new HashSet<FixClass>();

    public CacheHygieneResult Analyse(
        byte[] currentPrefixRaw,
        byte[]? currentPrefixNormalised,
        byte[]? previousPrefix,
        IReadOnlySet<FixClass>? appliedFixes,
        Usage usage,
        ModelRef model,
        BillingModel billing)
    {
        appliedFixes ??= NoFixes;
        if (previousPrefix is null)
        {
            return CacheHygieneResult.NoMiss;   // first request in the lineage — nothing to compare
        }

        var rawOffset = FirstDivergence(currentPrefixRaw, previousPrefix);
        if (rawOffset < 0)
        {
            return CacheHygieneResult.NoMiss;   // the raw prefix matches — a natural hit, no drift cause
        }

        var cause = Classify(currentPrefixRaw, previousPrefix);
        var avoidable = cause.IsAvoidable();
        var premium = _pricing.CacheWritePremiumUsdPerToken(model);
        var stamp = new PricingStamp(
            _pricing.SnapshotDate, _pricing.FxDate, _pricing.UsdGbp, billing == BillingModel.Subscription);

        // Did an applied fix make the prefix we actually sent match the previous one, turning the miss
        // into a read? Then the saving is measured from the real (hit) usage.
        var fixPrevented = appliedFixes.Count > 0
            && currentPrefixNormalised is not null
            && FirstDivergence(currentPrefixNormalised, previousPrefix) < 0;

        if (fixPrevented)
        {
            var savedTokens = usage.CacheReadTokens;
            return new CacheHygieneResult
            {
                Missed = true,
                Cause = cause,
                Avoidable = avoidable,
                DivergenceOffset = rawOffset,
                FixApplied = CauseToFix(cause),
                RecomputedTokens = 0,
                SavedTokens = savedTokens,
                WasteGbp = 0m,
                SavedGbp = Gbp(savedTokens, premium, stamp.UsdGbp),
                Pricing = stamp,
            };
        }

        var recomputed = usage.CacheCreationTokens;   // tokens re-written at cache-write price on this miss
        return new CacheHygieneResult
        {
            Missed = true,
            Cause = cause,
            Avoidable = avoidable,
            DivergenceOffset = rawOffset,
            FixApplied = null,
            RecomputedTokens = recomputed,
            SavedTokens = 0,
            WasteGbp = avoidable ? Gbp(recomputed, premium, stamp.UsdGbp) : 0m,
            SavedGbp = 0m,
            Pricing = stamp,
        };
    }

    private static decimal Gbp(int tokens, decimal premiumUsdPerToken, decimal usdGbp)
        => tokens * premiumUsdPerToken * usdGbp;

    private static FixClass? CauseToFix(CauseClass cause) => cause switch
    {
        CauseClass.CrlfDrift => FixClass.LineEnding,
        CauseClass.TrailingWhitespace => FixClass.TrailingWhitespace,
        CauseClass.ToolDefinitionChurn or CauseClass.ConcatOrderChange => FixClass.ToolOrdering,
        _ => null,
    };

    /// <summary>First differing byte, or -1 when the two are identical.</summary>
    internal static long FirstDivergence(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            if (a[i] != b[i])
            {
                return i;
            }
        }

        return a.Length == b.Length ? -1 : n;
    }

    /// <summary>Classify by asking which safe normaliser would reconcile the two prefixes.</summary>
    internal static CauseClass Classify(byte[] current, byte[] previous)
    {
        var cur = TextNormalization.Decode(current);
        var prev = TextNormalization.Decode(previous);

        // Line endings only reconcile them -> CRLF drift (the proven wild win).
        if (TextNormalization.NormalizeLineEndings(cur) == TextNormalization.NormalizeLineEndings(prev))
        {
            return CauseClass.CrlfDrift;
        }

        // Line endings + trailing whitespace reconcile them -> trailing whitespace.
        var curTrim = TextNormalization.StripTrailingWhitespace(TextNormalization.NormalizeLineEndings(cur));
        var prevTrim = TextNormalization.StripTrailingWhitespace(TextNormalization.NormalizeLineEndings(prev));
        if (curTrim == prevTrim)
        {
            return CauseClass.TrailingWhitespace;
        }

        // Structural causes (tool ordering, volatile headers, timestamps) are refined in a follow-up
        // against the sibling vectors; anything not reconciled by a safe whitespace fix is a genuine edit.
        return CauseClass.GenuineEdit;
    }
}
