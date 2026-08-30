using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using OmnisRouter.Vigil;

namespace OmnisRouter.Api.Collect;

/// <summary>
/// The <c>collect</c> command: the router's observe mode for flat-rate subscriptions. It reads
/// Claude Code's local transcripts (subagents included), prices the tokens at API list rates, and
/// posts the same content-free <c>/v1/ingest</c> receipts the routed path posts, using the same
/// OmnisVigil endpoint and project key. Idempotent by the entry's message id, so it backfills
/// history and, with --watch, tails for new usage without ever double-counting.
/// </summary>
internal static class TranscriptCollector
{
    public static async Task<int> RunAsync(string[] args)
    {
        var opts = CollectOptions.Parse(args);
        if (opts is null)
        {
            CollectOptions.PrintUsage();
            return 1;
        }

        // Endpoint and key come from --url/--key, else the router's own OmnisVigil config section.
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var vigil = config.GetSection(OmnisVigilOptions.SectionName).Get<OmnisVigilOptions>() ?? new OmnisVigilOptions();

        var endpoint = (opts.Url ?? vigil.Endpoint)?.TrimEnd('/');
        var key = opts.Key ?? vigil.ProjectKey;
        if (!opts.DryRun && (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key)))
        {
            Console.Error.WriteLine("An OmnisVigil endpoint and project key are required. Pass --url and --key, or set the OmnisVigil section (Endpoint, ProjectKey).");
            return 1;
        }

        if (!Directory.Exists(opts.Root))
        {
            Console.Error.WriteLine($"Transcript directory not found: {opts.Root}");
            return 1;
        }

        Console.WriteLine("OmnisRouter collect (subscription observe mode)");
        Console.WriteLine($"  transcripts : {opts.Root}");
        Console.WriteLine($"  target      : {(opts.DryRun ? "dry run (nothing posted)" : $"{endpoint}/v1/ingest")}");
        Console.WriteLine($"  window      : {(opts.Since is { } s ? $"since {s:yyyy-MM-dd}" : "all history")}");
        Console.WriteLine();

        // In dry-run mode nothing is posted, so there is no client and no key requirement.
        using var http = opts.DryRun
            ? null
            : new HttpClient { BaseAddress = new Uri(endpoint!), Timeout = TimeSpan.FromMinutes(2) };
        if (http is not null)
        {
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var batch = new List<JsonObject>(opts.Batch);
        long scanned = 0, unique = 0, accepted = 0, duplicates = 0;
        var quiet = false;

        async Task FlushAsync()
        {
            if (batch.Count == 0)
            {
                return;
            }

            if (http is null)
            {
                accepted += batch.Count;   // dry run: count what would post, send nothing
            }
            else
            {
                var (a, d) = await PostBatchAsync(http, batch).ConfigureAwait(false);
                accepted += a;
                duplicates += d;
            }

            batch.Clear();
            if (!quiet)
            {
                Console.Write($"\r  scanned {scanned:N0}  unique {unique:N0}  posted {accepted:N0}  dup {duplicates:N0}   ");
            }
        }

        async Task ProcessAsync(IEnumerable<UsageEntry> entries)
        {
            foreach (var e in entries)
            {
                scanned++;
                if (!seen.Add(e.Id))
                {
                    continue;
                }

                unique++;
                var cost = ModelPrices.CostUsd(e.Model, e.InputTokens, e.OutputTokens, e.CacheReadTokens, e.CacheCreationTokens);
                batch.Add(ToRecord(e, cost));
                if (batch.Count >= opts.Batch)
                {
                    await FlushAsync().ConfigureAwait(false);
                }
            }
        }

        await ProcessAsync(TranscriptReader.Read(opts.Root, opts.Since)).ConfigureAwait(false);
        await FlushAsync().ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine($"  unique calls : {unique:N0}");
        Console.WriteLine(opts.DryRun
            ? $"  would post   : {accepted:N0}  (dry run, nothing sent)"
            : $"  new receipts : {accepted:N0}   already present : {duplicates:N0}");

        if (opts.Watch && !opts.DryRun)
        {
            quiet = true;
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            Console.WriteLine();
            Console.WriteLine($"  watching for new usage every {opts.Interval}s (Ctrl+C to stop)...");

            var lastScan = DateTime.UtcNow.AddSeconds(-opts.Interval);
            while (!cts.IsCancellationRequested)
            {
                var tickStart = DateTime.UtcNow;
                var before = accepted;
                try
                {
                    foreach (var file in TranscriptReader.EnumerateFiles(opts.Root))
                    {
                        if (SafeLastWrite(file) >= lastScan.AddSeconds(-5))
                        {
                            await ProcessAsync(TranscriptReader.ReadFile(file, opts.Since)).ConfigureAwait(false);
                        }
                    }

                    await FlushAsync().ConfigureAwait(false);
                    lastScan = tickStart;

                    var added = accepted - before;
                    if (added > 0)
                    {
                        Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] +{added:N0} new receipt(s)   (session total {accepted:N0})");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    foreach (var rec in batch)
                    {
                        var id = rec["id"]?.GetValue<string>();
                        if (id is not null)
                        {
                            seen.Remove(id);
                        }
                    }

                    batch.Clear();
                    Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] tick failed, will retry: {ex.Message.Split('\n')[0]}");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(opts.Interval), cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            Console.WriteLine("  stopped watching.");
        }

        return 0;
    }

    // Build the content-free ingest record, matching the routed path's wire shape. No routing
    // happened, so there is no realised saving; the model-mix saving is derived by OmnisVigil.
    private static JsonObject ToRecord(UsageEntry e, double cost) => new()
    {
        ["id"] = e.Id,
        ["timestamp"] = e.Timestamp.ToString("O", CultureInfo.InvariantCulture),
        ["tenant_id"] = "local",
        ["router_id"] = "claude-code-local",
        ["session_id"] = e.SessionId,
        ["request_hash"] = e.RequestId ?? e.Id,
        ["client_format"] = "anthropic",
        ["cluster_id"] = 0,
        ["chosen_provider"] = "anthropic",
        ["chosen_model_id"] = e.Model,
        ["confidence"] = 1.0,
        ["top1_sim"] = null,
        ["top2_sim"] = null,
        ["margin"] = null,
        ["decision"] = "DIRECT",
        ["reason"] = "claude-code-transcript",
        ["policy_version"] = "collector",
        ["est_cost_usd"] = cost,
        ["est_cost_delta_vs_big_usd"] = 0.0,
        ["actual_cost_usd"] = cost,
        ["actual_cost_delta_vs_big_usd"] = 0.0,
        ["usage"] = new JsonObject
        {
            ["input_tokens"] = ToInt(e.InputTokens),
            ["output_tokens"] = ToInt(e.OutputTokens),
            ["cache_creation_tokens"] = ToInt(e.CacheCreationTokens),
            ["cache_read_tokens"] = ToInt(e.CacheReadTokens),
        },
        ["session_pin_applied"] = false,
        ["outcome"] = "success",
        ["latency_ms"] = 0,
        ["tags"] = new JsonObject
        {
            ["project"] = e.Project,
            ["team"] = null,
            ["client_name"] = "claude-code",
            ["commit"] = null,
            ["branch"] = e.Branch,
        },
    };

    private static int ToInt(long v) => v > int.MaxValue ? int.MaxValue : (int)v;

    private static DateTime SafeLastWrite(string file)
    {
        try
        {
            return File.GetLastWriteTimeUtc(file);
        }
        catch (IOException)
        {
            return DateTime.MinValue;
        }
    }

    private static async Task<(int Accepted, int Duplicates)> PostBatchAsync(HttpClient client, List<JsonObject> records)
    {
        var array = new JsonArray();
        foreach (var r in records)
        {
            array.Add(r);   // each record is built fresh per batch and never reused, so no clone
        }

        var body = new JsonObject { ["schema_version"] = 1, ["records"] = array };
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/v1/ingest", content).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Ingest failed ({(int)response.StatusCode}): {text}");
        }

        using var doc = JsonDocument.Parse(text);
        return (ReadInt(doc.RootElement, "accepted"), ReadInt(doc.RootElement, "duplicates"));

        static int ReadInt(JsonElement el, string name)
        {
            foreach (var prop in el.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase) && prop.Value.TryGetInt32(out var n))
                {
                    return n;
                }
            }

            return 0;
        }
    }

    private sealed record CollectOptions(string Root, string? Url, string? Key, DateTimeOffset? Since, int Batch, bool Watch, int Interval, bool DryRun)
    {
        public static CollectOptions? Parse(string[] args)
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
                        Console.Error.WriteLine($"Unknown or malformed argument: {args[i]}");
                        return null;
                }
            }

            return new CollectOptions(root, url, key, since, batch, watch, interval, dryRun);
        }

        public static void PrintUsage()
        {
            Console.Error.WriteLine("""
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
            """);
        }
    }
}
