using System.Text.Json;
using System.Text.Json.Nodes;
using OmnisRouter.ClientLink;

namespace OmnisRouter.ClientLink.Tests;

/// <summary>
/// The executor around the pure link transforms (T033): it reads the client's config file, writes a
/// timestamped backup, applies the connect/revert transform to disk, and sets/restores the user
/// environment variables through an injected <see cref="IUserEnvironment"/> so it stays testable off
/// Windows. The pure transforms themselves are covered by the per-link golden tests.
/// </summary>
public sealed class ClientLinkServiceTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "omr-linksvc-" + Guid.NewGuid().ToString("N"));

    private const string Root = "http://127.0.0.1:8787";
    private const string Token = "sk-omr-test-token";

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private string SeedClaudeSettings(string content)
    {
        var path = Path.Combine(_home, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Connect_ClaudeCode_WritesMergedFile_BacksUpOriginal_AndRevertRestoresIt()
    {
        var original = "{\n  \"env\": {\n    \"FOO\": \"bar\"\n  }\n}\n";
        var path = SeedClaudeSettings(original);
        var env = new FakeUserEnvironment();
        var svc = new ClientLinkService(env);
        var link = new ClaudeCodeLink(_home);

        var record = svc.Connect(link, Root, Token);

        // File now carries the merged keys alongside the untouched FOO.
        var connected = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var connectedEnv = connected["env"]!.AsObject();
        Assert.Equal("bar", (string?)connectedEnv["FOO"]);
        Assert.Equal(Root, (string?)connectedEnv["ANTHROPIC_BASE_URL"]);
        Assert.Equal(Token, (string?)connectedEnv["ANTHROPIC_AUTH_TOKEN"]);

        // A timestamped backup of the original was written.
        Assert.NotNull(record.BackupPath);
        Assert.True(File.Exists(record.BackupPath));
        Assert.Equal(original, File.ReadAllText(record.BackupPath!));
        Assert.Equal(ClientKind.ClaudeCode, record.Kind);

        svc.Revert(link, record);

        // The two managed keys are gone; FOO remains.
        var reverted = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var revertedEnv = reverted["env"]!.AsObject();
        Assert.Equal("bar", (string?)revertedEnv["FOO"]);
        Assert.False(revertedEnv.ContainsKey("ANTHROPIC_BASE_URL"));
        Assert.False(revertedEnv.ContainsKey("ANTHROPIC_AUTH_TOKEN"));
    }

    [Fact]
    public void Connect_Codex_SetsEnvVar_CapturesAbsentPrior_AndRevertUnsetsIt()
    {
        var env = new FakeUserEnvironment();
        var svc = new ClientLinkService(env);
        var link = new CodexLink(_home);

        var record = svc.Connect(link, Root, Token);

        // The user env var was set to the token, and its prior (absent) state was captured as null.
        Assert.Equal(Token, env.Get("OMNISROUTER_API_KEY"));
        Assert.True(record.PriorEnvironmentValues.ContainsKey("OMNISROUTER_API_KEY"));
        Assert.Null(record.PriorEnvironmentValues["OMNISROUTER_API_KEY"]);

        // The config file was written with the managed block.
        var configPath = Path.Combine(_home, ".codex", "config.toml");
        Assert.Contains("[model_providers.omnisrouter]", File.ReadAllText(configPath));

        svc.Revert(link, record);

        // Absent-before means revert unsets it, and the block is removed from the file.
        Assert.Null(env.Get("OMNISROUTER_API_KEY"));
        Assert.DoesNotContain("[model_providers.omnisrouter]", File.ReadAllText(configPath));
    }

    [Fact]
    public void Revert_Codex_RestoresPriorEnvValue_WhenOneExisted()
    {
        var env = new FakeUserEnvironment();
        env.Set("OMNISROUTER_API_KEY", "previous-value");
        var svc = new ClientLinkService(env);
        var link = new CodexLink(_home);

        var record = svc.Connect(link, Root, Token);
        Assert.Equal(Token, env.Get("OMNISROUTER_API_KEY"));
        Assert.Equal("previous-value", record.PriorEnvironmentValues["OMNISROUTER_API_KEY"]);

        svc.Revert(link, record);

        Assert.Equal("previous-value", env.Get("OMNISROUTER_API_KEY"));
    }

    [Fact]
    public void Connect_Cursor_WritesNoFile_SetsNoEnv_AndRecordsEmptyPrior()
    {
        var env = new FakeUserEnvironment();
        var svc = new ClientLinkService(env);
        var link = new CursorLink(_home);

        var record = svc.Connect(link, Root, Token);

        Assert.Equal(ClientKind.Cursor, record.Kind);
        Assert.Null(record.BackupPath);
        Assert.Empty(env.All);
        Assert.Empty(record.PriorEnvironmentValues);

        // Revert is a no-op that does not throw.
        svc.Revert(link, record);
        Assert.Empty(env.All);
    }

    private sealed class FakeUserEnvironment : IUserEnvironment
    {
        private readonly Dictionary<string, string> _vars = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, string> All => _vars;

        public string? Get(string name) => _vars.TryGetValue(name, out var v) ? v : null;

        public void Set(string name, string value) => _vars[name] = value;

        public void Unset(string name) => _vars.Remove(name);
    }
}
