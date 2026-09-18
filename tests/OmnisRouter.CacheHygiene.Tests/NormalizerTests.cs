using OmnisRouter.CacheHygiene;
using OmnisRouter.Core.Model;

namespace OmnisRouter.CacheHygiene.Tests;

public class NormalizerTests
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
    public void Line_ending_normalizer_rewrites_crlf_in_system_to_lf()
    {
        var req = Request(system: [new TextPart("a\r\nb\rc")]);
        Assert.True(new LineEndingNormalizer().TryNormalize(req, out var norm));
        Assert.Equal("a\nb\nc", norm.System[0].Text);
    }

    [Fact]
    public void Line_ending_normalizer_leaves_message_prose_alone()
    {
        // Message content is the user's prose — never mutated (design non-goal), even with CRLF.
        var req = Request(
            system: [new TextPart("sys\r\n")],
            messages: [new Message(Role.User, [new TextPart("user wrote\r\nthis")])]);

        Assert.True(new LineEndingNormalizer().TryNormalize(req, out var norm));
        Assert.Equal("user wrote\r\nthis", ((TextPart)norm.Messages[0].Parts[0]).Text);   // untouched
    }

    [Fact]
    public void Line_ending_normalizer_is_a_no_op_when_already_lf()
    {
        Assert.False(new LineEndingNormalizer().TryNormalize(Request(system: [new TextPart("clean\nlines")]), out _));
    }

    [Fact]
    public void Trailing_whitespace_normalizer_strips_line_ends()
    {
        var req = Request(system: [new TextPart("line one   \nline two\t")]);
        Assert.True(new TrailingWhitespaceNormalizer().TryNormalize(req, out var norm));
        Assert.Equal("line one\nline two", norm.System[0].Text);
    }

    [Fact]
    public void Tool_ordering_normalizer_sorts_by_name_and_canonicalises_keys()
    {
        var req = Request(tools:
        [
            new Tool("zebra", "z", """{"b":1,"a":2}"""),
            new Tool("alpha", "a", """{"x":1}"""),
        ]);

        Assert.True(new ToolOrderingNormalizer().TryNormalize(req, out var norm));
        Assert.Equal("alpha", norm.Tools[0].Name);
        Assert.Equal("zebra", norm.Tools[1].Name);
        Assert.Equal("""{"a":2,"b":1}""", norm.Tools[1].JsonSchema);   // keys sorted
    }

    [Fact]
    public void Tool_ordering_normalizer_is_a_no_op_when_already_canonical()
    {
        var req = Request(tools: [new Tool("alpha", "a", """{"a":1}"""), new Tool("beta", "b", """{"b":1}""")]);
        Assert.False(new ToolOrderingNormalizer().TryNormalize(req, out _));
    }

    [Fact]
    public void Canonicalising_preserves_array_order()
    {
        // Array order can be meaningful; only object keys are reordered.
        var req = Request(tools: [new Tool("t", "d", """{"required":["b","a"],"z":1,"a":2}""")]);
        Assert.True(new ToolOrderingNormalizer().TryNormalize(req, out var norm));
        Assert.Equal("""{"a":2,"required":["b","a"],"z":1}""", norm.Tools[0].JsonSchema);
    }
}
