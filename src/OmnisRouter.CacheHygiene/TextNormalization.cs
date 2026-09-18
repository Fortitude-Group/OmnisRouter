using System.Text;

namespace OmnisRouter.CacheHygiene;

/// <summary>
/// The provably-safe text transforms, shared by the analyzer's cause classifier (which asks "would
/// this normaliser make the two prefixes equal?") and the request normalisers (US3). Kept together so
/// classification and fixing can never disagree about what a fix does.
/// </summary>
public static class TextNormalization
{
    /// <summary>Normalise CRLF and lone CR to LF.</summary>
    public static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    /// <summary>Strip trailing spaces and tabs from each line.</summary>
    public static string StripTrailingWhitespace(string text)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd(' ', '\t');
        }

        return string.Join('\n', lines);
    }

    /// <summary>UTF-8 decode a prefix for text-level comparison; invalid bytes are replaced, not thrown.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);
}
