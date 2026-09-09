using OmnisRouter.LocalProxy;

namespace OmnisRouter.LocalProxy.Tests;

/// <summary>
/// US5: the loopback port is user-settable (default 8787), validated to a sane range, and changing it
/// restarts the router on the new port. Validation is a non-throwing check the settings window can show
/// as an error; the restart-on-new-port behaviour is exercised at the supervisor level here (the tray's
/// re-point-connected-clients orchestration is Windows-only and covered by the build).
/// </summary>
public sealed class PortSettingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1023)]
    [InlineData(65536)]
    [InlineData(100000)]
    public void ValidatePort_returns_a_message_for_out_of_range(int port)
    {
        Assert.NotNull(RouterSettings.ValidatePort(port));
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(8787)]
    [InlineData(65535)]
    public void ValidatePort_returns_null_for_in_range(int port)
    {
        Assert.Null(RouterSettings.ValidatePort(port));
    }

    [Fact]
    public async Task StartAsync_launches_on_the_given_port()
    {
        var launcher = new CapturingLauncher();
        var supervisor = new RouterSupervisor(launcher, _ => Task.FromResult(true), TestPaths(), NoDelay);

        await supervisor.StartAsync(8787, "tok");

        Assert.Contains("http://127.0.0.1:8787", launcher.LastArgs);
    }

    [Fact]
    public async Task Restart_on_a_new_port_launches_on_the_new_port()
    {
        var launcher = new CapturingLauncher();
        var supervisor = new RouterSupervisor(launcher, _ => Task.FromResult(true), TestPaths(), NoDelay);

        await supervisor.StartAsync(8787, "tok");
        await supervisor.StopAsync();
        await supervisor.StartAsync(9191, "tok");

        Assert.Contains("http://127.0.0.1:9191", launcher.LastArgs);
        Assert.DoesNotContain("http://127.0.0.1:8787", launcher.LastArgs);
    }

    private static RouterPaths TestPaths()
    {
        var dir = Path.Combine(Path.GetTempPath(), "OmnisRouter.PortTests", Guid.NewGuid().ToString("N"));
        return new RouterPaths(dir, dir);
    }

    private static Task NoDelay(TimeSpan ts, CancellationToken ct) => Task.CompletedTask;

    private sealed class CapturingHandle : IProcessHandle
    {
        public bool HasExited { get; private set; }

        public event EventHandler? Exited;

        public void Kill()
        {
            HasExited = true;
            Exited?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class CapturingLauncher : IProcessLauncher
    {
        public IReadOnlyList<string> LastArgs { get; private set; } = [];

        public IProcessHandle Start(string exePath, string workingDir, IReadOnlyDictionary<string, string> environment, IReadOnlyList<string> args)
        {
            LastArgs = args;
            return new CapturingHandle();
        }
    }
}
