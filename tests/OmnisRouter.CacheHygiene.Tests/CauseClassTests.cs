using System.Text;
using OmnisRouter.CacheHygiene;

namespace OmnisRouter.CacheHygiene.Tests;

public class CauseClassTests
{
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Classifies_crlf_drift()
    {
        var prev = B("system prompt\nline two\ntools");
        var cur = B("system prompt\r\nline two\r\ntools");
        Assert.Equal(CauseClass.CrlfDrift, CacheHygieneAnalyzer.Classify(cur, prev));
    }

    [Fact]
    public void Classifies_lone_cr_as_crlf_drift()
    {
        Assert.Equal(CauseClass.CrlfDrift, CacheHygieneAnalyzer.Classify(B("a\rb"), B("a\nb")));
    }

    [Fact]
    public void Classifies_trailing_whitespace()
    {
        var prev = B("line one\nline two\n");
        var cur = B("line one   \nline two\t\n");
        Assert.Equal(CauseClass.TrailingWhitespace, CacheHygieneAnalyzer.Classify(cur, prev));
    }

    [Fact]
    public void A_real_content_change_is_a_genuine_edit()
    {
        Assert.Equal(CauseClass.GenuineEdit, CacheHygieneAnalyzer.Classify(B("you are a helpful bot"), B("you are a terse bot")));
    }

    [Theory]
    [InlineData(CauseClass.CrlfDrift, true)]
    [InlineData(CauseClass.TrailingWhitespace, true)]
    [InlineData(CauseClass.ToolDefinitionChurn, true)]
    [InlineData(CauseClass.GenuineEdit, false)]
    [InlineData(CauseClass.ModelChange, false)]
    [InlineData(CauseClass.SystemPromptChange, false)]
    public void Avoidability_matches_the_cause(CauseClass cause, bool avoidable)
    {
        Assert.Equal(avoidable, cause.IsAvoidable());
    }

    [Fact]
    public void Wire_labels_are_snake_case()
    {
        Assert.Equal("crlf_drift", CauseClass.CrlfDrift.Wire());
        Assert.Equal("trailing_whitespace", CauseClass.TrailingWhitespace.Wire());
        Assert.Equal("genuine_edit", CauseClass.GenuineEdit.Wire());
        Assert.Equal("line_ending", FixClass.LineEnding.Wire());
    }
}
