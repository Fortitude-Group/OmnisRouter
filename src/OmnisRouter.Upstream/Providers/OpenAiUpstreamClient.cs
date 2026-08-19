using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;

namespace OmnisRouter.Upstream.Providers;

/// <summary>
/// OpenAI Chat Completions upstream wire client: builds the OpenAI request from the neutral
/// <see cref="ChatRequest"/> for the chosen model, and parses the OpenAI response/SSE stream back
/// into the neutral model. See research.md R3 — the streaming path uses ResponseHeadersRead and is
/// never retried (a retry after headers-read would resend the prompt and double-charge).
/// </summary>
public sealed class OpenAiUpstreamClient : IUpstreamClient
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;

    public OpenAiUpstreamClient(HttpClient httpClient, OpenAiUpstreamOptions options)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(options.BaseUrl);
    }

    public Provider Provider => Provider.OpenAI;

    public async Task<ChatResponse> SendAsync(
        ChatRequest request, ModelRef model, ProviderCredential credential, CancellationToken cancellationToken)
    {
        var wireRequest = OpenAiRequestMapper.ToWireRequest(request, model, stream: false);
        using var httpRequest = CreateHttpRequest(wireRequest, credential);

        using var httpResponse = await _httpClient
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        var wireResponse = await httpResponse.Content
            .ReadFromJsonAsync<OpenAiChatCompletionResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (wireResponse is null)
        {
            throw new InvalidOperationException("OpenAI response body was empty or invalid JSON.");
        }

        return OpenAiResponseMapper.ToChatResponse(wireResponse, model);
    }

    public async IAsyncEnumerable<NeutralStreamEvent> StreamAsync(
        ChatRequest request,
        ModelRef model,
        ProviderCredential credential,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var wireRequest = OpenAiRequestMapper.ToWireRequest(request, model, stream: true);
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
        var state = new OpenAiStreamState();

        await foreach (var item in parser.EnumerateAsync(cancellationToken).ConfigureAwait(false))
        {
            if (item.Data is "[DONE]")
            {
                break;
            }

            var chunk = JsonSerializer.Deserialize<OpenAiChatCompletionChunk>(item.Data, JsonOptions);
            if (chunk is null)
            {
                continue;
            }

            foreach (var streamEvent in state.Process(chunk))
            {
                yield return streamEvent;
            }
        }

        foreach (var streamEvent in state.Finalize())
        {
            yield return streamEvent;
        }
    }

    private static HttpRequestMessage CreateHttpRequest(OpenAiChatRequest body, ProviderCredential credential)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.ApiKey);
        return httpRequest;
    }
}
