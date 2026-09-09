namespace OmnisRouter.ClientLink;

/// <summary>
/// The catalogue of client links the tray can offer, and best-effort detection of which are
/// installed for the current user (T033). Detection is each link's own <see cref="IClientLink.IsInstalled"/>
/// check (a config file/directory present); the Connect window shows every client but highlights the
/// detected ones.
/// </summary>
public static class ClientDetection
{
    /// <summary>Every supported client link, in display order. <paramref name="homeDirectory"/>
    /// overrides the user profile root (used by tests); null resolves to the real profile.</summary>
    public static IReadOnlyList<IClientLink> All(string? homeDirectory = null) =>
    [
        new ClaudeCodeLink(homeDirectory),
        new CodexLink(homeDirectory),
        new CursorLink(homeDirectory),
    ];

    /// <summary>The subset of <see cref="All"/> that appears installed for the current user.</summary>
    public static IReadOnlyList<IClientLink> Installed(string? homeDirectory = null) =>
        [.. All(homeDirectory).Where(link => link.IsInstalled)];
}
