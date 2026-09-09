using OmnisRouter.ClientLink;

namespace OmnisRouter.ClientLink.Tests;

public class ClaudeCodeLinkTests
{
    private const string Root = "http://127.0.0.1:8787";
    private const string Token = "sk-omr-test-token";

    private static ClaudeCodeLink CreateLink() => new(homeDirectory: @"C:\fake-home");

    [Fact]
    public void Connect_MergesIntoExistingEnv_PreservingUnrelatedKeys()
    {
        var currentContent = """
            {
              "unrelatedTopLevel": "keep-me",
              "env": {
                "SOME_OTHER_VAR": "unchanged"
              }
            }
            """;

        var result = CreateLink().Connect(Root, Token, currentContent);

        var expected = """
            {
              "unrelatedTopLevel": "keep-me",
              "env": {
                "SOME_OTHER_VAR": "unchanged",
                "ANTHROPIC_BASE_URL": "http://127.0.0.1:8787",
                "ANTHROPIC_AUTH_TOKEN": "sk-omr-test-token"
              }
            }
            """.Replace("\r\n", "\n") + "\n";

        Assert.Equal(expected, result.NewFileContent);
    }

    [Fact]
    public void Connect_WhenFileAbsent_CreatesEnvWithBothKeys()
    {
        var result = CreateLink().Connect(Root, Token, currentContent: null);

        var expected = """
            {
              "env": {
                "ANTHROPIC_BASE_URL": "http://127.0.0.1:8787",
                "ANTHROPIC_AUTH_TOKEN": "sk-omr-test-token"
              }
            }
            """.Replace("\r\n", "\n") + "\n";

        Assert.Equal(expected, result.NewFileContent);
    }

    [Fact]
    public void Connect_CapturesPriorState_ForPresentAndAbsentKeys()
    {
        var currentContent = """
            {
              "env": {
                "ANTHROPIC_BASE_URL": "http://old-value.example"
              }
            }
            """;

        var result = CreateLink().Connect(Root, Token, currentContent);

        Assert.Equal("http://old-value.example", result.PriorState.PriorValues["ANTHROPIC_BASE_URL"]);
        Assert.True(result.PriorState.PriorValues.ContainsKey("ANTHROPIC_AUTH_TOKEN"));
        Assert.Null(result.PriorState.PriorValues["ANTHROPIC_AUTH_TOKEN"]);
    }

    [Fact]
    public void Revert_AfterConnect_RestoresOriginalContentExactly()
    {
        var link = CreateLink();
        var originalContent = """
            {
              "unrelatedTopLevel": "keep-me",
              "env": {
                "SOME_OTHER_VAR": "unchanged",
                "ANTHROPIC_BASE_URL": "http://old-value.example"
              }
            }
            """.Replace("\r\n", "\n") + "\n";

        var connectResult = link.Connect(Root, Token, originalContent);
        var reverted = link.Revert(connectResult.NewFileContent, connectResult.PriorState);

        Assert.Equal(originalContent, reverted);
    }

    [Fact]
    public void Connect_WithInvalidJson_ThrowsClientLinkException()
    {
        var link = CreateLink();

        Assert.Throws<ClientLinkException>(() => link.Connect(Root, Token, "{ not json"));
    }

    [Fact]
    public void Connect_IsIdempotent_WhenFedItsOwnOutputAgain()
    {
        var link = CreateLink();

        var first = link.Connect(Root, Token, currentContent: null);
        var second = link.Connect(Root, Token, first.NewFileContent);

        Assert.Equal(first.NewFileContent, second.NewFileContent);
    }
}
