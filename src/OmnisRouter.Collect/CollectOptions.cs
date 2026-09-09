using System.Globalization;

namespace OmnisRouter.Collect;

/// <summary>Engine-only options: what to read and how to loop. No endpoint/key (that is the sink's job).
/// <paramref name="ExcludedClients"/> is a live set of source names (see <see cref="CollectSource"/>)
/// the tray mutates as clients connect/disconnect from the local router proxy; the engine consults it
/// each backfill and tick, so an excluded source is not tailed and double-counted (US4). Null or empty
/// means collect everything, which is the CLI default.</summary>
public sealed record CollectEngineOptions(
    string Root, DateTimeOffset? Since, int Batch, bool Watch, int Interval,
    IReadOnlySet<string>? ExcludedClients = null);

/// <summary>
/// The <c>omnisrouter collect</c> command line. Parsing and the usage text live here (moved verbatim
/// from the old <c>TranscriptCollector</c>) so the CLI contract is one shared, testable thing.
/// Parsing never touches the console: it returns an error string for the caller to render.
/// </summary>
public sealed record CollectOptions(
    string Root, string? Url, string? Key, DateTimeOffset? Since,
    int Batch, bool Watch, int Interval, bool DryRun)
{
    public CollectEngineOptions ToEngineOptions() => new(Root, Since, Batch, Watch, Interval);

    /// <summary>Parse argv. Returns the options, or an error message when an argument is unknown/malformed.</summary>
    public static (CollectOptions? Options, string? Error) Parse(string[] args)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        string? url = null;
        string? key = null;
        DateTimeOffset? since = DateTimeOffset.UtcNow.AddDays(-90);
        var batch = 1000;
        var watch = false;
        var dryRun = false;
        var interval = 15;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--root" when i + 1 < args.Length: root = args[++i]; break;
                case "--url" when i + 1 < args.Length: url = args[++i].TrimEnd('/'); break;
                case "--key" when i + 1 < args.Length: key = args[++i]; break;
                case "--batch" when i + 1 < args.Length && int.TryParse(args[i + 1], out var b): batch = Math.Max(1, b); i++; break;
                case "--interval" when i + 1 < args.Length && int.TryParse(args[i + 1], out var n): interval = Math.Max(2, n); i++; break;
                case "--all": since = null; break;
                case "--watch": watch = true; break;
                case "--dry-run": dryRun = true; break;
                case "--since" when i + 1 < args.Length
                    && DateTimeOffset.TryParse(args[i + 1], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var d):
                    since = d; i++; break;
                default:
                    return (null, $"Unknown or malformed argument: {args[i]}");
            }
        }

        return (new CollectOptions(root, url, key, since, batch, watch, interval, dryRun), null);
    }

    public const string Usage = """
        Usage: omnisrouter collect [options]
          Observe a Claude subscription by reading Claude Code's local transcripts and posting
          content-free usage receipts to OmnisVigil. Endpoint and key come from --url/--key or
          the OmnisVigil config section.
          --url <u>       OmnisVigil base URL
          --key <k>       OmnisVigil project key
          --root <dir>    Transcript root (default ~/.claude/projects)
          --since <date>  Only entries on/after this date (default: last 90 days)
          --all           Backfill the entire transcript history
          --watch         After the backfill, keep running and post new usage as it appears
          --interval <s>  Watch poll interval in seconds (default 15)
          --batch <n>     Records per ingest post (default 1000)
          --dry-run       Read and summarise only; post nothing (no key required)
        """;
}
