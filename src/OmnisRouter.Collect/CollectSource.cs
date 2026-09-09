namespace OmnisRouter.Collect;

/// <summary>
/// Names of the usage sources the collector can tail, used as the identifiers in
/// <see cref="CollectEngineOptions.ExcludedClients"/>. Today the collector only reads Claude Code
/// transcripts, so <see cref="ClaudeCode"/> is the only source; excluding it (because Claude Code is
/// connected to the local router proxy) stops the collector double-counting routed traffic. The
/// values match <c>OmnisRouter.ClientLink.ClientKind</c> names so the tray can map one to the other
/// without this core library depending on the tray's client-link types.
/// </summary>
public static class CollectSource
{
    public const string ClaudeCode = "ClaudeCode";
}
