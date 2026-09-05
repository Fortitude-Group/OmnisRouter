using Microsoft.Extensions.Configuration;
using OmnisRouter.Collect;
using OmnisRouter.Vigil;

namespace OmnisRouter.Api.Collect;

/// <summary>
/// The <c>collect</c> command's console face. It resolves the OmnisVigil endpoint/key, builds the
/// receipt sink, drives <see cref="CollectEngine"/>, and turns the engine's events into exactly the
/// output the old <c>TranscriptCollector</c> produced (FR-020). All collection logic now lives in
/// the shared engine; this class only renders.
/// </summary>
internal static class ConsoleCollectRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        var (opts, parseError) = CollectOptions.Parse(args);
        if (opts is null)
        {
            Console.Error.WriteLine(parseError);
            Console.Error.WriteLine(CollectOptions.Usage);
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

        var target = opts.DryRun ? "dry run (nothing posted)" : $"{endpoint}/v1/ingest";
        Console.WriteLine("OmnisRouter collect (subscription observe mode)");
        Console.WriteLine($"  transcripts : {opts.Root}");
        Console.WriteLine($"  target      : {target}");
        Console.WriteLine($"  window      : {(opts.Since is { } s ? $"since {s:yyyy-MM-dd}" : "all history")}");
        Console.WriteLine();

        using var sink = opts.DryRun ? null : new HttpReceiptSink(endpoint!, key!);
        IReceiptSink receiptSink = sink ?? (IReceiptSink)new NullReceiptSink();

        var engine = new CollectEngine(opts.ToEngineOptions(), receiptSink, endpoint ?? "");
        engine.Emitted += e => Render(e, opts);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        await engine.RunAsync(cts.Token).ConfigureAwait(false);
        return 0;
    }

    private static void Render(CollectEvent e, CollectOptions opts)
    {
        var st = e.Status;
        switch (e.Signal)
        {
            case CollectSignal.BackfillProgress:
                Console.Write($"\r  scanned {st.Scanned:N0}  unique {st.Unique:N0}  posted {st.SessionReceipts:N0}  dup {st.Duplicates:N0}   ");
                break;

            case CollectSignal.BackfillComplete:
                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine($"  unique calls : {st.Unique:N0}");
                Console.WriteLine(opts.DryRun
                    ? $"  would post   : {st.SessionReceipts:N0}  (dry run, nothing sent)"
                    : $"  new receipts : {st.SessionReceipts:N0}   already present : {st.Duplicates:N0}");
                break;

            case CollectSignal.WatchStarted:
                Console.WriteLine();
                Console.WriteLine($"  watching for new usage every {opts.Interval}s (Ctrl+C to stop)...");
                break;

            case CollectSignal.WatchTick:
                Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] +{e.Added:N0} new receipt(s)   (session total {st.SessionReceipts:N0})");
                break;

            case CollectSignal.TickFailed:
                Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] tick failed, will retry: {e.Error}");
                break;

            case CollectSignal.Stopped:
                Console.WriteLine("  stopped watching.");
                break;

            case CollectSignal.Paused:
            case CollectSignal.Resumed:
                break;   // the CLI has no pause; these never fire from it
        }
    }
}
