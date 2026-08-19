using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;

namespace OmnisRouter.Upstream.Providers;

/// <summary>
/// Anthropic Messages upstream wire client: builds the Anthropic request from the neutral
/// <see cref="ChatRequest"/> for the chosen model, and parses the Anthropic response/SSE stream back
/// into the neutral model. Auth is <c>x-api-key</c> + <c>anthropic-version</c> (not a Bearer token —
/// see research.md R2/R3). See research.md R3 — the streaming path uses ResponseHeadersRead and is
/// never retried (a retry after headers-read would resend the prompt and double-charge).
/// </summary>
public sealed class AnthropicUpstreamClient : IUpstreamClient
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly string _anthropicVersion;

    public AnthropicUpstreamClient(HttpClient httpClient, AnthropicUpstreamOptions options)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(options.BaseUrl);
        _anthropicVersion = options.AnthropicVersion;
    }

    public Provider Provider => Provider.Anthropic;

    public async Task<ChatResponse> SendAsync(
        ChatRequest request, ModelRef model, ProviderCredential credential, CancellationToken cancellationToken)
    {
        var wireRequest = AnthropicRequestMapper.ToWireRequest(request, model, stream: false);
        using var httpRequest = CreateHttpRequest(wireRequest, credential);

        using var httpResponse = await _httpClient
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        var wireResponse = await httpResponse.Content
            .ReadFromJsonAsync<AnthropicMessagesResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (wireResponse is null)
        {
            throw new InvalidOperationException("Anthropic response body was empty or invalid JSON.");
        }

        return AnthropicResponseMapper.ToChatResponse(wireResponse, model);
    }

    public async IAsyncEnumerable<NeutralStreamEvent> StreamAsync(
        ChatRequest request,
        ModelRef model,
        ProviderCredential credential,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var wireRequest = AnthropicRequestMapper.ToWireRequest(request, model, stream: true);
        using var httpRequest = CreateHttpRequest(wireRequest, credential);

        using var httpResponse = await _httpClient
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        yield return new StreamMessageStart(model);

        await using var responseStream = await httpResponse.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        var parser = SseParser.Create(responseStream);
        var state = new AnthropicStreamState();

        await foreach (var item in parser.EnumerateAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrEmpty(item.EventType) || string.IsNullOrEmpty(item.Data))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(item.Data);
            foreach (var streamEvent in state.Process(item.EventType, doc.RootElement))
            {
                yield return streamEvent;
            }
        }
    }

    private HttpRequestMessage CreateHttpRequest(AnthropicMessagesRequest body, ProviderCredential credential)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        httpRequest.Headers.Add("x-api-key", credential.ApiKey);
        httpRequest.Headers.Add("anthropic-version", _anthropicVersion);
        return httpRequest;
    }
}
