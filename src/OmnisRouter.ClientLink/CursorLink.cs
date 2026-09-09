namespace OmnisRouter.ClientLink;

/// <summary>
/// Wires Cursor to the local router. Cursor has no safe scriptable config file, so this link is
/// show-only: <see cref="Connect"/> writes nothing and instead returns the values for the user to
/// paste into Cursor Settings -> Models (contracts/client-link.md "Cursor").
/// </summary>
public sealed class CursorLink(string? homeDirectory = null) : IClientLink
{
    private readonly string _home =
        homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <inheritdoc />
    public ClientKind Kind => ClientKind.Cursor;

    /// <inheritdoc />
    public string? ConfigPath => null;

    /// <inheritdoc />
    public bool IsInstalled => Directory.Exists(Path.Combine(_home, ".cursor"));

    /// <inheritdoc />
    public ClientLinkResult Connect(string root, string token, string? currentContent) =>
        new()
        {
            NewFileContent = null,
            PriorState = new ClientPriorState(),
            DisplayValues = new Dictionary<string, string>
            {
                ["OPENAI_BASE_URL"] = root + "/v1",
                ["OPENAI_API_KEY"] = token,
            },
        };

    /// <inheritdoc />
    public string? Revert(string? currentContent, ClientPriorState priorState) => null;
}
