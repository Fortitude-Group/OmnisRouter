using System.Globalization;

namespace OmnisRouter.Collect;

/// <summary>
/// Turns a <see cref="CollectionStatus"/> into the short strings the tray shows. Lives here (not in
/// the WinForms project) so the wording is unit-tested without a UI (T034). Every figure shown is a
/// liveness figure; the spend/savings numbers stay in the dashboard (constitution XII).
/// </summary>
public static class StatusFormat
{
    private const string Name = "OmnisRouter";

    /// <summary>The tray tooltip. Kept short (the OS caps it).</summary>
    public static string Tooltip(CollectionStatus s) => s.State switch
    {
        CollectState.Paused => $"{Name} · paused",
        CollectState.Error => $"{Name} · can't reach OmnisVigil · retrying",
        CollectState.Backfilling => $"{Name} · catching up · {N(s.TodayReceipts)} today",
        _ when s.LastPostUtc is null => $"{Name} · watching · {N(s.TodayReceipts)} today",
        _ => $"{Name} · last posted {LocalTime(s.LastPostUtc, "HH:mm")} · {N(s.TodayReceipts)} today",
    };

    /// <summary>The popup's first line: what the engine is doing right now.</summary>
    public static string StateLine(CollectionStatus s) => s.State switch
    {
        CollectState.Idle => "Idle",
        CollectState.Backfilling => "Backfilling…",
        CollectState.Watching => "Watching",
        CollectState.Paused => "Paused",
        CollectState.Error => $"Error: {s.LastError}",
        CollectState.Stopped => "Stopped",
        _ => s.State.ToString(),
    };

    public static string LastPostedLine(CollectionStatus s) =>
        s.LastPostUtc is null ? "Last posted: not yet" : $"Last posted: {LocalTime(s.LastPostUtc, "HH:mm:ss")}";

    public static string TodayLine(CollectionStatus s) => $"Today: {N(s.TodayReceipts)} receipts";

    private static string N(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string LocalTime(DateTimeOffset? utc, string format) =>
        utc!.Value.ToLocalTime().ToString(format, CultureInfo.CurrentCulture);
}
