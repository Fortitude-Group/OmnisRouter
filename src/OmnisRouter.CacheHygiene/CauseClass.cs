namespace OmnisRouter.CacheHygiene;

/// <summary>
/// Why a cached prefix missed. Mirrors the sibling CacheScope tool's cause set so the two analyses
/// agree (research D7). The last three are unavoidable — the cache correctly busting on a real change.
/// </summary>
public enum CauseClass
{
    CrlfDrift,
    TrailingWhitespace,
    VolatileHeader,
    TimestampInjection,
    ConcatOrderChange,
    ToolDefinitionChurn,
    ModelChange,
    SystemPromptChange,
    GenuineEdit,
}

public static class CauseClassExtensions
{
    /// <summary>A genuine edit, a deliberate model change, and a system-prompt change are not avoidable.</summary>
    public static bool IsAvoidable(this CauseClass cause) => cause is not (
        CauseClass.ModelChange or CauseClass.SystemPromptChange or CauseClass.GenuineEdit);

    /// <summary>Snake-case wire label used in the receipt and the content-free ingest block.</summary>
    public static string Wire(this CauseClass cause) => cause switch
    {
        CauseClass.CrlfDrift => "crlf_drift",
        CauseClass.TrailingWhitespace => "trailing_whitespace",
        CauseClass.VolatileHeader => "volatile_header",
        CauseClass.TimestampInjection => "timestamp_injection",
        CauseClass.ConcatOrderChange => "concat_order_change",
        CauseClass.ToolDefinitionChurn => "tool_definition_churn",
        CauseClass.ModelChange => "model_change",
        CauseClass.SystemPromptChange => "system_prompt_change",
        CauseClass.GenuineEdit => "genuine_edit",
        _ => "genuine_edit",
    };
}

/// <summary>The provably-safe fixes. Snake-case wire labels via <see cref="FixClassExtensions.Wire"/>.</summary>
public enum FixClass
{
    LineEnding,
    TrailingWhitespace,
    ToolOrdering,
}

public static class FixClassExtensions
{
    public static string Wire(this FixClass fix) => fix switch
    {
        FixClass.LineEnding => "line_ending",
        FixClass.TrailingWhitespace => "trailing_whitespace",
        FixClass.ToolOrdering => "tool_ordering",
        _ => "line_ending",
    };
}

/// <summary>How the caller is billed, which drives the shadow-price marker.</summary>
public enum BillingModel
{
    PayAsYouGo,
    Subscription,
}
