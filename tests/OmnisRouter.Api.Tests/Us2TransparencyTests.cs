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
using OmnisRouter.Vigil;

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

    [Fact]
    public async Task Decision_log_records_actual_usage_and_cost_after_a_routed_call()
    {
        using var factory = new ConfigurableUpstreamFactory();
        SeedToken(factory, withKey: true);
        var client = factory.CreateClient();

        var chat = await client.SendAsync(Post("/v1/chat/completions", """{"model":"auto","messages":[{"role":"user","content":"2+2"}]}"""));
        Assert.Equal(HttpStatusCode.OK, chat.StatusCode);

        var export = await client.SendAsync(Headers("test-token"));
        var ndjson = await export.Content.ReadAsStringAsync();
        var row = JsonSerializer.Deserialize<JsonElement>(
            ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0]);

        // The canned completion reports usage {prompt_tokens:5, completion_tokens:1}, so actual
        // token accounting must land in the log, not just the pre-call estimate.
        Assert.Equal(5, row.GetProperty("actual_input_tokens").GetInt32());
        Assert.Equal(1, row.GetProperty("actual_output_tokens").GetInt32());
        Assert.Equal(0, row.GetProperty("actual_cache_read_tokens").GetInt32());

        // Priced from the real snapshot: a real, positive actual cost, and a routed choice never
        // costs more than the strongest candidate (saving is zero or negative).
        Assert.True(row.GetProperty("actual_cost_usd").GetDouble() > 0);
        Assert.True(row.GetProperty("actual_cost_delta_vs_big_usd").GetDouble() <= 0);
    }

    [Fact]
    public async Task Attribution_tags_from_headers_land_in_the_log_and_are_length_capped()
    {
        using var factory = new ConfigurableUpstreamFactory();
        SeedToken(factory, withKey: true);
        var client = factory.CreateClient();

        var req = Post("/v1/chat/completions", """{"model":"auto","messages":[{"role":"user","content":"hi"}]}""");
        req.Headers.Add("X-Omnis-Project", "web-app");
        req.Headers.Add("X-Omnis-Team", "payments");
        req.Headers.Add("X-Omnis-Client", "claude-code");
        req.Headers.Add("X-Omnis-Commit", new string('a', 250)); // over the 200-char cap
        var chat = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, chat.StatusCode);

        var export = await client.SendAsync(Headers("test-token"));
        var ndjson = await export.Content.ReadAsStringAsync();
        var row = JsonSerializer.Deserialize<JsonElement>(
            ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0]);

        var tags = row.GetProperty("tags");
        Assert.Equal("web-app", tags.GetProperty("project").GetString());
        Assert.Equal("payments", tags.GetProperty("team").GetString());
        Assert.Equal("claude-code", tags.GetProperty("client_name").GetString());
        Assert.Equal(200, tags.GetProperty("commit").GetString()!.Length); // capped
    }

    [Fact]
    public async Task Org_kill_switch_halts_routed_requests_before_any_upstream_spend()
    {
        using var factory = new ConfigurableUpstreamFactory();
        SeedToken(factory, withKey: true);

        // Engage the org kill-switch in the shared policy state, as a fresh policy poll would.
        factory.Services.GetRequiredService<VigilPolicyState>()
            .Update(new VigilPolicy { Kill = new PolicyKill { Org = true } });

        var client = factory.CreateClient();
        var resp = await client.SendAsync(
            Post("/v1/chat/completions", """{"model":"auto","messages":[{"role":"user","content":"hi"}]}"""));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal(0, factory.UpstreamCalls); // halted before the upstream call, so no spend
    }

    private static HttpRequestMessage Headers(string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/v1/analytics/routing-decisions");
        req.Headers.Authorization = new("Bearer", token);
        return req;
    }
}
