namespace OmnisRouter.Collect;

/// <summary>
/// Time source for the engine, injected so the "today" rollover and timestamps are deterministic
/// under test. <see cref="LocalNow"/> drives the day boundary; <see cref="UtcNow"/> stamps events.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    DateTimeOffset LocalNow { get; }
}

/// <summary>The real clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateTimeOffset LocalNow => DateTimeOffset.Now;
}
