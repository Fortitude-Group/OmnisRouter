using System.Text.Json;

namespace OmnisRouter.Adapters.Tests.Fixtures;

/// <summary>
/// Loads recorded upstream response fixtures (small, hand-trimmed JSON payloads captured from real
/// provider responses) out of the <c>Fixtures/</c> folder. Fixture files are set to
/// <c>CopyToOutputDirectory</c> in the csproj so they land next to the test binaries.
/// </summary>
public static class FixtureLoader
{
    private static readonly string FixturesDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>Reads a fixture file's raw text content by file name (e.g. "openai-chat-completion-nonstream.json").</summary>
    public static string ReadText(string fileName)
    {
        var path = Path.Combine(FixturesDir, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Fixture '{fileName}' not found at '{path}'. Ensure it is set to CopyToOutputDirectory.",
                path);
        }

        return File.ReadAllText(path);
    }

    /// <summary>Reads and parses a fixture JSON file into a <see cref="JsonDocument"/>. Caller owns disposal.</summary>
    public static JsonDocument ReadJson(string fileName) => JsonDocument.Parse(ReadText(fileName));
}
