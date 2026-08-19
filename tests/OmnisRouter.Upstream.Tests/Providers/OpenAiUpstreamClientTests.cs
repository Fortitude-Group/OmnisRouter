using System.Net;
using System.Text;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Upstream.Providers;

namespace OmnisRouter.Upstream.Tests.Providers;

/// <summary>
/// Wire-level tests for <see cref="OpenAiUpstreamClient"/> (T029): a canned <c>chat.completion</c>
/// JSON body parses into the neutral <see cref="ChatResponse"/>, a canned
/// <c>chat.completion.chunk</c> SSE sequence parses into the expected ordered
/// <see cref="NeutralStreamEvent"/>s, and cancellation stops enumeration.
/// </summary>
public class OpenAiUpstreamClientTests
{
    private static readonly ModelRef Model = new(Provider.OpenAI, "gpt-5-mini");
    private static readonly ProviderCredential Credential = new(Provider.OpenAI, "sk-test");

    [Fact]
    public async Task SendAsync_ParsesCannedChatCompletion_IntoNeutralChatResponse()
    {
        const string responseJson = """
        {
          "id": "chatcmpl-1",
          "object": "chat.completion",
          "model": "gpt-5-mini",
          "choices": [
            {
              "index": 0,
              "message": { "role": "assistant", "content": "Hello there." },
              "finish_reason": "stop"
            }
          ],
          "usage": {
            "prompt_tokens": 10,
            "completion_tokens": 4,
            "total_tokens": 14,
            "prompt_tokens_details": { "cached_tokens": 2 }
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
        Assert.Equal(new Uri("https://fake.test/v1/chat/completions"), handler.LastRequest!.RequestUri);
        Assert.Equal("Bearer sk-test", handler.LastRequest.Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task SendAsync_ParsesToolCalls_IntoToolUseParts()
    {
        const string responseJson = """
        {
          "choices": [
            {
              "index": 0,
              "message": {
                "role": "assistant",
                "content": null,
                "tool_calls": [
                  { "id": "call_1", "type": "function", "function": { "name": "get_weather", "arguments": "{\"city\":\"NYC\"}" } }
                ]
              },
              "finish_reason": "tool_calls"
            }
          ],
          "usage": { "prompt_tokens": 5, "completion_tokens": 3, "total_tokens": 8 }
        }
        """;

        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });

        var client = CreateClient(handler);

        var response = await client.SendAsync(SampleRequest(), Model, Credential, CancellationToken.None);

        var toolUse = Assert.IsType<ToolUsePart>(Assert.Single(response.Content));
        Assert.Equal("call_1", toolUse.Id);
        Assert.Equal("get_weather", toolUse.Name);
        Assert.Equal("{\"city\":\"NYC\"}", toolUse.InputJson);
        Assert.Equal(StopReason.ToolUse, response.StopReason);
    }

    [Fact]
    public async Task StreamAsync_ParsesCannedSseChunks_IntoOrderedNeutralStreamEvents()
    {
        var sseBody = BuildSseBody(
            """{"choices":[{"index":0,"delta":{"role":"assistant","content":""}}]}""",
            """{"choices":[{"index":0,"delta":{"content":"Hello"}}]}""",
            """{"choices":[{"index":0,"delta":{"content":" world"}}]}""",
            """{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"get_weather","arguments":""}}]}}]}""",
            """{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"city\":"}}]}}]}""",
            """{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"NYC\"}"}}]}}]}""",
            """{"choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}""",
            """{"choices":[],"usage":{"prompt_tokens":20,"completion_tokens":8,"total_tokens":28}}""",
            "[DONE]");

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
        Assert.Equal(0, firstTextDelta.Index);
        Assert.Equal("Hello", firstTextDelta.Text);

        var secondTextDelta = Assert.IsType<StreamTextDelta>(events[3]);
        Assert.Equal(0, secondTextDelta.Index);
        Assert.Equal(" world", secondTextDelta.Text);

        var toolStart = Assert.IsType<StreamBlockStart>(events[4]);
        Assert.Equal(1, toolStart.Index);
        Assert.Equal("tool_use", toolStart.BlockKind);
        Assert.Equal("call_1", toolStart.ToolId);
        Assert.Equal("get_weather", toolStart.ToolName);

        var firstArgsDelta = Assert.IsType<StreamToolArgsDelta>(events[5]);
        Assert.Equal(1, firstArgsDelta.Index);
        Assert.Equal("{\"city\":", firstArgsDelta.PartialJson);

        var secondArgsDelta = Assert.IsType<StreamToolArgsDelta>(events[6]);
        Assert.Equal(1, secondArgsDelta.Index);
        Assert.Equal("\"NYC\"}", secondArgsDelta.PartialJson);

        var textStop = Assert.IsType<StreamBlockStop>(events[7]);
        Assert.Equal(0, textStop.Index);

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

    private static OpenAiUpstreamClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new OpenAiUpstreamOptions { BaseUrl = "https://fake.test/v1/" });

    private static ChatRequest SampleRequest() => new()
    {
        Messages = [new Message(Role.User, [new TextPart("Hi")])],
        OriginFormat = ClientFormat.OpenAI,
    };

    private static string BuildSseBody(params string[] dataLines)
    {
        var sb = new StringBuilder();
        foreach (var line in dataLines)
        {
            sb.Append("data: ").Append(line).Append('\n').Append('\n');
        }

        return sb.ToString();
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(responder(request));
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
