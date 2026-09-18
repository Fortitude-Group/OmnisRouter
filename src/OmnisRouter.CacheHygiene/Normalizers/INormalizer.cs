using OmnisRouter.Core.Model;

namespace OmnisRouter.CacheHygiene;

/// <summary>
/// A provably-safe request fix. Operates only on the cacheable prefix — the system blocks and tool
/// definitions — never on message content (the user's prose is warn-only, per the design's non-goals).
/// Returns false when it cannot prove this request is safe to touch, or when there is nothing to change,
/// so an unproven request is forwarded unchanged (spec FR-009).
/// </summary>
public interface INormalizer
{
    FixClass Class { get; }

    bool TryNormalize(ChatRequest request, out ChatRequest normalised);
}
