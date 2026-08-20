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

public class Us2TransparencyTests
{
    private const string CannedCompletion =
        """{"id":"c","object":"chat.completion","created":0,"model":"gpt-5-mini","choices":[{"index":0,"message":{"role":"assistant","content":"4"},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":1,"total_tokens":6}}""";

    private sealed class ConfigurableUpstreamFactory : OmnisApiFactory
    {
        public int UpstreamCalls;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<IUpstreamClient, OpenAiUpstreamClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new Handler(this)));
        }

        private sealed class Handler(ConfigurableUpstreamFactory factory) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref factory.UpstreamCalls);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(CannedCompletion, Encoding.UTF8, "application/json"),
                });
            }
        }
    }

    private static void SeedToken(ConfigurableUpstreamFactory factory, bool withKey)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OmnisRouterDbContext>();
        db.RouterTokens.Add(new RouterToken
        {
            Id = Guid.NewGuid().ToString("n"),
            TenantId = "default",
            HashedToken = RouterTokenHasher.Hash("test-token"),
            Name = "us2",
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        if (withKey)
        {
            db.ProviderKeys.Add(new ProviderKey
            {
                Id = Guid.NewGuid().ToString("n"),
                TenantId = "default",
                Provider = Provider.OpenAI,
                Label = "us2",
                ApiKey = "sk-test",
                KeyVersion = 1,
                CreatedAt = DateTimeOffset.UnixEpoch,
            });
        }

        db.SaveChanges();
    }

    private static HttpRequestMessage Post(string path, string body) => new(HttpMethod.Post, path)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
        Headers = { Authorization = new("Bearer", "test-token") },
    };

    [Fact]
    public async Task Route_endpoint_returns_full_decision_with_zero_upstream_calls()
    {
        using var factory = new ConfigurableUpstreamFactory();
        SeedToken(factory, withKey: true);
        var client = factory.CreateClient();

        var response = await client.SendAsync(Post("/v1/route", """{"model":"auto","messages":[{"role":"user","content":"hi"}]}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, factory.UpstreamCalls); // decision-only: no upstream, no cost

        var decision = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("openai", decision.GetProperty("chosen").GetProperty("provider").GetString());
        Assert.Equal("v3-omnisbench-2026-08-20", decision.GetProperty("policy_version").GetString());
    }

    [Fact]
    public async Task Route_receipt_has_all_required_schema_fields()
    {
        using var factory = new ConfigurableUpstreamFactory();
        SeedToken(factory, withKey: true);
        var client = factory.CreateClient();

        var response = await client.SendAsync(Post("/v1/route", """{"model":"auto","messages":[{"role":"user","content":"hi"}]}"""));
        var d = await response.Content.ReadFromJsonAsync<JsonElement>();

        foreach (var field in new[]
                 {
                     "policy_version", "cluster_id", "confidence", "confidence_floor", "decision",
                     "reason", "chosen", "alternatives", "est_cost_usd", "est_cost_delta_vs_big_usd",
                     "session_pin_applied",
                 })
        {
            Assert.True(d.TryGetProperty(field, out _), $"receipt missing required field '{field}'");
        }

        Assert.Contains(d.GetProperty("decision").GetString(), new[] { "ROUTED", "ESCALATED" });
    }

    [Fact]
    public async Task Decision_log_export_records_the_request_content_free()
    {
        using var factory = new ConfigurableUpstreamFactory();
        SeedToken(factory, withKey: true);
        var client = factory.CreateClient();

        // Route one real request (uses the stubbed upstream), then export the log.
        var secret = "SENSITIVE-PROMPT-9times7";
        var chat = await client.SendAsync(Post("/v1/chat/completions", $$"""{"model":"auto","messages":[{"role":"user","content":"{{secret}}"}]}"""));
        Assert.Equal(HttpStatusCode.OK, chat.StatusCode);

        var export = await client.SendAsync(Headers("test-token"));
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var ndjson = await export.Content.ReadAsStringAsync();

        var lines = ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        var row = JsonSerializer.Deserialize<JsonElement>(lines[0]);
        Assert.False(string.IsNullOrEmpty(row.GetProperty("request_hash").GetString()));
        Assert.Equal("openai", row.GetProperty("chosen_provider").GetString());
        Assert.DoesNotContain(secret, ndjson); // no prompt content leaks into the log
    }

    private static HttpRequestMessage Headers(string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/v1/analytics/routing-decisions");
        req.Headers.Authorization = new("Bearer", token);
        return req;
    }
}
