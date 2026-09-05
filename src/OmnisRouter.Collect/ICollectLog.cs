namespace OmnisRouter.Collect;

/// <summary>
/// Diagnostic sink for the engine. The CLI uses <see cref="NullCollectLog"/> (its diagnostics are
/// the console output). The tray uses <c>RollingFileLog</c> (US4) so its diagnostics survive without
/// a console. Kept tiny to avoid a logging-framework dependency for one bounded file.
/// </summary>
public interface ICollectLog
{
    void Info(string message);

    void Error(string message);
}

/// <summary>Discards everything. Default for the CLI, whose diagnostics are the console output.</summary>
public sealed class NullCollectLog : ICollectLog
{
    public static readonly NullCollectLog Instance = new();

    public void Info(string message)
    {
    }

    public void Error(string message)
    {
    }
}
