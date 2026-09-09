namespace OmnisRouter.ClientLink;

/// <summary>
/// One coding tool the tray has wired to the local router (data-model.md). Persisted in
/// <c>router.json</c> and used both to revert the client exactly and as the collector's exclusion
/// set (a connected client is not double-counted; see US4). Everything needed for an exact revert is
/// captured here at connect time.
/// </summary>
/// <param name="Kind">Which client this is.</param>
/// <param name="ConnectedAt">When it was connected.</param>
/// <param name="PriorState">The link's captured file state, for restoring the config file on revert.</param>
/// <param name="BackupPath">Path to the timestamped backup written before editing, or <c>null</c>
/// for show-only clients (Cursor) or when no file existed to back up.</param>
/// <param name="PriorEnvironmentValues">User environment variables the connect set, mapped to their
/// value before connect. A <c>null</c> value means the variable was unset before, so revert removes
/// it; a non-null value is restored on revert.</param>
public sealed record ConnectedClient(
    ClientKind Kind,
    DateTimeOffset ConnectedAt,
    ClientPriorState PriorState,
    string? BackupPath,
    IReadOnlyDictionary<string, string?> PriorEnvironmentValues);
