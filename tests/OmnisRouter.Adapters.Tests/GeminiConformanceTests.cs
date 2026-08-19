using System.Net.ServerSentEvents;
using System.Text.Json;
using OmnisRouter.Adapters.Gemini;
using OmnisRouter.Adapters.Tests.Fixtures;
using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;

namespace OmnisRouter.Adapters.Tests;

/// <summary>
/// Golden-file round-trip conformance for the Gemini CLIENT-format adapter (US4): ingress parsing
/// of a Gemini <c>generateContent</c> request into the neutral model (including <c>functionCall</c>/
/// <c>functionResponse</c> id correlation and <c>inlineData</c> vision), egress rendering of a
/// neutral response back into Gemini <c>GenerateContentResponse</c> shape, and whole-object SSE
/// re-framing of neutral stream events.
/// </summary>
public class GeminiConformanceTests
{
    private readonly GeminiAdapter _adapter = new();

    [Fact]
    public void ToInternal_ParsesRepresentativeGeminiRequest()
    {
        const string json = """
        {
          "systemInstruction": { "parts": [{ "text": "You are a helpful assistant." }] },
          "generationConfig": {
            "maxOutputTokens": 256,
            "temperature": 0.2,
            "thinkingConfig": { "thinkingBudget": 1024, "includeThoughts": true }
          },
          "contents": [
            {
              "role": "user",
              "parts": [
                { "text": "What's in this image?" },
                { "inlineData": { "mimeType": "image/png", "data": "iVBORw0KGgo=" } }
              ]
            },
            {
              "role": "model",
              "parts": [
                { "functionCall": { "name": "get_weather", "args": { "city": "NYC" } } }
              ]
            },
            {
              "role": "user",
              "parts": [
                { "functionResponse": { "name": "get_weather", "response": { "tempF": 72 } } }
              ]
            }
          ],
          "tools": [
            {
              "functionDeclarations": [
                {
                  "name": "get_weather",
                  "description": "Get the current weather for a city",
                  "parameters": { "type": "object", "properties": { "city": { "type": "string" } } }
                }
              ]
            }
          ],
          "toolConfig": { "functionCallingConfig": { "mode": "AUTO" } }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var request = _adapter.ToInternal(doc.RootElement, pathModel: "gemini-2.5-flash");

        Assert.Equal(ClientFormat.Gemini, request.OriginFormat);
        Assert.Equal("gemini-2.5-flash", request.Model);
        Assert.False(request.Stream);
        Assert.Equal(256, request.MaxTokens);
        Assert.Equal(0.2, request.Temperature);

        var system = Assert.Single(request.System);
        Assert.Equal("You are a helpful assistant.", system.Text);

        Assert.NotNull(request.Thinking);
        Assert.True(request.Thinking!.Enabled);
        Assert.Equal(1024, request.Thinking.BudgetTokens);

        Assert.Equal(3, request.Messages.Count);

        var userMessage = request.Messages[0];
        Assert.Equal(Role.User, userMessage.Role);
        var userText = Assert.IsType<TextPart>(userMessage.Parts[0]);
        Assert.Equal("What's in this image?", userText.Text);
        var image = Assert.IsType<ImagePart>(userMessage.Parts[1]);
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal("iVBORw0KGgo=", image.Base64);
        Assert.Null(image.Url);

        var assistantMessage = request.Messages[1];
        Assert.Equal(Role.Assistant, assistantMessage.Role);
        var toolUse = Assert.IsType<ToolUsePart>(Assert.Single(assistantMessage.Parts));
        Assert.Equal("get_weather", toolUse.Name);
        Assert.Contains("NYC", toolUse.InputJson);
        Assert.NotEmpty(toolUse.Id);

        var toolMessage = request.Messages[2];
        Assert.Equal(Role.Tool, toolMessage.Role);
        var toolResult = Assert.IsType<ToolResultPart>(Assert.Single(toolMessage.Parts));
        // Correlated by name (Gemini functionResponse carries no id) back to the synthesized
        // functionCall id from the preceding "model" turn.
        Assert.Equal(toolUse.Id, toolResult.ToolUseId);
        var toolResultText = Assert.IsType<TextPart>(Assert.Single(toolResult.Content));
        Assert.Contains("72", toolResultText.Text);

        var tool = Assert.Single(request.Tools);
        Assert.Equal("get_weather", tool.Name);
        Assert.False(tool.Strict);

        Assert.NotNull(request.ToolChoice);
        Assert.Equal(ToolChoiceKind.Auto, request.ToolChoice!.Kind);

        Assert.True(request.CapabilitiesUsed.HasFlag(RequestCapabilities.Vision));
        Assert.False(request.CapabilitiesUsed.HasFlag(RequestCapabilities.RemoteImageUrl));
        Assert.True(request.CapabilitiesUsed.HasFlag(RequestCapabilities.Tools));
        Assert.True(request.CapabilitiesUsed.HasFlag(RequestCapabilities.Thinking));
        Assert.True(request.CapabilitiesUsed.HasFlag(RequestCapabilities.NumericReasoningBudget));
        Assert.False(request.CapabilitiesUsed.HasFlag(RequestCapabilities.ParallelSameTool));
    }

    [Fact]
    public void ToInternal_FileUriAndParallelSameNamedFunctionCalls_DeriveCapabilities()
    {
        const string json = """
        {
          "contents": [
            {
              "role": "user",
              "parts": [
                { "fileData": { "mimeType": "image/jpeg", "fileUri": "https://example.com/cat.jpg" } }
              ]
            },
            {
              "role": "model",
              "parts": [
                { "functionCall": { "name": "search", "args": {} } },
                { "functionCall": { "name": "search", "args": {} } }
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

        var toolUses = request.Messages[1].Parts.OfType<ToolUsePart>().ToList();
        Assert.Equal(2, toolUses.Count);
        Assert.NotEqual(toolUses[0].Id, toolUses[1].Id);
    }

    [Fact]
    public void ToInternal_UsesPathModel_WhenBodyOmitsModel()
    {
        const string json = """{ "contents": [{ "role": "user", "parts": [{ "text": "hi" }] }] }""";
        using var doc = JsonDocument.Parse(json);

        var request = _adapter.ToInternal(doc.RootElement, pathModel: "gemini-2.5-flash");

        Assert.Equal("gemini-2.5-flash", request.Model);
    }

    [Fact]
    public void ToClientResponse_RendersGeminiGenerateContentShape()
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
            Chosen = new ModelRef(Provider.Gemini, "gemini-2.5-flash"),
            PolicyVersion = "v1",
        };

        var element = _adapter.ToClientResponse(response, receipt);

        var candidate = element.GetProperty("candidates")[0];
        Assert.Equal("STOP", candidate.GetProperty("finishReason").GetString());
        Assert.Equal(0, candidate.GetProperty("index").GetInt32());

        var content = candidate.GetProperty("content");
        Assert.Equal("model", content.GetProperty("role").GetString());

        var parts = content.GetProperty("parts");
        Assert.Equal("The weather is sunny.", parts[0].GetProperty("text").GetString());

        var functionCall = parts[1].GetProperty("functionCall");
        Assert.Equal("get_weather", functionCall.GetProperty("name").GetString());
        Assert.Equal("NYC", functionCall.GetProperty("args").GetProperty("city").GetString());
        Assert.False(functionCall.TryGetProperty("id", out _));

        var usage = element.GetProperty("usageMetadata");
        Assert.Equal(12, usage.GetProperty("promptTokenCount").GetInt32());
        Assert.Equal(9, usage.GetProperty("candidatesTokenCount").GetInt32());
        Assert.Equal(21, usage.GetProperty("totalTokenCount").GetInt32());
    }

    [Fact]
    public void ToClientResponse_MatchesFixtureShape()
    {
        using var fixture = FixtureLoader.ReadJson("gemini-generate-content-response.json");

        var response = new ChatResponse
        {
            Content = [new TextPart("Hello! How can I help you today?")],
            StopReason = StopReason.EndTurn,
            Usage = new Usage { InputTokens = 12, OutputTokens = 9 },
        };

        var receipt = new ModelDecision
        {
            Chosen = new ModelRef(Provider.Gemini, "gemini-2.5-flash"),
            PolicyVersion = "v1",
        };

        var element = _adapter.ToClientResponse(response, receipt);

        Assert.Equal(
            fixture.RootElement.GetProperty("candidates")[0].GetProperty("finishReason").GetString(),
            element.GetProperty("candidates")[0].GetProperty("finishReason").GetString());
        Assert.Equal(
            fixture.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0]
                .GetProperty("text").GetString(),
            element.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0]
                .GetProperty("text").GetString());
    }

    [Fact]
    public void ToClientResponse_IsDeterministic_AcrossCalls()
    {
        var response = new ChatResponse { Content = [new TextPart("hi")] };
        var receipt = new ModelDecision
        {
            Chosen = new ModelRef(Provider.Gemini, "gemini-2.5-flash"),
            PolicyVersion = "v1",
        };

        var first = _adapter.ToClientResponse(response, receipt).GetRawText();
        var second = _adapter.ToClientResponse(response, receipt).GetRawText();

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ToClientStream_EmitsWholeObjectSnapshotsPerChunk_NoDoneSentinel()
    {
        static async IAsyncEnumerable<NeutralStreamEvent> Events()
        {
            yield return new StreamMessageStart(new ModelRef(Provider.Gemini, "gemini-2.5-flash"));
            yield return new StreamBlockStart(0, "text");
            yield return new StreamTextDelta(0, "Hello ");
            yield return new StreamTextDelta(0, "world");
            yield return new StreamBlockStop(0);
            yield return new StreamBlockStart(1, "tool_use", ToolId: "get_weather_1", ToolName: "get_weather");
            yield return new StreamToolArgsDelta(1, "{\"city\":\"NYC\"}");
            yield return new StreamBlockStop(1);
            yield return new StreamMessageStop(StopReason.ToolUse, new Usage { InputTokens = 10, OutputTokens = 5 });
            await Task.CompletedTask;
        }

        var receipt = new ModelDecision
        {
            Chosen = new ModelRef(Provider.Gemini, "gemini-2.5-flash"),
            PolicyVersion = "v1",
        };

        var items = new List<SseItem<string>>();
        await foreach (var item in _adapter.ToClientStream(Events(), receipt, CancellationToken.None))
        {
            items.Add(item);
        }

        Assert.DoesNotContain(items, i => i.Data == "[DONE]");

        var chunkDocs = items.Select(i => JsonDocument.Parse(i.Data)).ToList();
        try
        {
            // Each chunk is a whole GenerateContentResponse-shaped object.
            foreach (var chunkDoc in chunkDocs)
            {
                Assert.True(chunkDoc.RootElement.TryGetProperty("candidates", out _));
            }

            // The two non-terminal chunks (no finishReason yet) carry the growing accumulated text,
            // not just the marginal delta.
            var progressionTexts = chunkDocs
                .Where(d => !d.RootElement.GetProperty("candidates")[0].TryGetProperty("finishReason", out _))
                .Select(d => d.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0])
                .Select(p => p.GetProperty("text").GetString())
                .ToList();
            Assert.Equal(["Hello ", "Hello world"], progressionTexts);
            Assert.Equal(3, chunkDocs.Count);

            var last = chunkDocs[^1].RootElement;
            var lastCandidate = last.GetProperty("candidates")[0];
            Assert.Equal("STOP", lastCandidate.GetProperty("finishReason").GetString());

            var lastParts = lastCandidate.GetProperty("content").GetProperty("parts");
            var functionCallPart = lastParts.EnumerateArray().First(p => p.TryGetProperty("functionCall", out _));
            Assert.Equal("get_weather", functionCallPart.GetProperty("functionCall").GetProperty("name").GetString());
            Assert.Equal(
                "NYC",
                functionCallPart.GetProperty("functionCall").GetProperty("args").GetProperty("city").GetString());

            var usage = last.GetProperty("usageMetadata");
            Assert.Equal(10, usage.GetProperty("promptTokenCount").GetInt32());
            Assert.Equal(5, usage.GetProperty("candidatesTokenCount").GetInt32());
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
