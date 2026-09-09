namespace OmnisRouter.LocalProxy;

/// <summary>
/// The tray's in-memory view of the supervised router process. Not persisted (data-model.md).
/// Off -&gt; Starting (enable) -&gt; Running (readiness passes) or Error (timeout / port in use).
/// Running -&gt; Starting (crash, auto-restart) -&gt; Running/Error. Any state -&gt; Off (disable / quit).
/// </summary>
public enum RouterProcessState
{
    Off,
    Starting,
    Running,
    Error,
}

/// <summary>Current supervision status, broadcast to the tray UI on every transition.</summary>
public sealed record RouterStatus(RouterProcessState State, int Port, string? Message = null);
