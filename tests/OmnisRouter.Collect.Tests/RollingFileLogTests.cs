using OmnisRouter.Collect;

namespace OmnisRouter.Collect.Tests;

public class RollingFileLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "omnis-log", Guid.NewGuid().ToString("n"));

    private long TotalBytes() => !Directory.Exists(_dir)
        ? 0
        : Directory.EnumerateFiles(_dir).Sum(f => new FileInfo(f).Length);

    [Fact]
    public void Writes_activity_to_the_active_file()
    {
        var log = new RollingFileLog(_dir, 1_048_576, 3);
        log.Info("started watching");
        log.Error("tick failed: boom");

        var text = File.ReadAllText(log.ActivePath);
        Assert.Contains("[INFO] started watching", text);
        Assert.Contains("[ERROR] tick failed: boom", text);
    }

    [Fact]
    public void Stays_within_the_cap_across_many_writes()
    {
        const long maxBytes = 2048;
        const int maxFiles = 3;
        var log = new RollingFileLog(_dir, maxBytes, maxFiles);

        for (var i = 0; i < 2000; i++)
        {
            log.Info($"line {i} with some padding to grow the file quickly");
        }

        Assert.True(Directory.EnumerateFiles(_dir).Count() <= maxFiles, "kept more files than allowed");
        Assert.True(TotalBytes() <= maxBytes * maxFiles, $"log grew to {TotalBytes()} bytes, over the {maxBytes * maxFiles} cap");
    }

    [Fact]
    public void Single_file_cap_discards_old_content_on_roll()
    {
        var log = new RollingFileLog(_dir, 512, maxFiles: 1);
        for (var i = 0; i < 500; i++)
        {
            log.Info($"line {i} padded out to force several rolls");
        }

        Assert.Single(Directory.EnumerateFiles(_dir));
        Assert.True(new FileInfo(log.ActivePath).Length <= 512 + 200);   // at most the last line over
    }

    [Fact]
    public void Clear_empties_the_whole_set()
    {
        var log = new RollingFileLog(_dir, 512, 3);
        for (var i = 0; i < 500; i++)
        {
            log.Info($"line {i} padded out to force rolls and rotated files");
        }

        Assert.True(Directory.EnumerateFiles(_dir).Any());
        log.Clear();
        Assert.Empty(Directory.EnumerateFiles(_dir));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
