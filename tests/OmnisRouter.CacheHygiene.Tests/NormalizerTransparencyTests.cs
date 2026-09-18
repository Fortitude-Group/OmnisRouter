using System.Text.Json;
using OmnisRouter.CacheHygiene;
using OmnisRouter.Core.Model;

namespace OmnisRouter.CacheHygiene.Tests;

/// <summary>
/// SC-004: a fix is response-transparent — it rewrites only non-semantic bytes (line endings, trailing
/// whitespace, tool ordering and JSON key order), so the model is handed the same request and returns an
/// equivalent response. These tests prove the rewrites never touch meaning: message prose is left alone,
/// array order (which can be significant) is preserved, and anything a normaliser cannot prove safe to
/// reorder is passed through byte-for-byte rather than mangled.
/// </summary>
public class NormalizerTransparencyTests
{
    private static ChatRequest Request(
        IReadOnlyList<TextPart>? system = null, IReadOnlyList<Tool>? tools = null, IReadOnlyList<Message>? messages = null) => new()
    {
        Messages = messages ?? [],
        OriginFormat = ClientFormat.Anthropic,
        System = system ?? [],
        Tools = tools ?? [],
    };

    [Fact]
    public void Line_ending_fix_changes_only_line_ending_bytes()
    {
        var original = "alpha\r\nbeta\rgamma";
        new LineEndingNormalizer().TryNormalize(Request(system: [new TextPart(original)]), out var norm);

        // Stripping the CRs from the original yields exactly the normalised text: nothing else moved.
        Assert.Equal(original.Replace("\r\n", "\n").Replace("\r", "\n"), norm.System[0].Text);
        // The visible characters are identical once newlines are set aside.
        Assert.Equal(
            original.Replace("\r", "").Replace("\n", ""),
            norm.System[0].Text.Replace("\n", ""));
    }

    [Fact]
    public void Trailing_whitespace_fix_preserves_interior_content()
    {
        new TrailingWhitespaceNormalizer().TryNormalize(Request(system: [new TextPart("a  b   \nc\td\t")]), out var norm);

        // Interior spacing ("a  b", "c\td") is untouched; only end-of-line runs are trimmed.
        Assert.Equal("a  b\nc\td", norm.System[0].Text);
    }

    [Fact]
    public void Fixes_never_touch_message_prose()
    {
        var req = Request(
            system: [new TextPart("sys  \r\n")],
            messages: [new Message(Role.User, [new TextPart("the user's\r\nexact  words   ")])]);

        new LineEndingNormalizer().TryNormalize(req, out var afterLe);
        new TrailingWhitespaceNormalizer().TryNormalize(afterLe, out var afterWs);

        Assert.Equal("the user's\r\nexact  words   ", ((TextPart)afterWs.Messages[0].Parts[0]).Text);
    }

    [Fact]
    public void Tool_ordering_hands_the_model_the_same_set_with_equivalent_schemas()
    {
        var req = Request(tools:
        [
            new Tool("zebra", "z", """{"b":1,"a":{"d":4,"c":3}}"""),
            new Tool("alpha", "a", """{"x":1}"""),
        ]);

        new ToolOrderingNormalizer().TryNormalize(req, out var norm);

        // Same tools, by name — nothing added or dropped.
        Assert.Equal(["alpha", "zebra"], norm.Tools.Select(t => t.Name));
        // Each schema is semantically identical to its original (only key order differs).
        var originalZebra = JsonDocument.Parse("""{"b":1,"a":{"d":4,"c":3}}""");
        var normalisedZebra = JsonDocument.Parse(norm.Tools.Single(t => t.Name == "zebra").JsonSchema);
        Assert.True(JsonEquivalent(originalZebra.RootElement, normalisedZebra.RootElement));
    }

    [Fact]
    public void Tool_ordering_leaves_an_unparseable_schema_byte_identical()
    {
        // A schema the canonicaliser cannot parse is a case where safety cannot be shown: it must be
        // forwarded exactly, never half-rewritten.
        const string broken = "{not valid json";
        var req = Request(tools:
        [
            new Tool("beta", "b", broken),
            new Tool("alpha", "a", """{"a":1}"""),
        ]);

        new ToolOrderingNormalizer().TryNormalize(req, out var norm);

        Assert.Equal(broken, norm.Tools.Single(t => t.Name == "beta").JsonSchema);
    }

    // Order-insensitive structural equality: object keys may be reordered, everything else must match.
    private static bool JsonEquivalent(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var aProps = a.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
                var bProps = b.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
                return aProps.Count == bProps.Count
                    && aProps.Zip(bProps).All(pair => pair.First.Name == pair.Second.Name
                        && JsonEquivalent(pair.First.Value, pair.Second.Value));
            case JsonValueKind.Array:
                var aItems = a.EnumerateArray().ToList();
                var bItems = b.EnumerateArray().ToList();
                return aItems.Count == bItems.Count
                    && aItems.Zip(bItems).All(pair => JsonEquivalent(pair.First, pair.Second));
            default:
                return a.GetRawText() == b.GetRawText();
        }
    }
}
