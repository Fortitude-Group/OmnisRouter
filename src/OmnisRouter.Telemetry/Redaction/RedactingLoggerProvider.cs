using Microsoft.Extensions.Logging;

namespace OmnisRouter.Telemetry.Redaction;

/// <summary>
/// <see cref="ILoggerProvider"/> decorator that wraps every <see cref="ILogger"/> it creates in a
/// <see cref="RedactingLogger"/>, so log lines written through the wrapped provider's sink (console,
/// OTLP, file, etc.) are scrubbed of anything shaped like a secret before they leave the process.
/// </summary>
public sealed class RedactingLoggerProvider : ILoggerProvider
{
    private readonly ILoggerProvider _inner;

    public RedactingLoggerProvider(ILoggerProvider inner)
    {
        _inner = inner;
    }

    public ILogger CreateLogger(string categoryName) => new RedactingLogger(_inner.CreateLogger(categoryName));

    public void Dispose() => _inner.Dispose();
}
