namespace OmnisRouter.Core.Model;

/// <summary>A cache breakpoint marker (Anthropic <c>cache_control</c> semantics).</summary>
public sealed record CacheDirective(CacheTtl Ttl = CacheTtl.FiveMinutes);

/// <summary>A tool/function the model may call.</summary>
public sealed record Tool(string Name, string Description, string JsonSchema)
{
    /// <summary>Request a guaranteed-valid-JSON contract (Anthropic/OpenAI only; no Gemini analog).</summary>
    public bool Strict { get; init; }
}

/// <summary>Constraint on tool selection.</summary>
public sealed record ToolChoice(ToolChoiceKind Kind)
{
    /// <summary>Tool name when <see cref="Kind"/> is <see cref="ToolChoiceKind.Specific"/>.</summary>
    public string? Name { get; init; }

    public static readonly ToolChoice Auto = new(ToolChoiceKind.Auto);
    public static readonly ToolChoice Any = new(ToolChoiceKind.Any);
    public static readonly ToolChoice None = new(ToolChoiceKind.None);

    public static ToolChoice ForTool(string name) => new(ToolChoiceKind.Specific) { Name = name };
}

/// <summary>Reasoning/"thinking" directives for the request.</summary>
public sealed record ThinkingConfig
{
    public bool Enabled { get; init; }
    public ReasoningEffort? Effort { get; init; }

    /// <summary>Numeric token budget (Anthropic legacy / Gemini <c>thinkingBudget</c>); no clean OpenAI-enum mapping.</summary>
    public int? BudgetTokens { get; init; }
}

/// <summary>
/// The capabilities a concrete request actually exercises. Derived at ingress and used by the
/// capability guardrails to refuse (rather than silently drop) when a candidate can't honor one.
/// </summary>
[Flags]
public enum RequestCapabilities
{
    None = 0,
    Vision = 1 << 0,
    RemoteImageUrl = 1 << 1,
    Tools = 1 << 2,
    ParallelSameTool = 1 << 3,
    StrictSchema = 1 << 4,
    CachePinGuaranteed = 1 << 5,
    Thinking = 1 << 6,
    ThinkingWithSignature = 1 << 7,
    NumericReasoningBudget = 1 << 8,
}

/// <summary>
/// The provider-neutral request. Every ingress adapter normalizes to this; every egress adapter
/// produces an upstream request from it. Provider quirks live in the adapters, never here.
/// </summary>
public sealed record ChatRequest
{
    /// <summary>Client-requested model (may be a routing alias like "auto"); routing may override.</summary>
    public string? Model { get; init; }

    public IReadOnlyList<TextPart> System { get; init; } = [];

    public required IReadOnlyList<Message> Messages { get; init; }

    public IReadOnlyList<Tool> Tools { get; init; } = [];

    public ToolChoice? ToolChoice { get; init; }

    public ThinkingConfig? Thinking { get; init; }

    public bool Stream { get; init; }

    public int? MaxTokens { get; init; }

    public double? Temperature { get; init; }

    /// <summary>Session identity for cache pinning (client-supplied header or derived; see ISessionPinner).</summary>
    public string? SessionId { get; init; }

    /// <summary>Capabilities this request exercises (drives guardrails). Derived at ingress.</summary>
    public RequestCapabilities CapabilitiesUsed { get; init; } = RequestCapabilities.None;

    /// <summary>The wire format the client used; the response is returned in this same format.</summary>
    public required ClientFormat OriginFormat { get; init; }
}
