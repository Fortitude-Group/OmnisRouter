using System.Net;
using System.Text;
using OmnisRouter.LocalProxy;

namespace OmnisRouter.LocalProxy.Tests;

public class RouterManagementClientCacheTests
{
    private sealed class StubHandler(HttpStatusCode code, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private static RouterManagementClient Client(HttpStatusCode code, string body) =>
        new(new HttpClient(new StubHandler(code, body)) { BaseAddress = new Uri("http://127.0.0.1:9/") }, "t");

    private const string SummaryJson = """
        {
          "measurement_enabled": true,
          "source": "routed",
          "since_start": {"avoidable_waste_gbp":0.04,"recovered_gbp":0.01,"recomputed_tokens":400,"saved_tokens":100,"miss_count":3,"recovery_count":1},
          "today": {"avoidable_waste_gbp":0.02,"recovered_gbp":0.01,"recomputed_tokens":200,"saved_tokens":100,"miss_count":2,"recovery_count":1},
          "pricing_version": "2026-08-15",
          "fx_date": "2026-08-15",
          "usd_gbp": 0.79,
          "shadow_price": false
        }
        """;

    [Fact]
    public async Task GetCacheSummaryAsync_parses_the_summary()
    {
        var summary = await Client(HttpStatusCode.OK, SummaryJson).GetCacheSummaryAsync();

        Assert.NotNull(summary);
        Assert.True(summary!.MeasurementEnabled);
        Assert.Equal("routed", summary.Source);
        Assert.Equal(0.02m, summary.Today.AvoidableWasteGbp);
        Assert.Equal(3, summary.SinceStart.MissCount);
        Assert.Equal(0.79m, summary.UsdGbp);
        Assert.False(summary.ShadowPrice);
    }

    [Fact]
    public async Task GetCacheSummaryAsync_returns_null_when_the_router_errors()
    {
        var summary = await Client(HttpStatusCode.ServiceUnavailable, "").GetCacheSummaryAsync();

        Assert.Null(summary);   // fail-open: the tray shows "not measuring"
    }
}
