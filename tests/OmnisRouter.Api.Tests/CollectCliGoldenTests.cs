using System.Text.Json.Nodes;
using OmnisRouter.Api.Collect;

namespace OmnisRouter.Api.Tests;

/// <summary>
/// Golden test for the collect CLI (FR-020): the engine extraction must not change the command's
/// output. Runs a small fixture through `collect --dry-run --all` and asserts the header, progress
/// line, and summary the old <c>TranscriptCollector</c> produced.
/// </summary>
public class CollectCliGoldenTests
{
    private static string WriteFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "omnis-cli-golden", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        string Line(string id) => new JsonObject
        {
            ["type"] = "assistant",
            ["timestamp"] = "2026-09-05T10:00:00Z",
            ["message"] = new JsonObject
            {
                ["id"] = id,
                ["model"] = "claude-sonnet-4-6",
                ["usage"] = new JsonObject { ["input_tokens"] = 100, ["output_tokens"] = 50 },
            },
        }.ToJsonString();
        File.WriteAllText(Path.Combine(root, "t.jsonl"), Line("m1") + "\n" + Line("m2") + "\n");
        return root;
    }

    [Fact]
    public async Task Dry_run_prints_the_expected_header_progress_and_summary()
    {
        var root = WriteFixture();
        var original = Console.Out;
        var buffer = new StringWriter();
        int rc;
        try
        {
            Console.SetOut(buffer);
            rc = await ConsoleCollectRunner.RunAsync(new[] { "--dry-run", "--all", "--root", root });
        }
        finally
        {
            Console.SetOut(original);
        }

        var output = buffer.ToString();

        Assert.Equal(0, rc);
        Assert.Contains("OmnisRouter collect (subscription observe mode)", output);
        Assert.Contains($"  transcripts : {root}", output);
        Assert.Contains("  target      : dry run (nothing posted)", output);
        Assert.Contains("  window      : all history", output);
        Assert.Contains("scanned 2  unique 2  posted 2  dup 0", output);
        Assert.Contains("  unique calls : 2", output);
        Assert.Contains("  would post   : 2  (dry run, nothing sent)", output);
    }

    [Fact]
    public async Task Unknown_argument_is_rejected_with_usage()
    {
        var original = Console.Error;
        var buffer = new StringWriter();
        int rc;
        try
        {
            Console.SetError(buffer);
            rc = await ConsoleCollectRunner.RunAsync(new[] { "--nonsense" });
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Equal(1, rc);
        Assert.Contains("Unknown or malformed argument: --nonsense", buffer.ToString());
        Assert.Contains("Usage: omnisrouter collect", buffer.ToString());
    }
}
