namespace OmnisRouter.Core.Model;

/// <summary>Capabilities a candidate model/provider supports; used by the routing guardrails.</summary>
[Flags]
public enum ModelCapabilities
{
    None = 0,
    Vision = 1 << 0,
    Tools = 1 << 1,
    ParallelTools = 1 << 2,
    StrictSchema = 1 << 3,
    PromptCachePin = 1 << 4,
    Thinking = 1 << 5,
    Streaming = 1 << 6,
}

/// <summary>
/// A concrete candidate model the router may choose. The pricing key references the pinned,
/// dated pricing snapshot so cost math is reproducible and explainable (Principle XII).
/// </summary>
public sealed record ModelRef(Provider Provider, string ModelId)
{
    public ModelCapabilities Capabilities { get; init; } = ModelCapabilities.None;

    /// <summary>Key into the pricing snapshot (defaults to "<provider>/<modelId>").</summary>
    public string PricingKey { get; init; } = $"{Provider}/{ModelId}";

    public bool Supports(ModelCapabilities cap) => (Capabilities & cap) == cap;

    public override string ToString() => $"{Provider.ToString().ToLowerInvariant()}/{ModelId}";
}
