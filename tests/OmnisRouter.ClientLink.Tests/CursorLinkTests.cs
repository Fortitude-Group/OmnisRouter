using OmnisRouter.ClientLink;

namespace OmnisRouter.ClientLink.Tests;

public class CursorLinkTests
{
    private const string Root = "http://127.0.0.1:8787";
    private const string Token = "sk-omr-test-token";

    [Fact]
    public void Connect_ReturnsDisplayValuesAndNoFileContent()
    {
        var link = new CursorLink();

        var result = link.Connect(Root, Token, currentContent: null);

        Assert.Null(result.NewFileContent);
        Assert.Equal(Root + "/v1", result.DisplayValues["OPENAI_BASE_URL"]);
        Assert.Equal(Token, result.DisplayValues["OPENAI_API_KEY"]);
        Assert.Null(link.ConfigPath);
        Assert.Equal(ClientKind.Cursor, link.Kind);
    }

    [Fact]
    public void Connect_IsIdempotentAcrossReconnect()
    {
        var link = new CursorLink();

        var first = link.Connect(Root, Token, currentContent: null);
        var second = link.Connect(Root, Token, currentContent: null);

        Assert.Equal(first.DisplayValues, second.DisplayValues);
        Assert.Null(first.NewFileContent);
        Assert.Null(second.NewFileContent);
    }

    [Fact]
    public void Revert_ReturnsNullAndDoesNotThrow()
    {
        var link = new CursorLink();

        var result = link.Revert(currentContent: null, priorState: new ClientPriorState());

        Assert.Null(result);
    }
}
