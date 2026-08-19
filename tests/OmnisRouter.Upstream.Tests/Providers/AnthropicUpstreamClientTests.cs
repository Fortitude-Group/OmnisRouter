using System.Net;
using System.Text;
using System.Text.Json;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Upstream.Providers;

namespace OmnisRouter.Upstream.Tests.Providers;

/// <summary>
/// Wire-level tests for <see cref="AnthropicUpstreamClient"/>: a canned <c>message</c> JSON body
/// parses into the neutral <see cref="ChatResponse"/> (incl. tool_use and thinking blocks), a canned
/// Anthropic named-event SSE sequence parses into the expected ordered <see cref="NeutralStreamEvent"/>s,
/// auth headers are set correctly (<c>x-api-key</c> + <c>anthropic-version</c>, not Bearer), and
/// cancellation stops enumeration.
/// </summary>
public class AnthropicUpstreamClientTests
{
    private static readonly ModelRef Model = new(Provider.Anthropic, "claude-opus-4-8");
    private static readonly ProviderCredential Credential = new(Provider.Anthropic, "sk-ant-test");

    [Fact]
    public async Task SendAsync_ParsesCannedMessage_IntoNeutralChatResponse()
    {
        const string responseJson = """
        {
          "id": "msg_1",
          "type": "message",
          "role": "assistant",
          "model": "claude-opus-4-8",
          "content": [
            { "type": "text", "text": "Hello there." }
          ],
          "stop_reason": "end_turn",
          "stop_sequence": null,
          "usage": {
            "input_tokens": 10,
            "output_tokens": 4,
            "cache_read_input_tokens": 2
          }
        }
        """;

        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });

        var client = CreateClient(handler);

        var response = await client.SendAsync(SampleRequest(), Model, Credential, CancellationToken.None);

        var text = Assert.IsType<TextPart>(Assert.Single(response.Content));
        Assert.Equal("Hello there.", text.Text);
        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Equal(10, response.Usage.InputTokens);
        Assert.Equal(4, response.Usage.OutputTokens);
        Assert.Equal(2, response.Usage.CacheReadTokens);
        Assert.Same(Model, response.ServedBy);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(new Uri("https://fake.test/v1/messages"), handler.LastRequest!.RequestUri);
        Assert.Null(handler.LastRequest.Headers.Authorization);
        Assert.Equal("sk-ant-test", handler.LastRequest.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", handler.LastRequest.Headers.GetValues("anthropic-version").Single());
    }

    [Fact]
    public async Task SendAsync_ParsesToolUseAndThinkingBlocks()
    {
        const string responseJson = """
        {
          "content": [
            { "type": "thinking", "thinking": "Let me check.", "signature": "sig_1" },
            { "type": "tool_use", "id": "toolu_1", "name": "get_weather", "input": { "city": "NYC" } }
          ],
          "stop_reason": "tool_use",
          "usage": { "input_tokens": 5, "output_tokens": 3 }
        }
        """;

        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });

        var client = CreateClient(handler);

        var response = await client.SendAsync(SampleRequest(), Model, Credential, CancellationToken.None);

        Assert.Equal(2, response.Content.Count);
        var thinking = Assert.IsType<ThinkingPart>(response.Content[0]);
        Assert.Equal("Let me check.", thinking.Text);
        Assert.Equal("sig_1", thinking.Signature);
        Assert.False(thinking.Redacted);

        var toolUse = Assert.IsType<ToolUsePart>(response.Content[1]);
        Assert.Equal("toolu_1", toolUse.Id);
        Assert.Equal("get_weather", toolUse.Name);
        Assert.Contains("NYC", toolUse.InputJson);
        Assert.Equal(StopReason.ToolUse, response.StopReason);
    }

    [Fact]
    public async Task SendAsync_SendsMaxTokensDefault_WhenRequestOmitsIt()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"content":[{"type":"text","text":"hi"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}""",
                    Encoding.UTF8,
                    "application/json"),
            });

        var client = CreateClient(handler);
        await client.SendAsync(SampleRequest(), Model, Credential, CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal(4096, doc.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task StreamAsync_ParsesCannedNamedEventSse_IntoOrderedNeutralStreamEvents()
    {
        var sseBody = BuildSseBody(
            ("message_start", """{"type":"message_start","message":{"id":"msg_1","model":"claude-opus-4-8","usage":{"input_tokens":20}}}"""),
            ("content_block_start", """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}"""),
            ("content_block_delta", """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello"}}"""),
            ("content_block_delta", """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":" world"}}"""),
            ("content_block_stop", """{"type":"content_block_stop","index":0}"""),
            ("content_block_start", """{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_1","name":"get_weather","input":{}}}"""),
            ("content_block_delta", """{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"city\":"}}"""),
            ("content_block_delta", """{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"\"NYC\"}"}}"""),
            ("content_block_stop", """{"type":"content_block_stop","index":1}"""),
            ("message_delta", """{"type":"message_delta","delta":{"stop_reason":"tool_use","stop_sequence":null},"usage":{"output_tokens":8}}"""),
            ("message_stop", """{"type":"message_stop"}"""));

        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sseBody, Encoding.UTF8, "text/event-stream"),
            });

        var client = CreateClient(handler);

        var events = new List<NeutralStreamEvent>();
        await foreach (var evt in client.StreamAsync(SampleRequest(), Model, Credential, CancellationToken.None))
        {
            events.Add(evt);
        }

        var messageStart = Assert.IsType<StreamMessageStart>(events[0]);
        Assert.Same(Model, messageStart.ServedBy);

        var textStart = Assert.IsType<StreamBlockStart>(events[1]);
        Assert.Equal(0, textStart.Index);
        Assert.Equal("text", textStart.BlockKind);

        var firstTextDelta = Assert.IsType<StreamTextDelta>(events[2]);
        Assert.Equal("Hello", firstTextDelta.Text);

        var secondTextDelta = Assert.IsType<StreamTextDelta>(events[3]);
        Assert.Equal(" world", secondTextDelta.Text);

        var textStop = Assert.IsType<StreamBlockStop>(events[4]);
        Assert.Equal(0, textStop.Index);

        var toolStart = Assert.IsType<StreamBlockStart>(events[5]);
        Assert.Equal(1, toolStart.Index);
        Assert.Equal("tool_use", toolStart.BlockKind);
        Assert.Equal("toolu_1", toolStart.ToolId);
        Assert.Equal("get_weather", toolStart.ToolName);

        var firstArgsDelta = Assert.IsType<StreamToolArgsDelta>(events[6]);
        Assert.Equal("{\"city\":", firstArgsDelta.PartialJson);

        var secondArgsDelta = Assert.IsType<StreamToolArgsDelta>(events[7]);
        Assert.Equal("\"NYC\"}", secondArgsDelta.PartialJson);

        var toolStop = Assert.IsType<StreamBlockStop>(events[8]);
        Assert.Equal(1, toolStop.Index);

        var messageStop = Assert.IsType<StreamMessageStop>(events[9]);
        Assert.Equal(StopReason.ToolUse, messageStop.StopReason);
        Assert.Equal(20, messageStop.Usage.InputTokens);
        Assert.Equal(8, messageStop.Usage.OutputTokens);

        Assert.Equal(10, events.Count);
    }

    [Fact]
    public async Task StreamAsync_StopsEnumerating_WhenCancelled()
    {
        var handler = new NeverRespondingHandler();
        var client = CreateClient(handler);
        using var cts = new CancellationTokenSource();

        var enumerationTask = Task.Run(async () =>
        {
            await foreach (var _ in client.StreamAsync(SampleRequest(), Model, Credential, cts.Token))
            {
            }
        });

        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => enumerationTask);
    }

    private static AnthropicUpstreamClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new AnthropicUpstreamOptions { BaseUrl = "https://fake.test/" });

    private static ChatRequest SampleRequest() => new()
    {
        Messages = [new Message(Role.User, [new TextPart("Hi")])],
        OriginFormat = ClientFormat.Anthropic,
    };

    private static string BuildSseBody(params (string EventType, string Data)[] events)
    {
        var sb = new StringBuilder();
        foreach (var (eventType, data) in events)
        {
            sb.Append("event: ").Append(eventType).Append('\n');
            sb.Append("data: ").Append(data).Append('\n').Append('\n');
        }

        return sb.ToString();
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        /// <summary>Captured synchronously before the request is disposed by the caller.</summary>
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return responder(request);
        }
    }

    private sealed class NeverRespondingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("unreachable");
        }
    }
}
