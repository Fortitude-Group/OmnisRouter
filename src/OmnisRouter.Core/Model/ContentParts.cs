namespace OmnisRouter.Core.Model;

/// <summary>
/// A single piece of message content in the neutral representation. Every ingress
/// adapter maps provider content blocks into these; every egress adapter maps back.
/// </summary>
public abstract record ContentPart;

/// <summary>Plain text content.</summary>
public sealed record TextPart(string Text) : ContentPart
{
    /// <summary>Optional cache breakpoint attached to this segment (Anthropic-style).</summary>
    public CacheDirective? Cache { get; init; }
}

/// <summary>
/// An image input. Exactly one of <see cref="Base64"/> or <see cref="Url"/> is set.
/// A remote <see cref="Url"/> is flagged for router-side fetch+re-encode when the
/// chosen provider cannot dereference it (see the capability guardrails).
/// </summary>
public sealed record ImagePart(string MediaType) : ContentPart
{
    public string? Base64 { get; init; }
    public string? Url { get; init; }

    /// <summary>OpenAI-only fidelity hint (low|high|auto); has no analog on other providers.</summary>
    public string? Detail { get; init; }
}

/// <summary>A model-emitted tool/function call.</summary>
public sealed record ToolUsePart(string Id, string Name, string InputJson) : ContentPart;

/// <summary>A tool result being fed back to the model. Correlates to a <see cref="ToolUsePart"/> by id.</summary>
public sealed record ToolResultPart(string ToolUseId, IReadOnlyList<ContentPart> Content) : ContentPart
{
    public bool IsError { get; init; }
}

/// <summary>
/// An extended-reasoning ("thinking") block. The <see cref="Signature"/> is provider- and
/// model-bound and cannot be replayed to a different model/provider (see research.md R2).
/// </summary>
public sealed record ThinkingPart : ContentPart
{
    public string? Text { get; init; }
    public string? Signature { get; init; }
    public bool Redacted { get; init; }

    /// <summary>Provider that produced the signature (provenance for continuity checks).</summary>
    public Provider? OriginProvider { get; init; }

    /// <summary>Model id that produced the signature.</summary>
    public string? OriginModelId { get; init; }
}

/// <summary>One turn in the conversation.</summary>
public sealed record Message(Role Role, IReadOnlyList<ContentPart> Parts);
