namespace OmnisRouter.CacheHygiene;

/// <summary>
/// The analyzer's content-free verdict for one request. A C# port of the sibling CacheScope RESULT
/// contract (research D7); every £ carries a <see cref="PricingStamp"/>. See data-model.md.
///
/// Two shapes matter: a miss the caller could recover (<see cref="WasteGbp"/> &gt; 0, no fix), and a
/// miss a fix prevented (<see cref="SavedGbp"/> &gt; 0, <see cref="FixApplied"/> set). An unavoidable
/// miss carries a cause but zero waste.
/// </summary>
public sealed record CacheHygieneResult
{
    /// <summary>The sibling RESULT contract version this conforms to.</summary>
    public const string ResultVersion = "1.0.0";

    /// <summary>True when the prefix diverged from the lineage's previous request.</summary>
    public bool Missed { get; init; }

    /// <summary>The cause of the miss, when there was one.</summary>
    public CauseClass? Cause { get; init; }

    /// <summary>Derived from <see cref="Cause"/>; false for genuine edits and deliberate changes.</summary>
    public bool Avoidable { get; init; }

    /// <summary>First divergent byte between this prefix and the previous one, when missed.</summary>
    public long? DivergenceOffset { get; init; }

    /// <summary>Tokens re-written at cache-write price on this miss (from real usage). Zero when a fix prevented the miss.</summary>
    public int RecomputedTokens { get; init; }

    /// <summary>The fix that ran and prevented the miss, if any.</summary>
    public FixClass? FixApplied { get; init; }

    /// <summary>Tokens the fix turned from a write into a read.</summary>
    public int SavedTokens { get; init; }

    /// <summary>Avoidable-miss cost: <see cref="RecomputedTokens"/> at the write premium; zero when unavoidable.</summary>
    public decimal WasteGbp { get; init; }

    /// <summary>Value of <see cref="SavedTokens"/> at the write premium.</summary>
    public decimal SavedGbp { get; init; }

    /// <summary>The stamp on every £ above.</summary>
    public PricingStamp? Pricing { get; init; }

    /// <summary>A no-miss result (a hit, or the first request in a lineage) — nothing to report.</summary>
    public static readonly CacheHygieneResult NoMiss = new() { Missed = false };
}
