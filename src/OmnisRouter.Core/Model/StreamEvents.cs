namespace OmnisRouter.Core.Model;

/// <summary>
/// Provider-neutral streaming events. Upstream clients yield these; egress adapters re-frame them
/// into each client format's SSE shape (Anthropic block bracketing, OpenAI delta chunks, Gemini
/// whole-object chunks). This is the single pivot for cross-format streaming translation.
/// </summary>
public abstract record NeutralStreamEvent;

/// <summary>Start of the response message.</summary>
public sealed record StreamMessageStart(ModelRef ServedBy) : NeutralStreamEvent;

/// <summary>Open a content block at the given index (text or tool-use).</summary>
public sealed record StreamBlockStart(int Index, string BlockKind, string? ToolId = null, string? ToolName = null)
    : NeutralStreamEvent;

/// <summary>Incremental text for the block at <paramref name="Index"/>.</summary>
public sealed record StreamTextDelta(int Index, string Text) : NeutralStreamEvent;

/// <summary>Incremental tool-call argument JSON for the block at <paramref name="Index"/>.</summary>
public sealed record StreamToolArgsDelta(int Index, string PartialJson) : NeutralStreamEvent;

/// <summary>Incremental thinking text (+ optional signature on completion).</summary>
public sealed record StreamThinkingDelta(int Index, string? Text = null, string? Signature = null)
    : NeutralStreamEvent;

/// <summary>Close the content block at <paramref name="Index"/>.</summary>
public sealed record StreamBlockStop(int Index) : NeutralStreamEvent;

/// <summary>Terminal event carrying stop reason and final usage.</summary>
public sealed record StreamMessageStop(StopReason StopReason, Usage Usage) : NeutralStreamEvent;

/// <summary>An in-band error surfaced mid-stream (e.g. upstream overloaded).</summary>
public sealed record StreamError(string Type, string Message) : NeutralStreamEvent;
