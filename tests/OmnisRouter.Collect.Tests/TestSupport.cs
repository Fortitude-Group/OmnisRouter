using System.Diagnostics;
using System.Text.Json.Nodes;
using OmnisRouter.Collect;

namespace OmnisRouter.Collect.Tests;

internal static class Wait
{
    /// <summary>Poll until <paramref name="condition"/> holds, or throw after the timeout.</summary>
    public static async Task Until(Func<bool> condition, int timeoutMs = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
            {
                throw new TimeoutException("condition not met in time");
            }

            await Task.Delay(5);
        }
    }
}

/// <summary>
/// Clock for tests: <see cref="UtcNow"/> tracks real time so the watch loop's file-mtime check
/// works against freshly written fixtures, while <see cref="Local"/> is settable so the "today"
/// rollover can be driven deterministically.
/// </summary>
internal sealed class FakeClock : IClock
{
    public DateTimeOffset Local { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateTimeOffset LocalNow => Local;
}

/// <summary>An in-memory sink that records every batch and can be told to fail its first N calls.</summary>
internal sealed class FakeReceiptSink : IReceiptSink
{
    private readonly object _lock = new();

    public int Calls { get; private set; }

    /// <summary>Throw while <see cref="Calls"/> is at or below this (0 = never fail).</summary>
    public int FailUntilCall { get; set; }

    /// <summary>All ids across all SUCCESSFUL posts, in order.</summary>
    public List<string> PostedIds { get; } = new();

    /// <summary>Invoked after each post attempt with the running call count; may cancel or mutate.</summary>
    public Action<int>? OnPost { get; set; }

    public Task<PostResult> PostAsync(IReadOnlyList<JsonObject> batch, CancellationToken ct)
    {
        lock (_lock)
        {
            Calls++;
            var call = Calls;
            if (call <= FailUntilCall)
            {
                OnPost?.Invoke(call);
                throw new InvalidOperationException($"boom on call {call}");
            }

            foreach (var r in batch)
            {
                PostedIds.Add(r["id"]!.GetValue<string>());
            }

            OnPost?.Invoke(call);
            return Task.FromResult(new PostResult(batch.Count, 0));
        }
    }
}

/// <summary>Writes Claude Code transcript fixtures into a throwaway directory.</summary>
internal sealed class TranscriptDir : IDisposable
{
    public TranscriptDir()
    {
        Root = Path.Combine(Path.GetTempPath(), "omnis-collect-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    /// <summary>Append one assistant usage entry to a transcript file (created if absent).</summary>
    public void Write(string file, string id, string model = "claude-sonnet-4-6",
        long input = 100, long output = 50, long cacheRead = 10, long cacheCreate = 5,
        string timestamp = "2026-09-05T10:00:00Z")
    {
        var line = new JsonObject
        {
            ["type"] = "assistant",
            ["timestamp"] = timestamp,
            ["sessionId"] = "s1",
            ["cwd"] = "/tmp/proj",
            ["gitBranch"] = "main",
            ["requestId"] = "req-" + id,
            ["message"] = new JsonObject
            {
                ["id"] = id,
                ["model"] = model,
                ["usage"] = new JsonObject
                {
                    ["input_tokens"] = input,
                    ["output_tokens"] = output,
                    ["cache_read_input_tokens"] = cacheRead,
                    ["cache_creation_input_tokens"] = cacheCreate,
                },
            },
        }.ToJsonString();

        var path = Path.Combine(Root, file);
        File.AppendAllText(path, line + "\n");
        // Make sure the watch loop's mtime check sees it as recently written.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
    }

    public void WriteRaw(string file, string content)
        => File.WriteAllText(Path.Combine(Root, file), content);

    /// <summary>Write several entries to one file in a single operation (no partial-read race).</summary>
    public void WriteMany(string file, params string[] ids)
    {
        var lines = ids.Select(id => new JsonObject
        {
            ["type"] = "assistant",
            ["timestamp"] = "2026-09-05T10:00:00Z",
            ["message"] = new JsonObject
            {
                ["id"] = id,
                ["model"] = "claude-sonnet-4-6",
                ["usage"] = new JsonObject { ["input_tokens"] = 100, ["output_tokens"] = 50 },
            },
        }.ToJsonString());

        var path = Path.Combine(Root, file);
        File.WriteAllText(path, string.Join('\n', lines) + "\n");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
