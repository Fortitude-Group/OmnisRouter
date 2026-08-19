using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmnisRouter.Api.Auth;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Store;
using OmnisRouter.Store.Entities;
using OmnisRouter.Upstream.Providers;

namespace OmnisRouter.Api.Tests;

/// <summary>
/// T055 — drives one routed request carrying a recognizable "canary" API key and prompt, then
/// asserts neither value ever surfaces anywhere observable outside the upstream call itself: app
/// logs, response headers, or the content-free decision-log export (FR-009).
/// </summary>
public sealed class NoLeakageTests
{
    private const string SecretApiKey = "sk-SECRET-abc123";
    private const string PromptCanary = "LEAKCANARY-prompt";

    private const string CannedCompletion =
        """
        {"id":"chatcmpl-canary","object":"chat.completion","created":0,"model":"gpt-5-mini",
         "choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":5,"completion_tokens":1,"total_tokens":6}}
        """;

    /// <summary>Captures every log line written through the app's <see cref="ILoggerFactory"/> for
    /// later assertion, without altering how/whether the app itself logs.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public readonly ConcurrentQueue<string> Lines = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Lines);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lines.Enqueue(formatter(state, exception));
                if (exception is not null)
                {
                    lines.Enqueue(exception.ToString());
                }
            }
        }
    }

    private sealed class StubOpenAiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CannedCompletion, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class LeakageTestFactory : OmnisApiFactory
    {
        public readonly CapturingLoggerProvider Logs = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ILoggerProvider>(Logs);
                services.AddHttpClient<IUpstreamClient, OpenAiUpstreamClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new StubOpenAiHandler());
            });
        }
    }

    private static async Task SeedAsync(LeakageTestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OmnisRouterDbContext>();
        db.RouterTokens.Add(new RouterToken
        {
            Id = Guid.NewGuid().ToString("n"),
            TenantId = "default",
            HashedToken = RouterTokenHasher.Hash("test-token"),
            Name = "leakage",
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        db.ProviderKeys.Add(new ProviderKey
        {
            Id = Guid.NewGuid().ToString("n"),
            TenantId = "default",
            Provider = Provider.OpenAI,
            Label = "leakage",
            ApiKey = SecretApiKey,
            KeyVersion = 1,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        await db.SaveChangesAsync();
    }

    private static HttpRequestMessage AuthedRequest(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return req;
    }

    [Fact]
    public async Task Routed_request_never_leaks_the_provider_key_or_the_prompt()
    {
        using var factory = new LeakageTestFactory();
        await SeedAsync(factory);
        var client = factory.CreateClient();

        var chatRequest = AuthedRequest(HttpMethod.Post, "/v1/chat/completions");
        chatRequest.Content = new StringContent(
            $$"""{"model":"auto","messages":[{"role":"user","content":"{{PromptCanary}}"}]}""",
            Encoding.UTF8, "application/json");

        var chatResponse = await client.SendAsync(chatRequest);
        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);

        // 1) Response headers (the routing "receipt") must carry neither secret.
        var headerText = string.Join(
            '\n',
            chatResponse.Headers.Select(h => $"{h.Key}: {string.Join(',', h.Value)}"));
        Assert.DoesNotContain(SecretApiKey, headerText, StringComparison.Ordinal);
        Assert.DoesNotContain(PromptCanary, headerText, StringComparison.Ordinal);

        // 2) App logs captured during the request must carry neither secret.
        var logText = string.Join('\n', factory.Logs.Lines);
        Assert.DoesNotContain(SecretApiKey, logText, StringComparison.Ordinal);
        Assert.DoesNotContain(PromptCanary, logText, StringComparison.Ordinal);

        // 3) The content-free decision-log export must carry neither secret.
        var exportResponse = await client.SendAsync(
            AuthedRequest(HttpMethod.Get, "/v1/analytics/routing-decisions"));
        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
        var ndjson = await exportResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(SecretApiKey, ndjson, StringComparison.Ordinal);
        Assert.DoesNotContain(PromptCanary, ndjson, StringComparison.Ordinal);
    }
}
