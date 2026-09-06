using System.Text.Json.Nodes;
using OmnisRouter.Collect;

namespace OmnisRouter.Collect.Tests;

public class CommitAttributionTests : IDisposable
{
    private const string Sha = "abcabcabcabcabcabcabcabcabcabcabcabcabca";
    private readonly string _base = Path.Combine(Path.GetTempPath(), "omnis-commit", Guid.NewGuid().ToString("n"));

    private string MakeRepo()
    {
        var repo = Path.Combine(_base, "repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git", "refs", "heads"));
        File.WriteAllText(Path.Combine(repo, ".git", "HEAD"), "ref: refs/heads/main\n");
        File.WriteAllText(Path.Combine(repo, ".git", "refs", "heads", "main"), Sha + "\n");
        return repo;
    }

    private static string? CommitOf(JsonObject record) => (string?)((JsonObject)record["tags"]!)["commit"];

    [Fact]
    public void Receipt_record_carries_the_commit_when_supplied()
    {
        var e = new UsageEntry("m1", DateTimeOffset.UnixEpoch, "claude-sonnet-4-6", 1, 1, 0, 0, null, "proj", "main", null, "/tmp/proj");
        Assert.Equal(Sha, CommitOf(ReceiptRecord.From(e, 0, Sha)));
        Assert.Null(CommitOf(ReceiptRecord.From(e, 0)));   // default: no commit
    }

    [Fact]
    public async Task Backfill_leaves_commit_null_even_inside_a_repo()
    {
        var repo = MakeRepo();
        using var dir = new TranscriptDir();
        dir.Write("a.jsonl", "m1", cwd: repo);

        var sink = new FakeReceiptSink();
        var engine = new CollectEngine(
            new CollectEngineOptions(dir.Root, null, 1000, Watch: false, Interval: 0),
            sink, "https://vigil.test", new FakeClock());

        await engine.RunAsync(CancellationToken.None);

        Assert.Null(CommitOf(Assert.Single(sink.PostedRecords)));
    }

    [Fact]
    public async Task Live_watch_stamps_the_current_head_commit()
    {
        var repo = MakeRepo();
        using var dir = new TranscriptDir();   // empty; the entry arrives while watching

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        var sink = new FakeReceiptSink();
        sink.OnPost = _ => cts.Cancel();

        var engine = new CollectEngine(
            new CollectEngineOptions(dir.Root, null, 1000, Watch: true, Interval: 0),
            sink, "https://vigil.test", new FakeClock());

        var run = engine.RunAsync(cts.Token);
        await Wait.Until(() => engine.Status.State is CollectState.Watching or CollectState.Error);
        dir.Write("live.jsonl", "m1", cwd: repo);
        await run;

        Assert.Equal(Sha, CommitOf(Assert.Single(sink.PostedRecords)));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_base, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
