using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OmnisRouter.Telemetry.Redaction;
using OmnisRouter.Vigil;

namespace OmnisRouter.Api.Tests;

public class VigilSignupHintTests
{
    private sealed class CapturingLogger : ILogger<VigilSignupHint>
    {
        public List<(LogLevel Level, string Text)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Lines.Add((logLevel, formatter(state, exception)));
    }

    private static IServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();
        return new ServiceCollection().AddLogging().AddOmnisVigil(config).BuildServiceProvider();
    }

    private static bool HintRegistered(IServiceProvider sp) =>
        sp.GetServices<IHostedService>().OfType<VigilSignupHint>().Any();

    [Fact]
    public async Task Logs_one_line_with_the_cli_link_when_vigil_is_not_connected()
    {
        var logger = new CapturingLogger();
        var hint = new VigilSignupHint(new OmnisVigilOptions(), logger);

        await hint.StartAsync(CancellationToken.None);
        await hint.StopAsync(CancellationToken.None);

        var line = Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains("https://app.omnisvigil.com/signup?utm_source=omnisrouter&utm_medium=cli", line.Text);
        Assert.Contains("OmnisVigil__ShowSignupHint=false", line.Text);
    }

    [Fact]
    public async Task Stays_quiet_when_silenced_by_config()
    {
        var logger = new CapturingLogger();
        var hint = new VigilSignupHint(new OmnisVigilOptions { ShowSignupHint = false }, logger);

        await hint.StartAsync(CancellationToken.None);

        Assert.Empty(logger.Lines);
    }

    [Fact]
    public void Shows_by_default_and_while_vigil_is_only_partly_configured()
    {
        Assert.True(VigilSignupHint.ShouldShow(new OmnisVigilOptions()));
        Assert.True(VigilSignupHint.ShouldShow(new OmnisVigilOptions { Enabled = true, Endpoint = "https://vigil.test" }));
        Assert.True(VigilSignupHint.ShouldShow(new OmnisVigilOptions { Endpoint = "https://vigil.test", ProjectKey = "pk-1" }));
    }

    [Fact]
    public void Goes_quiet_once_vigil_is_connected()
    {
        Assert.False(VigilSignupHint.ShouldShow(new OmnisVigilOptions
        {
            Enabled = true, Endpoint = "https://vigil.test", ProjectKey = "pk-1",
        }));
    }

    [Fact]
    public void Registered_as_a_startup_service_only_when_it_should_show()
    {
        Assert.True(HintRegistered(Build(("Unrelated:Key", "x"))));
        Assert.False(HintRegistered(Build(("OmnisVigil:ShowSignupHint", "false"))));
        Assert.False(HintRegistered(Build(
            ("OmnisVigil:Enabled", "true"),
            ("OmnisVigil:Endpoint", "https://vigil.test"),
            ("OmnisVigil:ProjectKey", "pk-1"))));
    }

    [Fact]
    public void The_log_redactor_leaves_the_link_intact()
    {
        Assert.Equal(VigilSignupHint.Message, SecretRedactor.Redact(VigilSignupHint.Message));
    }
}
