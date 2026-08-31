namespace OmnisRouter.Vigil;

/// <summary>The tenant-wide policy served by OmnisVigil (GET /v1/policy), enforced locally by the router.</summary>
public sealed record VigilPolicy
{
    public string PolicyVersion { get; init; } = "";
    public PolicyCaps Caps { get; init; } = new();
    public IReadOnlyList<string> AllowedModels { get; init; } = [];

    /// <summary>Per-project allowed-model overrides, keyed by project tag. Empty ⇒ tenant-wide only.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> AllowedModelsByProject { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>();

    public double? ConfidenceFloor { get; init; }
    public PolicyKill Kill { get; init; } = new();
}

/// <summary>
/// Raw caps plus OmnisVigil's authoritative cross-router spend. The router resolves the most
/// restrictive ceiling and enforces it against the fleet total (spent) plus its own spend since the
/// last poll, so a fleet under one shared cap cannot collectively overshoot it.
/// </summary>
public sealed record PolicyCaps
{
    public decimal? MonthlyUsd { get; init; }
    public IReadOnlyDictionary<string, decimal> PerProjectUsd { get; init; } = new Dictionary<string, decimal>();
    public decimal SpentUsd { get; init; }
    public IReadOnlyDictionary<string, decimal> SpentPerProjectUsd { get; init; } = new Dictionary<string, decimal>();
}

/// <summary>Kill state. <see cref="Org"/> halts the whole fleet. <see cref="Teams"/> is reserved (per-team kill is a follow-on).</summary>
public sealed record PolicyKill
{
    public bool Org { get; init; }
    public IReadOnlyList<string> Teams { get; init; } = [];
}
