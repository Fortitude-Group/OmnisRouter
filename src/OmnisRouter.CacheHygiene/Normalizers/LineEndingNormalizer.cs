using OmnisRouter.Core.Model;

namespace OmnisRouter.CacheHygiene;

/// <summary>
/// Normalises CRLF and lone CR to LF in the system blocks and tool definitions. Safe: line-ending
/// style is not semantically meaningful to the model. This is the fix for the wild CRLF cache-hostility
/// CacheScope found in instruction files.
/// </summary>
public sealed class LineEndingNormalizer : INormalizer
{
    public FixClass Class => FixClass.LineEnding;

    public bool TryNormalize(ChatRequest request, out ChatRequest normalised) =>
        PrefixRewrite.Apply(request, TextNormalization.NormalizeLineEndings, out normalised);
}
