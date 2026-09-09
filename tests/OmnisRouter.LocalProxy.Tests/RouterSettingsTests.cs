using OmnisRouter.ClientLink;
using OmnisRouter.LocalProxy;

namespace OmnisRouter.LocalProxy.Tests;

public sealed class RouterSettingsTests
{
    // Simple reversible transform standing in for DPAPI. Base64 rather than a plain prefix, so it
    // doesn't leave the plaintext sitting in the ciphertext as a substring (see the plaintext-leak
    // test below) — a prefix alone would defeat that assertion for any input.
    private sealed class FakeSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));

        public string Unprotect(string protectedValue) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue));
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"router-settings-{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_WhenFileAbsent_ReturnsDefaults()
    {
        var settings = RouterSettings.Load(TempPath(), new FakeSecretProtector());

        Assert.Equal(RouterSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.False(settings.Enabled);
        Assert.Equal(8787, settings.Port);
        Assert.Null(settings.ProtectedToken);
        Assert.Empty(settings.ConnectedClients);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1023)]
    [InlineData(65536)]
    public void Port_OutOfRange_Throws(int port)
    {
        var settings = new RouterSettings();
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Port = port);
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(8787)]
    [InlineData(65535)]
    public void Port_InRange_Accepted(int port)
    {
        var settings = new RouterSettings { Port = port };
        Assert.Equal(port, settings.Port);
    }

    [Fact]
    public void Token_RoundTrips_ThroughProtector()
    {
        var protector = new FakeSecretProtector();
        var settings = new RouterSettings();

        settings.SetToken("super-secret-token", protector);

        Assert.Equal("super-secret-token", settings.GetToken(protector));
        Assert.NotEqual("super-secret-token", settings.ProtectedToken);
    }

    [Fact]
    public void GetToken_WhenNoneSet_ReturnsNull()
    {
        var settings = new RouterSettings();
        Assert.Null(settings.GetToken(new FakeSecretProtector()));
    }

    [Fact]
    public void SaveAndLoad_RoundTripAcrossTempFile()
    {
        var path = TempPath();
        try
        {
            var protector = new FakeSecretProtector();
            var connectedAt = DateTimeOffset.Parse("2026-09-01T12:00:00Z");
            var settings = new RouterSettings { Enabled = true, Port = 9090 };
            settings.SetToken("round-trip-token", protector);
            settings.ConnectedClients.Add(new ConnectedClient(
                ClientKind.ClaudeCode,
                connectedAt,
                new ClientPriorState { PriorValues = new Dictionary<string, string?> { ["ANTHROPIC_BASE_URL"] = null } },
                BackupPath: "settings.json.bak-2026-09-01T12-00-00-000Z",
                PriorEnvironmentValues: new Dictionary<string, string?>()));

            settings.Save(path);
            var loaded = RouterSettings.Load(path, protector);

            Assert.True(loaded.Enabled);
            Assert.Equal(9090, loaded.Port);
            Assert.Equal("round-trip-token", loaded.GetToken(protector));
            var client = Assert.Single(loaded.ConnectedClients);
            Assert.Equal(ClientKind.ClaudeCode, client.Kind);
            Assert.Equal(connectedAt, client.ConnectedAt);
            Assert.Equal("settings.json.bak-2026-09-01T12-00-00-000Z", client.BackupPath);
            Assert.True(client.PriorState.PriorValues.ContainsKey("ANTHROPIC_BASE_URL"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_WritesCamelCaseJson_AndNeverPlaintextToken()
    {
        var path = TempPath();
        try
        {
            var protector = new FakeSecretProtector();
            var settings = new RouterSettings();
            settings.SetToken("do-not-leak-me", protector);

            settings.Save(path);
            var json = File.ReadAllText(path);

            Assert.Contains("\"schemaVersion\"", json);
            Assert.Contains("\"protectedToken\"", json);
            Assert.Contains("\"connectedClients\"", json);
            Assert.DoesNotContain("do-not-leak-me", json);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
