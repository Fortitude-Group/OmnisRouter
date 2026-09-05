using OmnisRouter.Collect;

namespace OmnisRouter.Collect.Tests;

public class TranscriptReaderTests
{
    [Fact]
    public void Reads_assistant_usage_entries()
    {
        using var dir = new TranscriptDir();
        dir.Write("a.jsonl", "m1", input: 100, output: 50, cacheRead: 10, cacheCreate: 5);

        var entries = TranscriptReader.Read(dir.Root, since: null).ToList();

        var e = Assert.Single(entries);
        Assert.Equal("m1", e.Id);
        Assert.Equal("claude-sonnet-4-6", e.Model);
        Assert.Equal(100, e.InputTokens);
        Assert.Equal(50, e.OutputTokens);
        Assert.Equal(10, e.CacheReadTokens);
        Assert.Equal(5, e.CacheCreationTokens);
    }

    [Fact]
    public void Recurses_into_subdirectories()
    {
        using var dir = new TranscriptDir();
        Directory.CreateDirectory(Path.Combine(dir.Root, "sub"));
        dir.Write("a.jsonl", "m1");
        dir.Write(Path.Combine("sub", "b.jsonl"), "m2");

        var ids = TranscriptReader.Read(dir.Root, since: null).Select(e => e.Id).OrderBy(x => x).ToList();

        Assert.Equal(new[] { "m1", "m2" }, ids);
    }

    [Fact]
    public void Honours_the_since_window()
    {
        using var dir = new TranscriptDir();
        dir.Write("a.jsonl", "old", timestamp: "2026-01-01T00:00:00Z");
        dir.Write("a.jsonl", "new", timestamp: "2026-09-05T09:00:00Z");

        var ids = TranscriptReader.Read(dir.Root, since: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero))
            .Select(e => e.Id).ToList();

        Assert.Equal(new[] { "new" }, ids);
    }

    [Fact]
    public void Skips_malformed_and_non_assistant_lines()
    {
        using var dir = new TranscriptDir();
        dir.WriteRaw("mixed.jsonl", string.Join('\n',
            "not json at all",
            "",
            "{\"type\":\"user\",\"message\":{\"id\":\"u1\"}}",                       // not assistant
            "{\"type\":\"assistant\",\"message\":{\"id\":\"noUsage\",\"model\":\"claude-sonnet-4-6\"}}", // no usage
            "{\"type\":\"assistant\",\"timestamp\":\"2026-09-05T10:00:00Z\",\"message\":{\"id\":\"m1\",\"model\":\"claude-sonnet-4-6\",\"usage\":{\"input_tokens\":1}}}"));

        var entries = TranscriptReader.Read(dir.Root, since: null).ToList();

        var e = Assert.Single(entries);
        Assert.Equal("m1", e.Id);
    }

    [Fact]
    public void Ignores_synthetic_model_entries()
    {
        using var dir = new TranscriptDir();
        dir.Write("a.jsonl", "m1", model: "<synthetic>");

        Assert.Empty(TranscriptReader.Read(dir.Root, since: null));
    }
}
