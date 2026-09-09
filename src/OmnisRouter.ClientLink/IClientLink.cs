namespace OmnisRouter.ClientLink;

/// <summary>The coding tools the tray can wire to the local router (data-model.md).</summary>
public enum ClientKind
{
    ClaudeCode,
    Codex,
    Cursor,
}

/// <summary>
/// Pre-connect state captured at connect time so a later revert can restore the file exactly
/// (contracts/client-link.md). Serialized into <c>router.json</c> alongside the connected client.
/// </summary>
public sealed record ClientPriorState
{
    /// <summary>
    /// Prior values of the settings a connect touches, keyed by name. A key present here with a
    /// <c>null</c> value means it was <em>absent</em> before connect, so revert removes it; a
    /// non-null value is what revert restores. Claude Code records its two <c>env</c> keys here;
    /// Codex records the prior <c>OMNISROUTER_API_KEY</c> environment value here.
    /// </summary>
    public IReadOnlyDictionary<string, string?> PriorValues { get; init; }
        = new Dictionary<string, string?>();

    /// <summary>
    /// Codex only: the text of a managed block that already existed before connect (so revert can
    /// restore it verbatim), or <c>null</c> when no managed block was present.
    /// </summary>
    public string? PriorManagedBlock { get; init; }
}

/// <summary>
/// The outcome of a pure <see cref="IClientLink.Connect"/> transform: the new file content to
/// persist, the prior state to keep for revert, and any non-file side effects the executor must
/// apply (environment variables to set) or surface (show-only display values).
/// </summary>
public sealed record ClientLinkResult
{
    /// <summary>The new config-file content to write, or <c>null</c> for show-only clients
    /// (Cursor) that have no scriptable config file.</summary>
    public string? NewFileContent { get; init; }

    /// <summary>The prior state to persist so the connect can be reverted exactly.</summary>
    public required ClientPriorState PriorState { get; init; }

    /// <summary>User-scoped environment variables the executor must set (name → value). Codex sets
    /// <c>OMNISROUTER_API_KEY</c>; the others are empty.</summary>
    public IReadOnlyDictionary<string, string> EnvironmentVariablesToSet { get; init; }
        = new Dictionary<string, string>();

    /// <summary>Values to display to the user to apply by hand (name → value). Used by show-only
    /// clients (Cursor); empty for clients written to disk.</summary>
    public IReadOnlyDictionary<string, string> DisplayValues { get; init; }
        = new Dictionary<string, string>();
}

/// <summary>Thrown when a connect or revert cannot proceed safely — e.g. the existing config file
/// is not valid JSON, or a managed block cannot be located to replace. The tray surfaces the
/// message and writes nothing (contracts/client-link.md "Refusal").</summary>
public sealed class ClientLinkException(string message) : Exception(message);

/// <summary>
/// Wires one coding tool to the local router and reverts it exactly. <see cref="Connect"/> and
/// <see cref="Revert"/> are pure and deterministic given <c>(root, token, currentContent)</c>
/// (contracts/client-link.md), so they are golden-tested; the tray owns the disk I/O, timestamped
/// backup and environment-variable writes around them.
/// </summary>
public interface IClientLink
{
    /// <summary>Which client this link wires.</summary>
    ClientKind Kind { get; }

    /// <summary>The absolute config-file path this client writes, or <c>null</c> for show-only
    /// clients (Cursor). Used for detection and for the executor's read/write/backup.</summary>
    string? ConfigPath { get; }

    /// <summary>Whether this client appears installed for the current user (its config directory
    /// or file is present). Best-effort; drives which entries the Connect window offers.</summary>
    bool IsInstalled { get; }

    /// <summary>
    /// Pure transform: given the router <paramref name="root"/> (e.g. <c>http://127.0.0.1:8787</c>),
    /// the router <paramref name="token"/>, and the <paramref name="currentContent"/> of the config
    /// file (<c>null</c> when it does not exist), produce the connected content, the prior state to
    /// keep for revert, and any environment-variable or display side effects. Idempotent: connecting
    /// an already-connected client reproduces the same result without stacking duplicates.
    /// </summary>
    /// <exception cref="ClientLinkException">The existing content cannot be parsed safely.</exception>
    ClientLinkResult Connect(string root, string token, string? currentContent);

    /// <summary>
    /// Pure transform: given the <paramref name="currentContent"/> of the config file and the
    /// <paramref name="priorState"/> captured at connect, produce the reverted content — restoring
    /// prior values, removing keys that were absent, and leaving every other part of the file
    /// untouched. Returns <c>null</c> for show-only clients (Cursor), which have no file to change.
    /// </summary>
    string? Revert(string? currentContent, ClientPriorState priorState);
}
