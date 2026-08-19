using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;

namespace OmnisRouter.Upstream.Providers;

/// <summary>Options for <see cref="OpenRouterUpstreamClient"/>.</summary>
public sealed record OpenRouterUpstreamOptions
{
    public string BaseUrl { get; init; } = "https://openrouter.ai/api/v1/";
}

/// <summary>
/// OpenRouter upstream client. OpenRouter speaks the OpenAI Chat Completions wire format, so this
/// reuses the OpenAI request/response/stream mappers and differs only in provider identity + base URL.
/// </summary>
public sealed class OpenRouterUpstreamClient : IUpstreamClient
{
    private readonly HttpClient _httpClient;

    public OpenRouterUpstreamClient(HttpClient httpClient, OpenRouterUpstreamOptions options)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(options.BaseUrl);
    }

    public Provider Provider => Provider.OpenRouter;

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
            .ReadFromJsonAsync<OpenAiChatCompletionResponse>(OpenAiUpstreamClient.JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("OpenRouter response body was empty or invalid JSON.");

        return OpenAiResponseMapper.ToChatResponse(wireResponse, model);
    }

    public async IAsyncEnumerable<NeutralStreamEvent> StreamAsync(
        ChatRequest request, ModelRef model, ProviderCredential credential,
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
            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var parser = SseParser.Create(responseStream);
        var state = new OpenAiStreamState();

        await foreach (var item in parser.EnumerateAsync(cancellationToken).ConfigureAwait(false))
        {
            if (item.Data is "[DONE]")
            {
                break;
            }

            var chunk = JsonSerializer.Deserialize<OpenAiChatCompletionChunk>(item.Data, OpenAiUpstreamClient.JsonOptions);
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
            Content = JsonContent.Create(body, options: OpenAiUpstreamClient.JsonOptions),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.ApiKey);
        return httpRequest;
    }
}

/// <summary>DI registration for the OpenRouter upstream client.</summary>
public static class OpenRouterServiceCollectionExtensions
{
    public static IServiceCollection AddOmnisOpenRouterUpstream(
        this IServiceCollection services, Action<OpenRouterUpstreamOptions>? configureOptions = null)
    {
        var options = new OpenRouterUpstreamOptions();
        configureOptions?.Invoke(options);
        services.AddSingleton(options);

        services.AddHttpClient<IUpstreamClient, OpenRouterUpstreamClient>(client =>
            {
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 100,
            });

        return services;
    }
}
