using System.Net.ServerSentEvents;
using System.Text.Json;
using OmnisRouter.Adapters.OpenAI;
using OmnisRouter.Adapters.Tests.Fixtures;
using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;

namespace OmnisRouter.Adapters.Tests;

/// <summary>
/// Golden-file round-trip conformance for the OpenAI CLIENT-format adapter (T021): ingress parsing
/// of an OpenAI Chat Completions request into the neutral model, egress rendering of a neutral
/// response back into OpenAI shape, and SSE re-framing of neutral stream events into
/// <c>chat.completion.chunk</c> frames terminated by <c>[DONE]</c>.
/// </summary>
public class OpenAiConformanceTests
{
    private readonly OpenAiAdapter _adapter = new();

    [Fact]
    public void ToInternal_ParsesRepresentativeOpenAiRequest()
    {
        using var doc = FixtureLoader.ReadJson("openai-chat-completion-request.json");

        var request = _adapter.ToInternal(doc.RootElement);

        Assert.Equal(ClientFormat.OpenAI, request.OriginFormat);
        Assert.Equal("gpt-5-mini", request.Model);
        Assert.False(request.Stream);
        Assert.Equal(256, request.MaxTokens);
        Assert.Equal(0.2, request.Temperature);

        var system = Assert.Single(request.System);
        Assert.Equal("You are a helpful assistant.", system.Text);

        Assert.Equal(3, request.Messages.Count);

        var userMessage = request.Messages[0];
        Assert.Equal(Role.User, userMessage.Role);
        var userText = Assert.IsType<TextPart>(userMessage.Parts[0]);
        Assert.Equal("What's in this image?", userText.Text);
        var image = Assert.IsType<ImagePart>(userMessage.Parts[1]);
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal("iVBORw0KGgo=", image.Base64);
        Assert.Null(image.Url);
        Assert.Equal("high", image.Detail);

        var assistantMessage = request.Messages[1];
        Assert.Equal(Role.Assistant, assistantMessage.Role);
        var toolUse = Assert.IsType<ToolUsePart>(Assert.Single(assistantMessage.Parts));
        Assert.Equal("call_1", toolUse.Id);
        Assert.Equal("get_weather", toolUse.Name);
        Assert.Contains("NYC", toolUse.InputJson);

        var toolMessage = request.Messages[2];
        Assert.Equal(Role.Tool, toolMessage.Role);
        var toolResult = Assert.IsType<ToolResultPart>(Assert.Single(toolMessage.Parts));
        Assert.Equal("call_1", toolResult.ToolUseId);
        var toolResultText = Assert.IsType<TextPart>(Assert.Single(toolResult.Content));
        Assert.Equal("{\"tempF\":72}", toolResultText.Text);

        var tool = Assert.Single(request.Tools);
        Assert.Equal("get_weather", tool.Name);
        Assert.True(tool.Strict);

        Assert.NotNull(request.ToolChoice);
        Assert.Equal(ToolChoiceKind.Auto, request.ToolChoice!.Kind);

        Assert.True(request.CapabilitiesUsed.HasFlag(RequestCapabilities.Vision));
        Assert.False(request.CapabilitiesUsed.HasFlag(RequestCapabilities.RemoteImageUrl));
        Assert.True(request.CapabilitiesUsed.HasFlag(RequestCapabilities.Tools));
        Assert.True(request.CapabilitiesUsed.HasFlag(RequestCapabilities.StrictSchema));
        Assert.False(request.CapabilitiesUsed.HasFlag(RequestCapabilities.ParallelSameTool));
    }

    [Fact]
    public void ToInternal_RemoteImageUrlAndParallelSameNamedToolCalls_DeriveCapabilities()
    {
        const string json = """
        {
          "model": "gpt-5-mini",
          "messages": [
            {
              "role": "user",
              "content": [
                { "type": "image_url", "image_url": { "url": "https://example.com/cat.jpg" } }
              ]
            },
            {
              "role": "assistant",
              "tool_calls": [
                { "id": "call_a", "type": "function", "function": { "name": "search", "arguments": "{}" } },
                { "id": "call_b", "type": "function", "function": { "name": "search", "arguments": "{}" } }
              ]
            }
          ]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var request = _adapter.ToInternal(doc.RootElement);

        var image = Assert.IsType<ImagePart>(Assert.Single(request.Messages[0].Parts));
        Assert.Equal("https://example.com/cat.jpg", image.Url);
        Assert.Null(image.Base64);

        Assert.True(request.CapabilitiesUsed.HasFlag(RequestCapabilities.Vision));
        Assert.True(request.CapabilitiesUsed.HasFlag(RequestCapabilities.RemoteImageUrl));
        Assert.True(request.CapabilitiesUsed.HasFlag(RequestCapabilities.ParallelSameTool));
    }

    [Fact]
    public void ToInternal_UsesPathModel_WhenBodyOmitsModel()
    {
        const string json = """{ "messages": [{ "role": "user", "content": "hi" }] }""";
        using var doc = JsonDocument.Parse(json);

        var request = _adapter.ToInternal(doc.RootElement, pathModel: "gpt-5-mini");

        Assert.Equal("gpt-5-mini", request.Model);
    }

    [Fact]
    public void ToClientResponse_RendersOpenAiChatCompletionShape()
    {
        var response = new ChatResponse
        {
            Content =
            [
                new TextPart("The weather is sunny."),
                new ToolUsePart("call_1", "get_weather", "{\"city\":\"NYC\"}"),
            ],
            StopReason = StopReason.ToolUse,
            Usage = new Usage { InputTokens = 12, OutputTokens = 9 },
        };

        var receipt = new ModelDecision
        {
            Chosen = new ModelRef(Provider.OpenAI, "gpt-5-mini"),
            PolicyVersion = "v1",
        };

        var element = _adapter.ToClientResponse(response, receipt);

        Assert.Equal("chat.completion", element.GetProperty("object").GetString());
        Assert.Equal(0, element.GetProperty("created").GetInt64());
        Assert.Equal("gpt-5-mini", element.GetProperty("model").GetString());

        var choice = element.GetProperty("choices")[0];
        Assert.Equal(0, choice.GetProperty("index").GetInt32());
        Assert.Equal("tool_calls", choice.GetProperty("finish_reason").GetString());

        var message = choice.GetProperty("message");
        Assert.Equal("assistant", message.GetProperty("role").GetString());
        Assert.Equal("The weather is sunny.", message.GetProperty("content").GetString());

        var toolCall = message.GetProperty("tool_calls")[0];
        Assert.Equal("call_1", toolCall.GetProperty("id").GetString());
        Assert.Equal("function", toolCall.GetProperty("type").GetString());
        Assert.Equal("get_weather", toolCall.GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("{\"city\":\"NYC\"}", toolCall.GetProperty("function").GetProperty("arguments").GetString());

        var usage = element.GetProperty("usage");
        Assert.Equal(12, usage.GetProperty("prompt_tokens").GetInt32());
        Assert.Equal(9, usage.GetProperty("completion_tokens").GetInt32());
        Assert.Equal(21, usage.GetProperty("total_tokens").GetInt32());
    }

    [Fact]
    public void ToClientResponse_IsDeterministic_AcrossCalls()
    {
        var response = new ChatResponse { Content = [new TextPart("hi")] };
        var receipt = new ModelDecision
        {
            Chosen = new ModelRef(Provider.OpenAI, "gpt-5-mini"),
            PolicyVersion = "v1",
        };

        var first = _adapter.ToClientResponse(response, receipt).GetRawText();
        var second = _adapter.ToClientResponse(response, receipt).GetRawText();

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ToClientStream_ReFramesNeutralEventsIntoChunksTerminatedByDone()
    {
        static async IAsyncEnumerable<NeutralStreamEvent> Events()
        {
            yield return new StreamMessageStart(new ModelRef(Provider.OpenAI, "gpt-5-mini"));
            yield return new StreamBlockStart(0, "text");
            yield return new StreamTextDelta(0, "Hello ");
            yield return new StreamTextDelta(0, "world");
            yield return new StreamBlockStop(0);
            yield return new StreamBlockStart(1, "tool_use", ToolId: "call_9", ToolName: "get_weather");
            yield return new StreamToolArgsDelta(1, "{\"city\":");
            yield return new StreamToolArgsDelta(1, "\"NYC\"}");
            yield return new StreamBlockStop(1);
            yield return new StreamMessageStop(StopReason.ToolUse, new Usage { InputTokens = 10, OutputTokens = 5 });
            await Task.CompletedTask;
        }

        var receipt = new ModelDecision
        {
            Chosen = new ModelRef(Provider.OpenAI, "gpt-5-mini"),
            PolicyVersion = "v1",
        };

        var items = new List<SseItem<string>>();
        await foreach (var item in _adapter.ToClientStream(Events(), receipt, CancellationToken.None))
        {
            items.Add(item);
        }

        Assert.Equal("[DONE]", items[^1].Data);

        var chunkDocs = items.Take(items.Count - 1)
            .Select(i => JsonDocument.Parse(i.Data))
            .ToList();

        try
        {
            foreach (var chunkDoc in chunkDocs)
            {
                var root = chunkDoc.RootElement;
                Assert.Equal("chat.completion.chunk", root.GetProperty("object").GetString());
                Assert.Equal(0, root.GetProperty("created").GetInt64());
                Assert.Equal("gpt-5-mini", root.GetProperty("model").GetString());
                Assert.True(root.TryGetProperty("choices", out _));
            }

            var deltas = chunkDocs.Select(d => d.RootElement.GetProperty("choices")[0].GetProperty("delta")).ToList();

            Assert.Equal("assistant", deltas[0].GetProperty("role").GetString());

            var textDeltas = deltas
                .Where(d => d.TryGetProperty("content", out _))
                .Select(d => d.GetProperty("content").GetString())
                .ToList();
            Assert.Equal(["Hello ", "world"], textDeltas);

            var toolCallDeltas = deltas
                .Where(d => d.TryGetProperty("tool_calls", out _))
                .Select(d => d.GetProperty("tool_calls")[0])
                .ToList();
            Assert.Equal(3, toolCallDeltas.Count);
            Assert.Equal("call_9", toolCallDeltas[0].GetProperty("id").GetString());
            Assert.Equal("get_weather", toolCallDeltas[0].GetProperty("function").GetProperty("name").GetString());
            Assert.Equal(0, toolCallDeltas[0].GetProperty("index").GetInt32());
            Assert.Equal("{\"city\":", toolCallDeltas[1].GetProperty("function").GetProperty("arguments").GetString());
            Assert.Equal("\"NYC\"}", toolCallDeltas[2].GetProperty("function").GetProperty("arguments").GetString());

            var finishReasons = chunkDocs
                .Select(d => d.RootElement.GetProperty("choices")[0])
                .Where(c => c.GetProperty("finish_reason").ValueKind == JsonValueKind.String)
                .Select(c => c.GetProperty("finish_reason").GetString())
                .ToList();
            Assert.Equal(["tool_calls"], finishReasons);
        }
        finally
        {
            foreach (var chunkDoc in chunkDocs)
            {
                chunkDoc.Dispose();
            }
        }
    }
}
