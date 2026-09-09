namespace OmnisRouter.ClientLink.Tests;

/// <summary>
/// Golden tests for <see cref="CodexLink"/> (contracts/client-link.md "Codex"). The expected
/// strings below are hardcoded independently of the implementation so a regression in the managed
/// block text, spacing, or marker handling is caught exactly.
/// </summary>
public class CodexLinkTests
{
    private const string Root = "http://127.0.0.1:8787";
    private const string StaleRoot = "http://127.0.0.1:9999";
    private const string Token = "sk-omr-test-token";

    private static string ExpectedBlock(string root) =>
        "# >>> omnisrouter-cli managed block >>>\n" +
        "# Added by omnisrouter-cli. Safe to edit or delete this block by hand.\n" +
        "# Set model_provider = \"omnisrouter\" (top level, or per-profile) to actually route through it.\n" +
        "[model_providers.omnisrouter]\n" +
        "name = \"OmnisRouter\"\n" +
        $"base_url = \"{root}/v1\"\n" +
        "env_key = \"OMNISROUTER_API_KEY\"\n" +
        "wire_api = \"chat\"\n" +
        "# <<< omnisrouter-cli managed block <<<\n";

    private static string ExpectedMarkerToMarker(string root) =>
        ExpectedBlock(root).TrimEnd('\n');

    [Fact]
    public void Connect_IntoEmptyFile_ProducesExactlyTheBlock()
    {
        var link = new CodexLink();

        var result = link.Connect(Root, Token, currentContent: null);

        Assert.Equal(ExpectedBlock(Root), result.NewFileContent);
        Assert.Null(result.PriorState.PriorManagedBlock);
    }

    [Fact]
    public void Connect_IntoAbsentFile_TreatsEmptyStringLikeNull()
    {
        var link = new CodexLink();

        var result = link.Connect(Root, Token, currentContent: string.Empty);

        Assert.Equal(ExpectedBlock(Root), result.NewFileContent);
        Assert.Null(result.PriorState.PriorManagedBlock);
    }

    [Fact]
    public void Connect_AppendsAfterExistingUnrelatedContent_WithCorrectSpacing()
    {
        var link = new CodexLink();
        const string existing = "[model_providers.other]\nname = \"Other\"\n";

        var result = link.Connect(Root, Token, existing);

        var expected = "[model_providers.other]\nname = \"Other\"\n\n" + ExpectedBlock(Root);
        Assert.Equal(expected, result.NewFileContent);
        Assert.Null(result.PriorState.PriorManagedBlock);
    }

    [Fact]
    public void Connect_WithExistingManagedBlock_ReplacesInPlaceAndCapturesPriorBlock()
    {
        var link = new CodexLink();
        var existing = "[model_providers.other]\nname = \"Other\"\n\n" + ExpectedBlock(StaleRoot);

        var result = link.Connect(Root, Token, existing);

        var expected = "[model_providers.other]\nname = \"Other\"\n\n" + ExpectedBlock(Root);
        Assert.Equal(expected, result.NewFileContent);
        Assert.Equal(ExpectedMarkerToMarker(StaleRoot), result.PriorState.PriorManagedBlock);

        // Idempotent: no duplicate block (exactly one start marker present).
        Assert.Equal(1, CountOccurrences(result.NewFileContent!, "# >>> omnisrouter-cli managed block >>>"));
    }

    [Fact]
    public void ConnectThenRevert_WithNoPriorBlock_ReturnsOriginalContentExactly()
    {
        var link = new CodexLink();
        const string original = "[model_providers.other]\nname = \"Other\"\n";

        var connected = link.Connect(Root, Token, original);
        var reverted = link.Revert(connected.NewFileContent, connected.PriorState);

        Assert.Equal(original, reverted);
    }

    [Fact]
    public void ConnectThenRevert_WithPriorManagedBlock_RestoresPriorBlockVerbatim()
    {
        var link = new CodexLink();
        var original = "[model_providers.other]\nname = \"Other\"\n\n" + ExpectedBlock(StaleRoot);

        var connected = link.Connect(Root, Token, original);
        var reverted = link.Revert(connected.NewFileContent, connected.PriorState);

        Assert.Equal(original, reverted);
    }

    [Fact]
    public void Connect_SetsOmnisRouterApiKeyEnvironmentVariable()
    {
        var link = new CodexLink();

        var result = link.Connect(Root, Token, currentContent: null);

        Assert.Equal(Token, result.EnvironmentVariablesToSet["OMNISROUTER_API_KEY"]);
        Assert.Empty(result.DisplayValues);
        Assert.Empty(result.PriorState.PriorValues);
    }

    [Fact]
    public void Connect_WithOnlyStartMarker_ThrowsClientLinkException()
    {
        var link = new CodexLink();
        const string malformed = "# >>> omnisrouter-cli managed block >>>\nsome stray content\n";

        Assert.Throws<ClientLinkException>(() => link.Connect(Root, Token, malformed));
    }

    [Fact]
    public void Connect_WithOnlyEndMarker_ThrowsClientLinkException()
    {
        var link = new CodexLink();
        const string malformed = "some stray content\n# <<< omnisrouter-cli managed block <<<\n";

        Assert.Throws<ClientLinkException>(() => link.Connect(Root, Token, malformed));
    }

    [Fact]
    public void Kind_IsCodex()
    {
        Assert.Equal(ClientKind.Codex, new CodexLink().Kind);
    }

    [Fact]
    public void ConfigPath_ComposesFromHomeDirectory()
    {
        var link = new CodexLink(homeDirectory: @"C:\fake-home");

        Assert.Equal(Path.Combine(@"C:\fake-home", ".codex", "config.toml"), link.ConfigPath);
    }

    [Fact]
    public void IsInstalled_ReflectsWhetherConfigFileExists()
    {
        var tempHome = Directory.CreateTempSubdirectory("codexlink-tests-");
        try
        {
            var link = new CodexLink(homeDirectory: tempHome.FullName);
            Assert.False(link.IsInstalled);

            var configDir = Path.Combine(tempHome.FullName, ".codex");
            Directory.CreateDirectory(configDir);
            File.WriteAllText(Path.Combine(configDir, "config.toml"), string.Empty);

            Assert.True(link.IsInstalled);
        }
        finally
        {
            tempHome.Delete(recursive: true);
        }
    }

    private static int CountOccurrences(string content, string marker) =>
        content.Split(marker, StringSplitOptions.None).Length - 1;
}
