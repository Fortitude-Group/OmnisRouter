using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;
using OmnisRouter.Vigil;

namespace OmnisRouter.Api.Tests;

public class VigilReceiptPusherTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<(string Url, string? Auth, string Body)> Requests { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.Accepted;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
            return new HttpResponseMessage(Status);
        }
    }

    private sealed class OneClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static DecisionLogEntry Seed(string id) => new()
    {
        Id = id,
        TenantId = "default",
        Timestamp = DateTimeOffset.UtcNow,
        RequestHash = "h",
        ClientFormat = ClientFormat.OpenAI,
        ChosenProvider = Provider.OpenAI,
        ChosenModelId = "gpt-5-nano",
        PolicyVersion = "v5",
        Outcome = RequestOutcome.Success,
        LatencyMs = 5,
        ActualInputTokens = 5,
        ActualOutputTokens = 1,
        ActualCacheCreationTokens = 0,
        ActualCacheReadTokens = 0,
        ActualCostUsd = 0.001m,
        ActualCostDeltaVsBigUsd = -0.01m,
    };

    private static VigilReceiptPusher Pusher(IServiceScopeFactory scopes, HttpMessageHandler handler, string routerId = "router-eu-1") =>
        new(scopes, new OneClientFactory(handler),
            new OmnisVigilOptions { Enabled = true, Endpoint = "https://vigil.test", ProjectKey = "pk-123", BatchSize = 200 },
            new RouterIdentity(routerId), NullLogger<VigilReceiptPusher>.Instance);

    [Fact]
    public async Task Drain_pushes_receipts_with_router_id_then_advances_the_cursor()
    {
        using var factory = new OmnisApiFactory();
        var scopes = factory.Services.GetRequiredService<IServiceScopeFactory>();
        using (var scope = scopes.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDecisionLog>()
                .AppendAsync(Seed("seed-1"), CancellationToken.None);
        }

        var handler = new CapturingHandler();
        var pusher = Pusher(scopes, handler);

        await pusher.DrainAsync(CancellationToken.None);

        Assert.Single(handler.Requests);
        Assert.EndsWith("/v1/ingest", handler.Requests[0].Url);
        Assert.Equal("Bearer pk-123", handler.Requests[0].Auth);

        var body = JsonSerializer.Deserialize<JsonElement>(handler.Requests[0].Body);
        Assert.Equal(1, body.GetProperty("schema_version").GetInt32());
        var record = body.GetProperty("records")[0];
        Assert.Equal("seed-1", record.GetProperty("id").GetString());
        Assert.Equal("router-eu-1", record.GetProperty("router_id").GetString());

        // Cursor advanced: a second drain finds nothing new to push.
        await pusher.DrainAsync(CancellationToken.None);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Drain_retains_the_batch_when_ingest_fails()
    {
        using var factory = new OmnisApiFactory();
        var scopes = factory.Services.GetRequiredService<IServiceScopeFactory>();
        using (var scope = scopes.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDecisionLog>()
                .AppendAsync(Seed("seed-2"), CancellationToken.None);
        }

        var handler = new CapturingHandler { Status = HttpStatusCode.InternalServerError };
        var pusher = Pusher(scopes, handler);

        await pusher.DrainAsync(CancellationToken.None); // 500 -> batch retained
        await pusher.DrainAsync(CancellationToken.None); // retried, cursor never advanced

        Assert.Equal(2, handler.Requests.Count); // same batch attempted twice (at-least-once)
    }
}
