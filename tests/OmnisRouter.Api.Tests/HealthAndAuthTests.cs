using System.Net;
using System.Net.Http.Json;

namespace OmnisRouter.Api.Tests;

public sealed class HealthAndAuthTests : IClassFixture<OmnisApiFactory>
{
    private readonly OmnisApiFactory _factory;

    public HealthAndAuthTests(OmnisApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedRoute_WithoutBearerToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/messages", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"type\":\"error\"", body);
    }

    [Fact]
    public async Task ProtectedRoute_WithUnknownToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer not-a-real-token");

        var response = await client.PostAsJsonAsync("/v1/chat/completions", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
