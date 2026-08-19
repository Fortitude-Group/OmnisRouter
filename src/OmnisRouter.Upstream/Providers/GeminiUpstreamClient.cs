using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;

namespace OmnisRouter.Upstream.Providers;

/// <summary>
/// Gemini <c>generateContent</c>/<c>streamGenerateContent</c> upstream wire client: builds the
/// Gemini request from the neutral <see cref="ChatRequest"/> for the chosen model, and parses the
/// Gemini response/SSE stream back into the neutral model. Auth is the <c>x-goog-api-key</c> header
/// (Gemini does not use a bearer token — research.md R2/R3). The streaming path uses
/// ResponseHeadersRead and is never retried (a retry after headers-read would resend the prompt and
/// double-charge). Gemini's SSE stream has no <c>[DONE]</c> sentinel; it simply ends.
/// </summary>
public sealed class GeminiUpstreamClient : IUpstreamClient
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;

    public GeminiUpstreamClient(HttpClient httpClient, GeminiUpstreamOptions options)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(options.BaseUrl);
    }

    public Provider Provider => Provider.Gemini;

    public async Task<ChatResponse> SendAsync(
        ChatRequest request, ModelRef model, ProviderCredential credential, CancellationToken cancellationToken)
    {
        var wireRequest = GeminiRequestMapper.ToWireRequest(request);
        using var httpRequest = CreateHttpRequest(
            $"v1beta/models/{model.ModelId}:generateContent", wireRequest, credential);

        using var httpResponse = await _httpClient
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        var wireResponse = await httpResponse.Content
            .ReadFromJsonAsync<GeminiGenerateContentResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (wireResponse is null)
        {
            throw new InvalidOperationException("Gemini response body was empty or invalid JSON.");
        }

        return GeminiResponseMapper.ToChatResponse(wireResponse, model);
    }

    public async IAsyncEnumerable<NeutralStreamEvent> StreamAsync(
        ChatRequest request,
        ModelRef model,
        ProviderCredential credential,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var wireRequest = GeminiRequestMapper.ToWireRequest(request);
        using var httpRequest = CreateHttpRequest(
            $"v1beta/models/{model.ModelId}:streamGenerateContent?alt=sse", wireRequest, credential);

        using var httpResponse = await _httpClient
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        yield return new StreamMessageStart(model);

        await using var responseStream = await httpResponse.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        var parser = SseParser.Create(responseStream);
        var state = new GeminiStreamState();

        await foreach (var item in parser.EnumerateAsync(cancellationToken).ConfigureAwait(false))
        {
            var chunk = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(item.Data, JsonOptions);
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

    private static HttpRequestMessage CreateHttpRequest(
        string relativeUrl, GeminiGenerateContentRequest body, ProviderCredential credential)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, relativeUrl)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        httpRequest.Headers.Add("x-goog-api-key", credential.ApiKey);
        return httpRequest;
    }
}
