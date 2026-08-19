using Microsoft.Extensions.Logging;

namespace OmnisRouter.Telemetry.Redaction;

/// <summary>
/// <see cref="ILogger"/> decorator that runs every formatted log message through
/// <see cref="SecretRedactor.Redact"/> before handing it to the wrapped logger. State/exception
/// objects are passed through unchanged (redaction only touches the rendered message text); scope
/// and enablement checks pass straight through to the inner logger.
/// </summary>
internal sealed class RedactingLogger : ILogger
{
    private readonly ILogger _inner;

    public RedactingLogger(ILogger inner)
    {
        _inner = inner;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _inner.Log(
            logLevel,
            eventId,
            state,
            exception,
            (s, e) => SecretRedactor.Redact(formatter(s, e)));
    }
}
