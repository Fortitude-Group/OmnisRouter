using System.Globalization;
using System.Text;

namespace OmnisRouter.Collect;

/// <summary>
/// A tiny thread-safe rolling log: one active file plus a bounded number of rotated files, each
/// capped in size, so diagnostics survive without a console but cannot grow without limit
/// (FR-014/FR-015). Purpose-built to avoid a logging-framework dependency for one bounded file.
/// </summary>
public sealed class RollingFileLog : ICollectLog
{
    private readonly object _lock = new();
    private readonly string _dir;
    private readonly string _active;
    private readonly long _maxBytes;
    private readonly int _maxFiles;

    public RollingFileLog(string directory, long maxBytes, int maxFiles)
    {
        _dir = directory;
        _active = Path.Combine(directory, "collect.log");
        _maxBytes = Math.Max(1024, maxBytes);
        _maxFiles = Math.Max(1, maxFiles);
    }

    public static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmnisRouter", "logs");

    public string ActivePath => _active;

    public void Info(string message) => Write("INFO", message);

    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)} [{level}] {message}{Environment.NewLine}";
        var bytes = Encoding.UTF8.GetByteCount(line);

        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(_dir);
                if (File.Exists(_active) && new FileInfo(_active).Length + bytes > _maxBytes)
                {
                    Roll();
                }

                File.AppendAllText(_active, line, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Logging must never take the watcher down; drop the line if the disk is unhappy.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void Roll()
    {
        // _maxFiles counts the active file too, so we keep at most (_maxFiles - 1) rotated files.
        // Total on-disk stays within _maxFiles * _maxBytes (data-model.md LogSet).
        var rotated = _maxFiles - 1;
        if (rotated <= 0)
        {
            File.Delete(_active);   // single-file cap: discard and start fresh
            return;
        }

        var oldest = $"{_active}.{rotated}";
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var k = rotated - 1; k >= 1; k--)
        {
            var src = $"{_active}.{k}";
            if (File.Exists(src))
            {
                File.Move(src, $"{_active}.{k + 1}", overwrite: true);
            }
        }

        File.Move(_active, $"{_active}.1", overwrite: true);
    }

    /// <summary>Delete the active log and every rotated file.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_active))
                {
                    File.Delete(_active);
                }

                for (var k = 1; k <= _maxFiles; k++)
                {
                    var f = $"{_active}.{k}";
                    if (File.Exists(f))
                    {
                        File.Delete(f);
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
