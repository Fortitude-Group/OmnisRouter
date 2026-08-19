using System.Net;
using System.Text;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Upstream.Providers;

namespace OmnisRouter.Upstream.Tests.Providers;

/// <summary>
/// Wire-level tests for <see cref="GeminiUpstreamClient"/> (US4): a canned
/// <c>GenerateContentResponse</c> JSON body parses into the neutral <see cref="ChatResponse"/>, a
/// canned <c>streamGenerateContent?alt=sse</c> sequence of whole-object chunks parses into the
/// expected ordered <see cref="NeutralStreamEvent"/>s (accumulating/diffing text, opening the tool
/// block atomically), and the request carries the <c>x-goog-api-key</c> header rather than a bearer
/// token.
/// </summary>
public class GeminiUpstreamClientTests
{
    private static readonly ModelRef Model = new(Provider.Gemini, "gemini-2.5-flash");
    private static readonly ProviderCredential Credential = new(Provider.Gemini, "gk-test");

    [Fact]
    public async Task SendAsync_ParsesCannedGenerateContentResponse_IntoNeutralChatResponse()
    {
        const string responseJson = """
        {
          "candidates": [
            {
              "content": { "parts": [{ "text": "Hello there." }], "role": "model" },
              "finishReason": "STOP",
              "index": 0
            }
          ],
          "usageMetadata": { "promptTokenCount": 10, "candidatesTokenCount": 4, "totalTokenCount": 14 }
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
        Assert.Same(Model, response.ServedBy);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(
            new Uri("https://fake.test/v1beta/models/gemini-2.5-flash:generateContent"),
            handler.LastRequest!.RequestUri);
        Assert.Null(handler.LastRequest.Headers.Authorization);
        Assert.Equal("gk-test", handler.LastRequest.Headers.GetValues("x-goog-api-key").Single());
    }

    [Fact]
    public async Task SendAsync_ParsesFunctionCall_IntoToolUsePart_AndInfersToolUseStopReason()
    {
        const string responseJson = """
        {
          "candidates": [
            {
              "content": {
                "parts": [{ "functionCall": { "name": "get_weather", "args": { "city": "NYC" } } }],
                "role": "model"
              },
              "finishReason": "STOP",
              "index": 0
            }
          ],
          "usageMetadata": { "promptTokenCount": 5, "candidatesTokenCount": 3, "totalTokenCount": 8 }
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
        Assert.Equal("get_weather", toolUse.Name);
        Assert.Contains("NYC", toolUse.InputJson);
        // Gemini reports "STOP" even for a function call — the neutral StopReason is inferred as
        // ToolUse from the presence of the functionCall part (research.md R2).
        Assert.Equal(StopReason.ToolUse, response.StopReason);
    }

    [Fact]
    public async Task StreamAsync_ParsesCannedWholeObjectSseChunks_IntoOrderedNeutralStreamEvents()
    {
        var sseBody = BuildSseBody(
            """{"candidates":[{"content":{"parts":[{"text":"Hello"}],"role":"model"},"index":0}]}""",
            """{"candidates":[{"content":{"parts":[{"text":"Hello world"}],"role":"model"},"index":0}]}""",
            """{"candidates":[{"content":{"parts":[{"text":"Hello world"},{"functionCall":{"name":"get_weather","args":{"city":"NYC"}}}],"role":"model"},"finishReason":"STOP","index":0}],"usageMetadata":{"promptTokenCount":20,"candidatesTokenCount":8,"totalTokenCount":28}}""");

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

        // First chunk: "Hello" is the whole accumulated text so far -> delta is "Hello".
        var firstTextDelta = Assert.IsType<StreamTextDelta>(events[2]);
        Assert.Equal(0, firstTextDelta.Index);
        Assert.Equal("Hello", firstTextDelta.Text);

        // Second chunk resends "Hello world" (the full accumulated text); diffed against "Hello" the
        // marginal delta is " world".
        var secondTextDelta = Assert.IsType<StreamTextDelta>(events[3]);
        Assert.Equal(0, secondTextDelta.Index);
        Assert.Equal(" world", secondTextDelta.Text);

        // Third chunk repeats the same accumulated text (no further text delta) and opens the
        // function-call block atomically (start + one whole-args delta, no diffing).
        var toolStart = Assert.IsType<StreamBlockStart>(events[4]);
        Assert.Equal(1, toolStart.Index);
        Assert.Equal("tool_use", toolStart.BlockKind);
        Assert.Equal("get_weather", toolStart.ToolName);
        Assert.NotNull(toolStart.ToolId);

        var argsDelta = Assert.IsType<StreamToolArgsDelta>(events[5]);
        Assert.Equal(1, argsDelta.Index);
        Assert.Contains("NYC", argsDelta.PartialJson);

        var textStop = Assert.IsType<StreamBlockStop>(events[6]);
        Assert.Equal(0, textStop.Index);

        var toolStop = Assert.IsType<StreamBlockStop>(events[7]);
        Assert.Equal(1, toolStop.Index);

        var messageStop = Assert.IsType<StreamMessageStop>(events[8]);
        Assert.Equal(StopReason.ToolUse, messageStop.StopReason);
        Assert.Equal(20, messageStop.Usage.InputTokens);
        Assert.Equal(8, messageStop.Usage.OutputTokens);

        Assert.Equal(9, events.Count);
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

    private static GeminiUpstreamClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new GeminiUpstreamOptions { BaseUrl = "https://fake.test/" });

    private static ChatRequest SampleRequest() => new()
    {
        Messages = [new Message(Role.User, [new TextPart("Hi")])],
        OriginFormat = ClientFormat.Gemini,
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
