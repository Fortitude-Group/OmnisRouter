using OmnisRouter.Adapters.Tests.Fixtures;

namespace OmnisRouter.Adapters.Tests;

public class FixtureLoaderTests
{
    [Fact]
    public void ReadJson_ParsesOpenAiChatCompletionFixture()
    {
        using var doc = FixtureLoader.ReadJson("openai-chat-completion-nonstream.json");

        Assert.Equal("chat.completion", doc.RootElement.GetProperty("object").GetString());
        Assert.Equal("stop", doc.RootElement.GetProperty("choices")[0].GetProperty("finish_reason").GetString());
    }

    [Fact]
    public void ReadJson_ParsesAnthropicMessagesFixture()
    {
        using var doc = FixtureLoader.ReadJson("anthropic-messages-response.json");

        Assert.Equal("message", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("end_turn", doc.RootElement.GetProperty("stop_reason").GetString());
    }

    [Fact]
    public void ReadJson_ParsesGeminiGenerateContentFixture()
    {
        using var doc = FixtureLoader.ReadJson("gemini-generate-content-response.json");

        Assert.Equal(
            "STOP",
            doc.RootElement.GetProperty("candidates")[0].GetProperty("finishReason").GetString());
    }

    [Fact]
    public void ReadText_MissingFixture_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() => FixtureLoader.ReadText("does-not-exist.json"));
    }
}
