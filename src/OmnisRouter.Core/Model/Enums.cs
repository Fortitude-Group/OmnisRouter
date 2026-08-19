namespace OmnisRouter.Core.Model;

/// <summary>Upstream provider that hosts a candidate model.</summary>
public enum Provider
{
    Anthropic,
    OpenAI,
    Gemini,
    OpenRouter,
}

/// <summary>Wire format a client speaks (the ingress/egress adapter family).</summary>
public enum ClientFormat
{
    Anthropic,
    OpenAI,
    Gemini,
}

/// <summary>Role of a message in the neutral conversation model.</summary>
public enum Role
{
    System,
    User,
    Assistant,
    Tool,
}

/// <summary>Reasoning/"thinking" effort level (provider-neutral bucket).</summary>
public enum ReasoningEffort
{
    None,
    Low,
    Medium,
    High,
    Max,
}

/// <summary>Cache-directive time-to-live (Anthropic breakpoint semantics).</summary>
public enum CacheTtl
{
    FiveMinutes,
    OneHour,
}

/// <summary>How the model must select tools.</summary>
public enum ToolChoiceKind
{
    Auto,
    Any,
    None,
    Specific,
}

/// <summary>Why a request-level generation stopped.</summary>
public enum StopReason
{
    EndTurn,
    ToolUse,
    MaxTokens,
    StopSequence,
    Refusal,
    Error,
}
