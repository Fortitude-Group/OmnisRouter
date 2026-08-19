using System.Text.RegularExpressions;

namespace OmnisRouter.Telemetry.Redaction;

/// <summary>
/// Defensive, belt-and-suspenders text scrubber. OmnisRouter's application code already avoids
/// logging prompts or provider keys (FR-014), but this gives every layer — including third-party
/// libraries and future call sites — a last line of defense: any string that happens to contain
/// something shaped like a secret gets masked before it reaches a log, receipt, or export.
/// </summary>
/// <remarks>
/// Deliberately conservative: the patterns target the shapes of real secrets (provider API key
/// prefixes, bearer tokens, long base64/hex blobs) with length floors chosen to avoid matching
/// short, ordinary tokens (words, short ids, GUIDs-without-dashes). Ordinary prose is left intact.
/// </remarks>
public static class SecretRedactor
{
    /// <summary>Replacement text substituted for anything that looks like a secret.</summary>
    public const string Mask = "***redacted***";

    // OpenAI-style keys (sk-...) and Anthropic-style keys (sk-ant-...) share the sk- prefix, so one
    // pattern covers both. 16+ chars after the prefix comfortably exceeds real key lengths while
    // staying well above anything a short ordinary token would produce.
    private static readonly Regex OpenAiStyleKeyPattern = new(
        @"sk-(?:ant-)?[A-Za-z0-9_-]{16,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // "Bearer <token>" authorization header values. Runs after the provider-key pattern so a
    // "Bearer sk-..." value is already reduced to "Bearer ***redacted***" by the time this pattern
    // would otherwise apply (Mask contains characters outside the token class, so the second pass
    // is a no-op on already-redacted text).
    private static readonly Regex BearerTokenPattern = new(
        @"\bBearer\s+[A-Za-z0-9\-_.=]{8,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // Generic catch-all for long base64/hex blobs (e.g. arbitrary API secrets that don't follow a
    // known provider's prefix convention). The 40-char floor is chosen so it does NOT catch a
    // 32-char GUID-without-dashes or other common, non-secret identifiers, while still catching
    // sha1-length hex (40) and realistically-sized base64 secrets. Boundaries are explicit
    // lookarounds (not \b) because base64's own alphabet includes non-word characters (+, /, =),
    // so a plain \b at the tail — right after '=' padding — fails to anchor there and lets the
    // match get silently truncated before the padding.
    private static readonly Regex LongOpaqueTokenPattern = new(
        @"(?<![A-Za-z0-9+/=])[A-Za-z0-9+/]{40,}={0,2}(?![A-Za-z0-9+/=])" +
        @"|(?<![0-9a-fA-F])[0-9a-fA-F]{40,}(?![0-9a-fA-F])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns <paramref name="input"/> with anything matching a known secret shape replaced by
    /// <see cref="Mask"/>. Null/empty input is returned as an empty string. Ordinary text with no
    /// secret-shaped substrings is returned unchanged (reference-different but content-equal).
    /// </summary>
    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var redacted = OpenAiStyleKeyPattern.Replace(input, Mask);
        redacted = BearerTokenPattern.Replace(redacted, $"Bearer {Mask}");
        redacted = LongOpaqueTokenPattern.Replace(redacted, Mask);

        return redacted;
    }
}
