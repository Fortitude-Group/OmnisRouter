using System.Globalization;
using System.Text.Json;

namespace OmnisRouter.Api.Collect;

/// <summary>One content-free usage fact read from a single Claude Code transcript entry.</summary>
internal sealed record UsageEntry(
    string Id, DateTimeOffset Timestamp, string Model,
    long InputTokens, long OutputTokens, long CacheReadTokens, long CacheCreationTokens,
    string? SessionId, string? Project, string? Branch, string? RequestId);

/// <summary>
/// Reads Claude Code session transcripts (<c>~/.claude/projects/**/*.jsonl</c>, subagent
/// transcripts included) and yields the content-free usage of each assistant response. This is
/// the router's "observe" mode for flat-rate subscriptions, which cannot be proxied: only the
/// usage fields are read, never the message text, so nothing content-bearing leaves the file.
/// Each entry is attributed to the git repository the session ran in.
/// </summary>
internal static class TranscriptReader
{
    public static IEnumerable<UsageEntry> Read(string root, DateTimeOffset? since)
    {
        foreach (var file in EnumerateFiles(root))
        {
            foreach (var entry in ReadFile(file, since))
            {
                yield return entry;
            }
        }
    }

    /// <summary>Every transcript file under the root, subagent transcripts included.</summary>
    public static IEnumerable<string> EnumerateFiles(string root)
        => Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories);

    public static IEnumerable<UsageEntry> ReadFile(string file, DateTimeOffset? since)
    {
        IEnumerable<string> lines;
        try
        {
            lines = File.ReadLines(file);
        }
        catch (IOException)
        {
            yield break;   // a transcript mid-write can be briefly unreadable; skip it
        }

        foreach (var line in lines)
        {
            if (Parse(line, since) is { } entry)
            {
                yield return entry;
            }
        }
    }

    private static UsageEntry? Parse(string line, DateTimeOffset? since)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (!IsAssistant(root)
                || !root.TryGetProperty("message", out var message)
                || !message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var model = Str(message, "model");
            var id = Str(message, "id");
            if (model is null || model == "<synthetic>" || id is null)
            {
                return null;
            }

            var timestamp = Time(root, "timestamp");
            if (since is { } from && timestamp < from)
            {
                return null;
            }

            return new UsageEntry(
                id, timestamp, model,
                Long(usage, "input_tokens"), Long(usage, "output_tokens"),
                Long(usage, "cache_read_input_tokens"), Long(usage, "cache_creation_input_tokens"),
                Str(root, "sessionId"), ProjectFrom(Str(root, "cwd")), Str(root, "gitBranch"), Str(root, "requestId"));
        }
    }

    private static bool IsAssistant(JsonElement root)
        => root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() == "assistant";

    // Attribute to the git repository the working directory sits in, so a session started in a
    // subfolder rolls up under its repo rather than fragmenting into a row per folder.
    private static readonly Dictionary<string, string?> ProjectCache = new(StringComparer.OrdinalIgnoreCase);

    private static string? ProjectFrom(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return null;
        }

        if (ProjectCache.TryGetValue(cwd, out var cached))
        {
            return cached;
        }

        var resolved = ResolveProject(cwd);
        ProjectCache[cwd] = resolved;
        return resolved;
    }

    private static string? ResolveProject(string cwd)
    {
        try
        {
            for (var dir = new DirectoryInfo(cwd); dir is not null; dir = dir.Parent)
            {
                var git = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(git) || File.Exists(git))   // File covers git worktrees
                {
                    return dir.Name;
                }
            }
        }
        catch (IOException)
        {
            // fall through to the leaf name
        }
        catch (UnauthorizedAccessException)
        {
            // fall through to the leaf name
        }

        var trimmed = cwd.Replace('\\', '/').TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash >= 0 && slash < trimmed.Length - 1 ? trimmed[(slash + 1)..] : trimmed;
    }

    private static string? Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long Long(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;

    private static DateTimeOffset Time(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt)
            ? dt
            : DateTimeOffset.UnixEpoch;
}
