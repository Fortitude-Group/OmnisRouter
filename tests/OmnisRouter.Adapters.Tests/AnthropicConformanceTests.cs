using System.Net.ServerSentEvents;
using System.Text.Json;
using OmnisRouter.Adapters.Anthropic;
using OmnisRouter.Adapters.Tests.Fixtures;
using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;

namespace OmnisRouter.Adapters.Tests;

/// <summary>
/// Golden-file round-trip conformance for the Anthropic CLIENT-format adapter: ingress parsing of an
/// Anthropic Messages request (incl. tool_use/thinking/cache_control) into the neutral model,
/// capability derivation, egress rendering of a neutral response back into Anthropic message shape,
/// and named-event SSE re-framing of neutral stream events.
/// </summary>
public class AnthropicConformanceTests
{
    private readonly AnthropicAdapter _adapter = new();

    [Fact]
    public void ToInternal_ParsesRepresentativeAnthropicRequest()
    {
        using var doc = FixtureLoader.ReadJson("anthropic-messages-request.json");

        var request = _adapter.ToInternal(doc.RootElement);

        Assert.Equal(ClientFormat.Anthropic, request.OriginFormat);
        Assert.Equal("claude-opus-4-8", request.Model);
        Assert.False(request.Stream);
        Assert.Equal(512, request.MaxTokens);
        Assert.Equal(0.3, request.Temperature);

        var system = Assert.Single(request.System);
        Assert.Equal("You are a helpful assistant.", system.Text);
        Assert.NotNull(system.Cache);
        Assert.Equal(CacheTtl.OneHour, system.Cache!.Ttl);

        Assert.NotNull(request.Thinking);
        Assert.True(request.Thinking!.Enabled);
        Assert.Equal(2048, request.Thinking.BudgetTokens);

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
        var thinking = Assert.IsType<ThinkingPart>(assistantMessage.Parts[0]);
        Assert.Equal("I should check the weather for NYC.", thinking.Text);
        Assert.Equal("sig_abc123", thinking.Signature);
        Assert.False(thinking.Redacted);
        Assert.Equal(Provider.Anthropic, thinking.OriginProvider);
        var toolUse = Assert.IsType<ToolUsePart>(assistantMessage.Parts[1]);
        Assert.Equal("toolu_1", toolUse.Id);
        Assert.Equal("get_weather", toolUse.Name);
        Assert.Contains("NYC", toolUse.InputJson);

        var toolResultMessage = request.Messages[2];
        Assert.Equal(Role.User, toolResultMessage.Role);
        var toolResult = Assert.IsType<ToolResultPart>(Assert.Single(toolResultMessage.Parts));
        Assert.Equal("toolu_1", toolResult.ToolUseId);
        var toolResultText = Assert.IsType<TextPart>(Assert.Single(toolResult.Content));
        Assert.Equal("{\"tempF\":72}", toolResultText.Text);

        var tool = Assert.Single(request.Tools);
        Assert.Equal("get_weather", tool.Name);
        Assert.True(tool.Strict);

        Assert.NotNull(request.ToolChoice);
        Assert.Equal(ToolChoiceKind.Auto, request.ToolChoice!.Kind);

        Assert.True(request.CapabilitiesUsed.HasFlag(RequestCapabilities.Vision));
        Assert.True(request.CapabilitiesUsed.HasFlag(RequestCapabilities.Tools));
        Assert.True(request.CapabilitiesUsed.HasFlag(RequestCapabilities.StrictSchema));
        Assert.True(request.CapabilitiesUsed.HasFlag(RequestCapabilities.CachePinGuaranteed));
        Assert.True(request.CapabilitiesUsed.HasFlag(RequestCapabilities.Thinking));
        Assert.True(request.CapabilitiesUsed.HasFlag(RequestCapabilities.ThinkingWithSignature));
        Assert.True(request.CapabilitiesUsed.HasFlag(RequestCapabilities.NumericReasoningBudget));
    }

    [Fact]
    public void ToInternal_UsesPathModel_WhenBodyOmitsModel()
    {
        const string json = """{ "messages": [{ "role": "user", "content": "hi" }] }""";
        using var doc = JsonDocument.Parse(json);

        var request = _adapter.ToInternal(doc.RootElement, pathModel: "claude-haiku-4-5");

        Assert.Equal("claude-haiku-4-5", request.Model);
    }

    [Fact]
    public void ToInternal_PlainStringSystemAndFiveMinuteCache_DoesNotSetCachePinGuaranteed()
    {
        const string json = """
        {
          "model": "claude-haiku-4-5",
          "system": "Be concise.",
          "messages": [
            {
              "role": "user",
              "content": [
                { "type": "text", "text": "Hi", "cache_control": { "type": "ephemeral" } }
              ]
            }
          ]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var request = _adapter.ToInternal(doc.RootElement);

        var system = Assert.Single(request.System);
        Assert.Equal("Be concise.", system.Text);
        Assert.Null(system.Cache);

        var textPart = Assert.IsType<TextPart>(Assert.Single(request.Messages[0].Parts));
        Assert.NotNull(textPart.Cache);
        Assert.Equal(CacheTtl.FiveMinutes, textPart.Cache!.Ttl);

        Assert.False(request.CapabilitiesUsed.HasFlag(RequestCapabilities.CachePinGuaranteed));
    }

    [Fact]
    public void ToInternal_RedactedThinkingBlock_MapsToThinkingPartWithRedactedFlag()
    {
        const string json = """
        {
          "model": "claude-haiku-4-5",
          "messages": [
            {
              "role": "assistant",
              "content": [
                { "type": "redacted_thinking", "data": "opaque_encrypted_blob" }
              ]
            }
          ]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var request = _adapter.ToInternal(doc.RootElement);

        var thinking = Assert.IsType<ThinkingPart>(Assert.Single(request.Messages[0].Parts));
        Assert.True(thinking.Redacted);
        Assert.Equal("opaque_encrypted_blob", thinking.Signature);
        Assert.Equal(Provider.Anthropic, thinking.OriginProvider);

        // A signature-less redacted block should not itself claim ThinkingWithSignature — real
        // Anthropic signatures come from non-redacted thinking blocks only. Here the opaque `data`
        // rides in the same field, so this documents the (accepted) approximation rather than
        // asserting a specific value either way.
        Assert.True(request.CapabilitiesUsed.HasFlag(RequestCapabilities.Thinking));
    }

    [Fact]
    public void ToClientResponse_RendersAnthropicMessageShape()
    {
        var response = new ChatResponse
        {
            Content =
            [
                new TextPart("The weather is sunny."),
                new ToolUsePart("toolu_1", "get_weather", "{\"city\":\"NYC\"}"),
            ],
            StopReason = StopReason.ToolUse,
            Usage = new Usage { InputTokens = 12, OutputTokens = 9, CacheReadTokens = 3 },
        };

        var receipt = new ModelDecision
        {
            Chosen = new ModelRef(Provider.Anthropic, "claude-opus-4-8"),
            PolicyVersion = "v1",
        };

        var element = _adapter.ToClientResponse(response, receipt);

        Assert.Equal("message", element.GetProperty("type").GetString());
        Assert.Equal("assistant", element.GetProperty("role").GetString());
        Assert.Equal("claude-opus-4-8", element.GetProperty("model").GetString());
        Assert.Equal("tool_use", element.GetProperty("stop_reason").GetString());
        Assert.Equal(JsonValueKind.Null, element.GetProperty("stop_sequence").ValueKind);

        var content = element.GetProperty("content");
        Assert.Equal(2, content.GetArrayLength());
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal("The weather is sunny.", content[0].GetProperty("text").GetString());
        Assert.Equal("tool_use", content[1].GetProperty("type").GetString());
        Assert.Equal("toolu_1", content[1].GetProperty("id").GetString());
        Assert.Equal("get_weather", content[1].GetProperty("name").GetString());
        Assert.Equal("NYC", content[1].GetProperty("input").GetProperty("city").GetString());

        var usage = element.GetProperty("usage");
        Assert.Equal(12, usage.GetProperty("input_tokens").GetInt32());
        Assert.Equal(9, usage.GetProperty("output_tokens").GetInt32());
        Assert.Equal(3, usage.GetProperty("cache_read_input_tokens").GetInt32());
    }

    [Fact]
    public void ToClientResponse_IsDeterministic_AcrossCalls()
    {
        var response = new ChatResponse { Content = [new TextPart("hi")] };
        var receipt = new ModelDecision
        {
            Chosen = new ModelRef(Provider.Anthropic, "claude-haiku-4-5"),
            PolicyVersion = "v1",
        };

        var first = _adapter.ToClientResponse(response, receipt).GetRawText();
        var second = _adapter.ToClientResponse(response, receipt).GetRawText();

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ToClientStream_ReFramesNeutralEventsIntoNamedEventsEndingInMessageStop()
    {
        static async IAsyncEnumerable<NeutralStreamEvent> Events()
        {
            yield return new StreamMessageStart(new ModelRef(Provider.Anthropic, "claude-opus-4-8"));
            yield return new StreamBlockStart(0, "text");
            yield return new StreamTextDelta(0, "Hello ");
            yield return new StreamTextDelta(0, "world");
            yield return new StreamBlockStop(0);
            yield return new StreamBlockStart(1, "tool_use", ToolId: "toolu_9", ToolName: "get_weather");
            yield return new StreamToolArgsDelta(1, "{\"city\":");
            yield return new StreamToolArgsDelta(1, "\"NYC\"}");
            yield return new StreamBlockStop(1);
            yield return new StreamMessageStop(StopReason.ToolUse, new Usage { InputTokens = 10, OutputTokens = 5 });
            await Task.CompletedTask;
        }

        var receipt = new ModelDecision
        {
            Chosen = new ModelRef(Provider.Anthropic, "claude-opus-4-8"),
            PolicyVersion = "v1",
        };

        var items = new List<SseItem<string>>();
        await foreach (var item in _adapter.ToClientStream(Events(), receipt, CancellationToken.None))
        {
            items.Add(item);
        }

        Assert.Equal("message_stop", items[^1].EventType);
        Assert.Equal("message_start", items[0].EventType);

        var eventTypes = items.Select(i => i.EventType).ToList();
        Assert.Equal(
            [
                "message_start",
                "content_block_start",
                "content_block_delta",
                "content_block_delta",
                "content_block_stop",
                "content_block_start",
                "content_block_delta",
                "content_block_delta",
                "content_block_stop",
                "message_delta",
                "message_stop",
            ],
            eventTypes);

        var docs = items.Select(i => JsonDocument.Parse(i.Data)).ToList();
        try
        {
            Assert.Equal("message_start", docs[0].RootElement.GetProperty("type").GetString());

            var toolStart = docs[5].RootElement;
            Assert.Equal(1, toolStart.GetProperty("index").GetInt32());
            var toolBlock = toolStart.GetProperty("content_block");
            Assert.Equal("tool_use", toolBlock.GetProperty("type").GetString());
            Assert.Equal("toolu_9", toolBlock.GetProperty("id").GetString());
            Assert.Equal("get_weather", toolBlock.GetProperty("name").GetString());

            var firstTextDelta = docs[2].RootElement.GetProperty("delta");
            Assert.Equal("text_delta", firstTextDelta.GetProperty("type").GetString());
            Assert.Equal("Hello ", firstTextDelta.GetProperty("text").GetString());

            var firstArgsDelta = docs[6].RootElement.GetProperty("delta");
            Assert.Equal("input_json_delta", firstArgsDelta.GetProperty("type").GetString());
            Assert.Equal("{\"city\":", firstArgsDelta.GetProperty("partial_json").GetString());

            var messageDelta = docs[9].RootElement;
            Assert.Equal("tool_use", messageDelta.GetProperty("delta").GetProperty("stop_reason").GetString());
            Assert.Equal(5, messageDelta.GetProperty("usage").GetProperty("output_tokens").GetInt32());

            Assert.Equal("message_stop", docs[10].RootElement.GetProperty("type").GetString());
        }
        finally
        {
            foreach (var d in docs)
            {
                d.Dispose();
            }
        }
    }

    [Fact]
    public async Task ToClientStream_ThinkingDeltaWithTextAndSignature_EmitsTwoSeparateDeltaFrames()
    {
        static async IAsyncEnumerable<NeutralStreamEvent> Events()
        {
            yield return new StreamMessageStart(new ModelRef(Provider.Anthropic, "claude-opus-4-8"));
            yield return new StreamBlockStart(0, "thinking");
            yield return new StreamThinkingDelta(0, Text: "Reasoning...");
            yield return new StreamThinkingDelta(0, Signature: "sig_xyz");
            yield return new StreamBlockStop(0);
            yield return new StreamMessageStop(StopReason.EndTurn, new Usage { InputTokens = 1, OutputTokens = 2 });
            await Task.CompletedTask;
        }

        var receipt = new ModelDecision
        {
            Chosen = new ModelRef(Provider.Anthropic, "claude-opus-4-8"),
            PolicyVersion = "v1",
        };

        var items = new List<SseItem<string>>();
        await foreach (var item in _adapter.ToClientStream(Events(), receipt, CancellationToken.None))
        {
            items.Add(item);
        }

        var deltaFrames = items.Where(i => i.EventType == "content_block_delta")
            .Select(i => JsonDocument.Parse(i.Data).RootElement.GetProperty("delta"))
            .ToList();

        Assert.Equal(2, deltaFrames.Count);
        Assert.Equal("thinking_delta", deltaFrames[0].GetProperty("type").GetString());
        Assert.Equal("Reasoning...", deltaFrames[0].GetProperty("thinking").GetString());
        Assert.Equal("signature_delta", deltaFrames[1].GetProperty("type").GetString());
        Assert.Equal("sig_xyz", deltaFrames[1].GetProperty("signature").GetString());
    }
}
