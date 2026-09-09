using System.Net;
using System.Net.Sockets;
using System.Text;
using OmnisRouter.LocalProxy;

namespace OmnisRouter.LocalProxy.Tests;

public sealed class RouterManagementClientTests
{
    // A minimal HttpListener stub standing in for the real router's loopback API, so the client is
    // exercised against real HTTP rather than a mocked HttpMessageHandler.
    private sealed class StubServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly Task _acceptLoop;

        public int Port { get; }

        public string LastMethod { get; private set; } = "";

        public string LastPath { get; private set; } = "";

        public string? LastAuthorizationHeader { get; private set; }

        public string LastBody { get; private set; } = "";

        public Func<HttpListenerContext, Task>? Handler { get; set; }

        public StubServer()
        {
            Port = GetFreeLoopbackPort();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _acceptLoop = RunAsync();
        }

        private static int GetFreeLoopbackPort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private async Task RunAsync()
        {
            while (true)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch
                {
                    return;
                }

                LastMethod = ctx.Request.HttpMethod;
                LastPath = ctx.Request.Url?.AbsolutePath ?? "";
                LastAuthorizationHeader = ctx.Request.Headers["Authorization"];
                using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                {
                    LastBody = await reader.ReadToEndAsync();
                }

                var handler = Handler;
                if (handler is not null)
                {
                    await handler(ctx);
                }
                else
                {
                    ctx.Response.StatusCode = 200;
                    ctx.Response.Close();
                }
            }
        }

        public static void Respond(HttpListenerContext ctx, int statusCode, string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.StatusCode = statusCode;
            ctx.Response.ContentType = "application/json";
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
        }
    }

    private static HttpClient ClientFor(StubServer server) => new() { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}") };

    [Fact]
    public async Task CreateKeyAsync_SendsBearerAndBody_ParsesCreatedResponse()
    {
        using var server = new StubServer();
        server.Handler = ctx =>
        {
            StubServer.Respond(ctx, 201, "{\"id\":\"key-1\",\"provider\":\"anthropic\",\"label\":\"work\",\"created_at\":\"2026-01-01T00:00:00Z\"}");
            return Task.CompletedTask;
        };

        using var http = ClientFor(server);
        var client = new RouterManagementClient(http, "test-token");

        var info = await client.CreateKeyAsync("Anthropic", "work", "sk-raw-key");

        Assert.Equal("POST", server.LastMethod);
        Assert.Equal("/v1/keys", server.LastPath);
        Assert.Equal("Bearer test-token", server.LastAuthorizationHeader);
        Assert.Contains("\"provider\":\"anthropic\"", server.LastBody);
        Assert.Contains("\"label\":\"work\"", server.LastBody);
        Assert.Contains("\"api_key\":\"sk-raw-key\"", server.LastBody);

        Assert.Equal("key-1", info.Id);
        Assert.Equal("anthropic", info.Provider);
        Assert.Equal("work", info.Label);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), info.CreatedAt);
        Assert.Null(info.LastUsedAt);
    }

    [Fact]
    public async Task ListKeysAsync_ParsesList()
    {
        using var server = new StubServer();
        server.Handler = ctx =>
        {
            StubServer.Respond(ctx, 200,
                "[{\"id\":\"a\",\"provider\":\"openai\",\"label\":\"l1\",\"created_at\":\"2026-01-01T00:00:00Z\",\"last_used_at\":null}," +
                "{\"id\":\"b\",\"provider\":\"gemini\",\"label\":\"l2\",\"created_at\":\"2026-01-02T00:00:00Z\",\"last_used_at\":\"2026-01-03T00:00:00Z\"}]");
            return Task.CompletedTask;
        };

        using var http = ClientFor(server);
        var client = new RouterManagementClient(http, "tok");

        var keys = await client.ListKeysAsync();

        Assert.Equal("GET", server.LastMethod);
        Assert.Equal("/v1/keys", server.LastPath);
        Assert.Equal("Bearer tok", server.LastAuthorizationHeader);
        Assert.Equal(2, keys.Count);
        Assert.Equal("a", keys[0].Id);
        Assert.Null(keys[0].LastUsedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-01-03T00:00:00Z"), keys[1].LastUsedAt);
    }

    [Fact]
    public async Task DeleteKeyAsync_IssuesDelete_TolerantOf204()
    {
        using var server = new StubServer();
        server.Handler = ctx =>
        {
            ctx.Response.StatusCode = 204;
            ctx.Response.Close();
            return Task.CompletedTask;
        };

        using var http = ClientFor(server);
        var client = new RouterManagementClient(http, "tok");

        await client.DeleteKeyAsync("key-1");

        Assert.Equal("DELETE", server.LastMethod);
        Assert.Equal("/v1/keys/key-1", server.LastPath);
        Assert.Equal("Bearer tok", server.LastAuthorizationHeader);
    }

    [Fact]
    public async Task CreateKeyAsync_NonSuccess_ThrowsWithStatusAndBody()
    {
        using var server = new StubServer();
        server.Handler = ctx =>
        {
            StubServer.Respond(ctx, 401, "{\"error\":\"bad token\"}");
            return Task.CompletedTask;
        };

        using var http = ClientFor(server);
        var client = new RouterManagementClient(http, "tok");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.CreateKeyAsync("openai", "l", "k"));

        Assert.Contains("401", ex.Message);
        Assert.Contains("bad token", ex.Message);
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(503, false)]
    public async Task IsReadyAsync_ReflectsStatusCode(int statusCode, bool expected)
    {
        using var server = new StubServer();
        server.Handler = ctx =>
        {
            ctx.Response.StatusCode = statusCode;
            ctx.Response.Close();
            return Task.CompletedTask;
        };

        using var http = ClientFor(server);
        var client = new RouterManagementClient(http, "tok");

        Assert.Equal(expected, await client.IsReadyAsync());
    }

    [Fact]
    public async Task IsHealthyAsync_True_On200()
    {
        using var server = new StubServer();
        server.Handler = ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.Close();
            return Task.CompletedTask;
        };

        using var http = ClientFor(server);
        var client = new RouterManagementClient(http, "tok");

        Assert.True(await client.IsHealthyAsync());
    }

    [Fact]
    public async Task CreateKeyAsync_InvalidProvider_ThrowsWithoutAnyHttpCall()
    {
        using var server = new StubServer();
        var called = false;
        server.Handler = ctx =>
        {
            called = true;
            ctx.Response.StatusCode = 200;
            ctx.Response.Close();
            return Task.CompletedTask;
        };

        using var http = ClientFor(server);
        var client = new RouterManagementClient(http, "tok");

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateKeyAsync("mistral", "l", "k"));

        Assert.False(called);
    }
}
