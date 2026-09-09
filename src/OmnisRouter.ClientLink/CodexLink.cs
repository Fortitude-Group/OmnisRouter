namespace OmnisRouter.ClientLink;

/// <summary>
/// Wires the Codex CLI to the local router by managing a delimited block inside
/// <c>~/.codex/config.toml</c>, plus a user-scoped <c>OMNISROUTER_API_KEY</c> environment variable
/// set by the executor (contracts/client-link.md "Codex"). The connect/revert transforms here are
/// pure file-content edits — no disk or environment access — mirroring the semantics of the Node
/// reference implementation (<c>installer/index.js</c> <c>writeCodexConfig</c>), plus the revert it
/// lacks.
/// </summary>
public sealed class CodexLink(string? homeDirectory = null) : IClientLink
{
    private const string StartMarker = "# >>> omnisrouter-cli managed block >>>";
    private const string EndMarker = "# <<< omnisrouter-cli managed block <<<";

    private readonly string _home =
        homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public ClientKind Kind => ClientKind.Codex;

    public string? ConfigPath => Path.Combine(_home, ".codex", "config.toml");

    public bool IsInstalled => File.Exists(ConfigPath);

    public ClientLinkResult Connect(string root, string token, string? currentContent)
    {
        var block = BuildBlock(root);
        string newContent;
        string? priorManagedBlock = null;

        if (string.IsNullOrEmpty(currentContent))
        {
            newContent = block;
        }
        else
        {
            var startIdx = currentContent.IndexOf(StartMarker, StringComparison.Ordinal);
            var endIdx = currentContent.IndexOf(EndMarker, StringComparison.Ordinal);

            if ((startIdx >= 0) != (endIdx >= 0))
            {
                throw new ClientLinkException(
                    "Codex config.toml contains only one of the omnisrouter-cli managed block " +
                    "markers; refusing to edit it.");
            }

            if (startIdx >= 0)
            {
                var markerEnd = endIdx + EndMarker.Length;
                priorManagedBlock = currentContent[startIdx..markerEnd];

                var blockEnd = markerEnd;
                if (blockEnd < currentContent.Length && currentContent[blockEnd] == '\n')
                {
                    blockEnd++;
                }

                newContent = currentContent[..startIdx] + block + currentContent[blockEnd..];
            }
            else
            {
                // Mirrors the Node reference's `existing.replace(/\s*$/, '\n')`: collapse any
                // trailing whitespace down to exactly one newline before appending.
                var trimmed = currentContent.TrimEnd() + "\n";
                newContent = trimmed + "\n" + block;
            }
        }

        return new ClientLinkResult
        {
            NewFileContent = newContent,
            PriorState = new ClientPriorState { PriorManagedBlock = priorManagedBlock },
            EnvironmentVariablesToSet = new Dictionary<string, string>
            {
                ["OMNISROUTER_API_KEY"] = token,
            },
        };
    }

    public string? Revert(string? currentContent, ClientPriorState priorState)
    {
        if (string.IsNullOrEmpty(currentContent))
        {
            return currentContent;
        }

        var startIdx = currentContent.IndexOf(StartMarker, StringComparison.Ordinal);
        var endIdx = currentContent.IndexOf(EndMarker, StringComparison.Ordinal);
        if (startIdx < 0 || endIdx < 0)
        {
            return currentContent;
        }

        var blockEnd = endIdx + EndMarker.Length;
        if (blockEnd < currentContent.Length && currentContent[blockEnd] == '\n')
        {
            blockEnd++;
        }

        var before = currentContent[..startIdx];
        var after = currentContent[blockEnd..];

        if (priorState.PriorManagedBlock is { } priorBlock)
        {
            return before + priorBlock + "\n" + after;
        }

        if (before.EndsWith('\n'))
        {
            before = before[..^1];
        }

        return before + after;
    }

    private static string BuildBlock(string root) => string.Join(
        "\n",
        StartMarker,
        "# Added by omnisrouter-cli. Safe to edit or delete this block by hand.",
        "# Set model_provider = \"omnisrouter\" (top level, or per-profile) to actually route through it.",
        "[model_providers.omnisrouter]",
        "name = \"OmnisRouter\"",
        $"base_url = \"{root}/v1\"",
        "env_key = \"OMNISROUTER_API_KEY\"",
        "wire_api = \"chat\"",
        EndMarker,
        "");
}
