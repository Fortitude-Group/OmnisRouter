using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmnisRouter.Telemetry.Redaction;

namespace OmnisRouter.Api.Tests;

public class RedactionTests
{
    [Theory]
    [InlineData(
        "key=sk-abcdefghijklmnopqrstuvwxyz012345",
        "key=***redacted***")]
    [InlineData(
        "key=sk-ant-api03-abcdefghijklmnopqrstuvwxyz0123456789",
        "key=***redacted***")]
    public void Redact_masks_openai_and_anthropic_style_keys(string input, string expected)
    {
        Assert.Equal(expected, SecretRedactor.Redact(input));
    }

    [Fact]
    public void Redact_masks_bearer_token_but_keeps_the_bearer_scheme_word()
    {
        var input = "Authorization: Bearer abcdefghijklmnopqrstuvwxyz0123456789";

        var result = SecretRedactor.Redact(input);

        Assert.Equal("Authorization: Bearer ***redacted***", result);
    }

    [Fact]
    public void Redact_masks_long_hex_and_base64_looking_secrets()
    {
        var hex = new string('a', 40);
        var base64ish = new string('A', 44) + "==";

        Assert.Equal("***redacted***", SecretRedactor.Redact(hex));
        Assert.Equal("***redacted***", SecretRedactor.Redact(base64ish));
    }

    [Theory]
    [InlineData("Hello, this is a perfectly ordinary log message about routing a request.")]
    [InlineData("Routed to openai/gpt-5-mini with decision=ROUTED, cluster_id=3, confidence=0.87.")]
    [InlineData("user-id-4821 requested model auto")]
    public void Redact_leaves_ordinary_prose_untouched(string prose)
    {
        Assert.Equal(prose, SecretRedactor.Redact(prose));
    }

    [Fact]
    public void Redact_does_not_mangle_a_guid_without_dashes()
    {
        // 32 hex chars — a common non-secret identifier shape (GUID with dashes stripped). The
        // redactor's floor is 40 chars specifically so it does not catch this.
        var guidNoDashes = Guid.NewGuid().ToString("N");

        Assert.Equal(guidNoDashes, SecretRedactor.Redact(guidNoDashes));
    }

    [Fact]
    public void Redact_returns_empty_string_for_null_or_empty_input()
    {
        Assert.Equal(string.Empty, SecretRedactor.Redact(null));
        Assert.Equal(string.Empty, SecretRedactor.Redact(string.Empty));
    }

    /// <summary>Captures every formatted message handed to it, without any redaction of its own.</summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public CapturingLogger Logger { get; } = new();

        public ILogger CreateLogger(string categoryName) => Logger;

        public void Dispose()
        {
        }
    }

    [Fact]
    public void RedactingLoggerProvider_scrubs_secrets_from_messages_reaching_the_wrapped_sink()
    {
        var captured = new CapturingLogger();
        var provider = new RedactingLoggerProvider(new PassThroughProvider(captured));
        var logger = provider.CreateLogger("test");

        logger.LogInformation("leaked key: {Key}", "sk-abcdefghijklmnopqrstuvwxyz012345");

        var message = Assert.Single(captured.Messages);
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz012345", message);
        Assert.Contains("***redacted***", message);
    }

    private sealed class PassThroughProvider : ILoggerProvider
    {
        private readonly ILogger _logger;

        public PassThroughProvider(ILogger logger) => _logger = logger;

        public ILogger CreateLogger(string categoryName) => _logger;

        public void Dispose()
        {
        }
    }

    [Fact]
    public void AddOmnisRedaction_decorates_registered_logger_providers()
    {
        var captured = new CapturingLogger();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerProvider>(new PassThroughProvider(captured));

        services.AddOmnisRedaction();

        using var provider = services.BuildServiceProvider();
        var loggerProvider = provider.GetRequiredService<ILoggerProvider>();

        Assert.IsType<RedactingLoggerProvider>(loggerProvider);

        var logger = loggerProvider.CreateLogger("test");
        logger.LogInformation("secret bearer token: Bearer abcdefghijklmnopqrstuvwxyz0123456789");

        var message = Assert.Single(captured.Messages);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz0123456789", message);
    }
}
