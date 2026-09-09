using OmnisRouter.Collect;
using OmnisRouter.LocalProxy;

namespace OmnisRouter.Tray;

/// <summary>
/// Names the active mode(s) in plain British English, so the collector's flat-rate reporting and the
/// router's per-token routing are never confused (FR-007, SC-003). Lives in the tray project (not
/// alongside <see cref="StatusFormat"/> in Collect) because it spans both features and Collect must
/// stay free of the LocalProxy dependency.
/// </summary>
internal static class RouterStatusText
{
    /// <summary>The popup's mode line(s): what is actually routing/reporting right now.
    /// <paramref name="hasProviderKeys"/> and <paramref name="connectedClients"/> keep the running
    /// state honest: a router with no key can't route, and one with no connected app isn't routing
    /// anything yet, so neither should claim to be.</summary>
    public static string ModeLine(CollectState collectState, RouterStatus router, bool hasProviderKeys, int connectedClients)
    {
        var routing = router.State switch
        {
            RouterProcessState.Running when !hasProviderKeys => "Local router is running, but has no provider key yet. Add one to route.",
            RouterProcessState.Running when connectedClients == 0 => "Local router is ready. Connect an app to route through your keys.",
            RouterProcessState.Running => "Routing your traffic through your own provider keys (per-token billing)",
            RouterProcessState.Starting => "Local router: starting…",
            RouterProcessState.Error => $"Local router error: {router.Message ?? "unknown"}",
            _ => null,
        };

        var reporting = collectState is CollectState.Watching or CollectState.Backfilling
            ? "Reporting your flat-rate usage to OmnisVigil"
            : null;

        return (routing, reporting) switch
        {
            (not null, not null) => $"{routing}\n{reporting}",
            (not null, null) => routing,
            (null, not null) => reporting,
            _ => "Not routing, not reporting usage",
        };
    }

    /// <summary>A short suffix for the tray tooltip (the OS caps its length).</summary>
    public static string TooltipSuffix(RouterProcessState state, bool hasProviderKeys, int connectedClients) => state switch
    {
        RouterProcessState.Running when !hasProviderKeys => " · router up, no key",
        RouterProcessState.Running when connectedClients == 0 => " · router ready",
        RouterProcessState.Running => " · routing live",
        RouterProcessState.Starting => " · router starting",
        RouterProcessState.Error => " · router error",
        _ => "",
    };
}
