using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OmnisRouter.Api.Auth;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Store;
using OmnisRouter.Store.Entities;
using OmnisRouter.Upstream.Providers;

namespace OmnisRouter.Api.Tests;

public class RouteOpenAiEndToEndTests
{
    private const string CannedCompletion =
        """
        {"id":"chatcmpl-stub","object":"chat.completion","created":0,"model":"gpt-5-mini",
         "choices":[{"index":0,"message":{"role":"assistant","content":"4"},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":5,"completion_tokens":1,"total_tokens":6}}
        """;

    /// <summary>Intercepts the upstream OpenAI call — no real network — returning a canned completion.</summary>
    private sealed class StubOpenAiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CannedCompletion, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class StubbedFactory : OmnisApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<IUpstreamClient, OpenAiUpstreamClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new StubOpenAiHandler());
            });
        }
    }

    private static void Seed(StubbedFactory factory, bool withProviderKey)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OmnisRouterDbContext>();
        db.RouterTokens.Add(new RouterToken
        {
            Id = Guid.NewGuid().ToString("n"),
            TenantId = "default",
            HashedToken = RouterTokenHasher.Hash("test-token"),
            Name = "e2e",
            CreatedAt = DateTimeOffset.UnixEpoch,
        });

        if (withProviderKey)
        {
            db.ProviderKeys.Add(new ProviderKey
            {
                Id = Guid.NewGuid().ToString("n"),
                TenantId = "default",
                Provider = Provider.OpenAI,
                Label = "e2e",
                ApiKey = "sk-test-key",
                KeyVersion = 1,
                CreatedAt = DateTimeOffset.UnixEpoch,
            });
        }

        db.SaveChanges();
    }

    private static HttpRequestMessage ChatRequest(string prompt)
    {
        var body = $$"""{"model":"auto","messages":[{"role":"user","content":"{{prompt}}"}]}""";
        return new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            Headers = { Authorization = new("Bearer", "test-token") },
        };
    }

    [Fact]
    public async Task Routes_to_an_openai_model_and_returns_openai_response_with_receipt()
    {
        using var factory = new StubbedFactory();
        Seed(factory, withProviderKey: true);
        var client = factory.CreateClient();

        var response = await client.SendAsync(ChatRequest("2+2?"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Receipt headers present, routed to a reachable (OpenAI) model, stamped with the seed policy.
        Assert.True(response.Headers.Contains("X-Omnis-Model"));
        var model = response.Headers.GetValues("X-Omnis-Model").Single();
        Assert.StartsWith("openai/", model);
        Assert.Equal("v2-onnx-2026-08-20", response.Headers.GetValues("X-Omnis-Policy").Single());
        Assert.Contains(response.Headers.GetValues("X-Omnis-Decision").Single(), new[] { "Routed", "Escalated" });

        // Response is a well-formed OpenAI chat.completion.
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("chat.completion", payload.GetProperty("object").GetString());
        var content = payload.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        Assert.Equal("4", content);
    }

    private static HttpRequestMessage PostTo(string path, string body) => new(HttpMethod.Post, path)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
        Headers = { Authorization = new("Bearer", "test-token") },
    };

    [Fact]
    public async Task Anthropic_request_routes_cross_provider_and_returns_anthropic_response()
    {
        // Only OpenAI is keyed, so an Anthropic-format request is served cross-provider by an OpenAI
        // model and translated back into Anthropic's response shape.
        using var factory = new StubbedFactory();
        Seed(factory, withProviderKey: true);
        var client = factory.CreateClient();

        var response = await client.SendAsync(PostTo("/v1/messages",
            """{"model":"claude-x","max_tokens":64,"messages":[{"role":"user","content":"2+2?"}]}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("openai/", response.Headers.GetValues("X-Omnis-Model").Single());

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("message", payload.GetProperty("type").GetString());
        var text = payload.GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Equal("4", text);
    }

    [Fact]
    public async Task Gemini_request_routes_cross_provider_and_returns_gemini_response()
    {
        using var factory = new StubbedFactory();
        Seed(factory, withProviderKey: true);
        var client = factory.CreateClient();

        var response = await client.SendAsync(PostTo("/v1beta/models/gemini-2.5-flash:generateContent",
            """{"contents":[{"role":"user","parts":[{"text":"2+2?"}]}]}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("openai/", response.Headers.GetValues("X-Omnis-Model").Single());

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var text = payload.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
        Assert.Equal("4", text);
    }

    [Fact]
    public async Task Returns_error_when_no_byok_key_configured()
    {
        using var factory = new StubbedFactory();
        Seed(factory, withProviderKey: false);
        var client = factory.CreateClient();

        var response = await client.SendAsync(ChatRequest("hello"));

        // No provider has a key → no routable models (never picks a model it can't call).
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
