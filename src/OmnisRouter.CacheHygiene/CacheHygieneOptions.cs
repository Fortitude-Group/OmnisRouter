namespace OmnisRouter.CacheHygiene;

/// <summary>
/// Cache-hygiene configuration. Measurement is on by default and content-free; every byte-mutating fix
/// is off until the operator enables it per class (spec FR-005/FR-009). Fix enablement is also settable
/// by an OmnisVigil policy the way caps/kill-state are (FR-012).
/// </summary>
public sealed class CacheHygieneOptions
{
    public const string SectionName = "CacheHygiene";

    /// <summary>Measure cache waste. On by default; free and content-free.</summary>
    public bool MeasurementEnabled { get; set; } = true;

    /// <summary>Fix classes the operator has turned on. Empty by default — nothing mutates the request.</summary>
    public HashSet<FixClass> EnabledFixes { get; set; } = new();

    /// <summary>Emit the content-free cache_waste block onward. Gated so an older Vigil never rejects a receipt (research D5).</summary>
    public bool EmitToVigil { get; set; }

    /// <summary>
    /// How the operator pays the upstream provider. Metered API keys are pay-as-you-go, so a cache miss
    /// is a real bill and the pounds are charged. A flat-rate subscription pays nothing per token, so the
    /// same figures are shadow-priced — shown as what a miss <em>would</em> cost, never as a bill (FR-016,
    /// SC-005). This is a deployment fact, not something the request bytes reveal, so it is configured, not
    /// detected. Routed traffic through metered keys is pay-as-you-go by default.
    /// </summary>
    public BillingModel Billing { get; set; } = BillingModel.PayAsYouGo;

    /// <summary>Time budget for in-path normalisation; over it, the fix is skipped and the request forwarded unchanged.</summary>
    public TimeSpan NormalizationBudget { get; set; } = TimeSpan.FromMilliseconds(5);

    /// <summary>Bound on the in-memory lineage cache.</summary>
    public int MaxLineageEntries { get; set; } = 2048;

    /// <summary>Age bound on a lineage entry.</summary>
    public TimeSpan MaxLineageAge { get; set; } = TimeSpan.FromHours(2);

    public bool IsFixEnabled(FixClass fix) => EnabledFixes.Contains(fix);
}
