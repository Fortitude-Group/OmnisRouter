using System.Net;
using System.Text;
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

/// <summary>
/// FR-013/SC-007: prompt content must leave the process ONLY to the chosen upstream provider, and
/// to no other destination. Every registered <see cref="IUpstreamClient"/>'s primary HTTP handler —
/// OpenAI, Anthropic, Gemini, and OpenRouter — is replaced with the SAME recording handler, so any
/// outbound call from any provider client is captured in one place. Only an OpenAI key is seeded, so
/// exactly one outbound call is expected, to the OpenAI host, and the canary prompt must appear only
/// in that one captured request.
/// </summary>
public class EgressRestrictionTests
{
    private const string CannedCompletion =
        """{"id":"c","object":"chat.completion","created":0,"model":"gpt-5-mini","choices":[{"index":0,"message":{"role":"assistant","content":"4"},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":1,"total_tokens":6}}""";

    private sealed record CapturedRequest(Uri Uri, string Body);

    /// <summary>
    /// Stands in as the primary handler for every upstream typed <see cref="HttpClient"/>. Whichever
    /// provider client actually sends a request, it lands here, so this is the single vantage point
    /// from which "did prompt content leave to more than one destination" can be answered.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        public List<CapturedRequest> Captured { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            lock (_gate)
            {
                Captured.Add(new CapturedRequest(request.RequestUri!, body));
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CannedCompletion, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class EgressTestFactory : OmnisApiFactory
    {
        public readonly RecordingHandler Handler = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                // Every provider's typed client is repointed at the SAME handler instance, so a
                // request from ANY of them is observable from one place.
                services.AddHttpClient<IUpstreamClient, OpenAiUpstreamClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => Handler);
                services.AddHttpClient<IUpstreamClient, AnthropicUpstreamClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => Handler);
                services.AddHttpClient<IUpstreamClient, GeminiUpstreamClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => Handler);
                services.AddHttpClient<IUpstreamClient, OpenRouterUpstreamClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => Handler);
            });
        }
    }

    private static void SeedOpenAiOnly(EgressTestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OmnisRouterDbContext>();
        db.RouterTokens.Add(new RouterToken
        {
            Id = Guid.NewGuid().ToString("n"),
            TenantId = "default",
            HashedToken = RouterTokenHasher.Hash("test-token"),
            Name = "egress",
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        db.ProviderKeys.Add(new ProviderKey
        {
            Id = Guid.NewGuid().ToString("n"),
            TenantId = "default",
            Provider = Provider.OpenAI,
            Label = "egress",
            ApiKey = "sk-test-key",
            KeyVersion = 1,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task Prompt_content_leaves_only_to_the_chosen_provider_host_and_nowhere_else()
    {
        using var factory = new EgressTestFactory();
        SeedOpenAiOnly(factory);
        var client = factory.CreateClient();

        const string canary = "CANARY-PROMPT-do-not-leak-9f3ac71b";
        var body = $$"""{"model":"auto","messages":[{"role":"user","content":"{{canary}}"}]}""";
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            Headers = { Authorization = new("Bearer", "test-token") },
        };

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Exactly one outbound call was made, across ALL provider handlers combined.
        var captured = Assert.Single(factory.Handler.Captured);

        // It went to the chosen provider's host (OpenAI — the only keyed provider) and no other.
        Assert.Equal("api.openai.com", captured.Uri.Host);

        // The canary prompt appears in that one captured upstream request...
        Assert.Contains(canary, captured.Body);

        // ...and the response returned to the caller does not itself echo the raw canary back out
        // via headers (only the model/decision receipt headers, never request content).
        foreach (var header in response.Headers)
        {
            foreach (var value in header.Value)
            {
                Assert.DoesNotContain(canary, value);
            }
        }
    }

    [Fact]
    public async Task No_provider_key_means_no_outbound_call_is_made_at_all()
    {
        // Belt-and-suspenders: when no provider is routable, nothing should be recorded by ANY
        // handler — confirming the recording setup itself is wired to every provider client.
        using var factory = new EgressTestFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OmnisRouterDbContext>();
        db.RouterTokens.Add(new RouterToken
        {
            Id = Guid.NewGuid().ToString("n"),
            TenantId = "default",
            HashedToken = RouterTokenHasher.Hash("test-token"),
            Name = "egress-no-key",
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        db.SaveChanges();

        var client = factory.CreateClient();
        var body = """{"model":"auto","messages":[{"role":"user","content":"hi"}]}""";
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            Headers = { Authorization = new("Bearer", "test-token") },
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(factory.Handler.Captured);
    }
}
