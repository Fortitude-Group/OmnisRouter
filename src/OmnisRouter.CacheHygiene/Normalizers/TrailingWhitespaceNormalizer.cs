using OmnisRouter.Core.Model;

namespace OmnisRouter.CacheHygiene;

/// <summary>
/// Strips trailing spaces and tabs from each line of the system blocks and tool definitions. Safe:
/// trailing whitespace carries no meaning to the model, and a JSON schema is whitespace-insensitive
/// between tokens. Message content is never touched.
/// </summary>
public sealed class TrailingWhitespaceNormalizer : INormalizer
{
    public FixClass Class => FixClass.TrailingWhitespace;

    public bool TryNormalize(ChatRequest request, out ChatRequest normalised) =>
        PrefixRewrite.Apply(request, TextNormalization.StripTrailingWhitespace, out normalised);
}
