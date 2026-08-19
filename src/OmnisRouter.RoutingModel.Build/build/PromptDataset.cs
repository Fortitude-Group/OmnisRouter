using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmnisRouter.RoutingModel.Build.Build;

/// <summary>One labeled prompt from the dataset JSONL (see routing/BUILD.md for the file format).</summary>
public sealed record PromptRecord(string Text, string Domain);

/// <summary>DTO mirror of one JSONL line: <c>{ "text": "...", "domain": "coding|math|general|..." }</c>.</summary>
internal sealed class PromptRecordDto
{
    [JsonPropertyName("text")] public string? Text { get; init; }
    [JsonPropertyName("domain")] public string? Domain { get; init; }
}

/// <summary>
/// Loads the labeled-prompt dataset used to fit the routing model (embed → k-means → per-cluster
/// policy table). One JSON object per line, blank lines ignored. Production would pin/version this
/// dataset the same way the bench-results are pinned (see routing/BUILD.md); the pipeline that
/// consumes it (embed, cluster, score) is identical regardless of dataset size.
/// </summary>
public static class PromptDataset
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<PromptRecord> Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Dataset file not found: {path}", path);
        }

        var records = new List<PromptRecord>();
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var dto = JsonSerializer.Deserialize<PromptRecordDto>(line, JsonOptions)
                      ?? throw new InvalidDataException($"{path}:{lineNumber}: could not parse JSONL record.");

            if (string.IsNullOrWhiteSpace(dto.Text))
            {
                throw new InvalidDataException($"{path}:{lineNumber}: missing/empty 'text'.");
            }

            if (string.IsNullOrWhiteSpace(dto.Domain))
            {
                throw new InvalidDataException($"{path}:{lineNumber}: missing/empty 'domain'.");
            }

            records.Add(new PromptRecord(dto.Text, dto.Domain));
        }

        if (records.Count == 0)
        {
            throw new InvalidDataException($"Dataset '{path}' contains no records.");
        }

        return records;
    }
}
