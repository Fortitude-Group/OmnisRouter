namespace OmnisRouter.Core.Model;

/// <summary>Token accounting for a request, including cache economics.</summary>
public sealed record Usage
{
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int CacheCreationTokens { get; init; }
    public int CacheReadTokens { get; init; }
}

/// <summary>
/// The provider-neutral, non-streaming response. Egress adapters translate this back into the
/// client's original wire format; the routing receipt is attached out-of-band (headers/log).
/// </summary>
public sealed record ChatResponse
{
    public required IReadOnlyList<ContentPart> Content { get; init; }
    public StopReason StopReason { get; init; } = StopReason.EndTurn;
    public Usage Usage { get; init; } = new();

    /// <summary>Model that actually served the request (provider/model id).</summary>
    public ModelRef? ServedBy { get; init; }
}
