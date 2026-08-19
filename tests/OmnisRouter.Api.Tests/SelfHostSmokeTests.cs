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

/// <summary>
/// FR-015/SC-008: the router must run as a single self-contained process with a local embedded
/// datastore, requiring no external service for core routing. <see cref="OmnisApiFactory"/> already
/// points the store at a private SQLite temp file rather than any external database (see its own
/// doc comment) — no Postgres, no external cache, nothing else is started for this test to pass.
/// The only thing stubbed here is the upstream provider HTTP call itself, since that call is by
/// definition to an external, real-world service (the LLM provider) — the point being proven is
/// that CORE ROUTING (auth, store, decisioning, health/readiness) needs nothing beyond the process
/// itself and its embedded SQLite file.
/// </summary>
public class SelfHostSmokeTests
{
    private const string CannedCompletion =
        """{"id":"c","object":"chat.completion","created":0,"model":"gpt-5-mini","choices":[{"index":0,"message":{"role":"assistant","content":"4"},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":1,"total_tokens":6}}""";

    private sealed class StubOpenAiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CannedCompletion, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class SelfHostFactory : OmnisApiFactory
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

    private static void Seed(SelfHostFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OmnisRouterDbContext>();
        db.RouterTokens.Add(new RouterToken
        {
            Id = Guid.NewGuid().ToString("n"),
            TenantId = "default",
            HashedToken = RouterTokenHasher.Hash("test-token"),
            Name = "self-host-smoke",
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        db.ProviderKeys.Add(new ProviderKey
        {
            Id = Guid.NewGuid().ToString("n"),
            TenantId = "default",
            Provider = Provider.OpenAI,
            Label = "self-host-smoke",
            ApiKey = "sk-test-key",
            KeyVersion = 1,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task Health_endpoint_is_ok_with_no_external_service_running()
    {
        using var factory = new SelfHostFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readyz_endpoint_is_ok_against_the_embedded_sqlite_store()
    {
        using var factory = new SelfHostFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ready", payload.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Routes_one_request_end_to_end_with_only_the_process_and_its_embedded_store()
    {
        using var factory = new SelfHostFactory();
        Seed(factory);
        var client = factory.CreateClient();

        var body = """{"model":"auto","messages":[{"role":"user","content":"2+2?"}]}""";
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            Headers = { Authorization = new("Bearer", "test-token") },
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("chat.completion", payload.GetProperty("object").GetString());
    }
}
