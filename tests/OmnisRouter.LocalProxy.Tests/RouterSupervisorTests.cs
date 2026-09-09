using OmnisRouter.LocalProxy;

namespace OmnisRouter.LocalProxy.Tests;

public sealed class RouterSupervisorTests
{
    private sealed class FakeProcessHandle : IProcessHandle
    {
        public bool HasExited { get; private set; }

        public int KillCount { get; private set; }

        public event EventHandler? Exited;

        public void Kill()
        {
            KillCount++;
            SimulateExit();
        }

        public void SimulateExit()
        {
            if (HasExited)
            {
                return;
            }

            HasExited = true;
            Exited?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeProcessLauncher : IProcessLauncher
    {
        private readonly Queue<FakeProcessHandle> _queued = new();

        public int StartCount { get; private set; }

        public void Enqueue(FakeProcessHandle handle) => _queued.Enqueue(handle);

        public IProcessHandle Start(string exePath, string workingDir, IReadOnlyDictionary<string, string> environment, IReadOnlyList<string> args)
        {
            StartCount++;
            return _queued.Count > 0 ? _queued.Dequeue() : new FakeProcessHandle();
        }
    }

    private static RouterPaths TestPaths()
    {
        var dir = Path.Combine(Path.GetTempPath(), "OmnisRouter.LocalProxy.Tests", Guid.NewGuid().ToString("N"));
        return new RouterPaths(dir, dir);
    }

    // No-op delay: the poll loop's iteration budget (timeout / poll interval) still bounds how many
    // times readiness is checked, so this keeps every test fast without any real wall-clock wait.
    private static Task NoDelay(TimeSpan ts, CancellationToken ct) => Task.CompletedTask;

    [Fact]
    public async Task StartAsync_ReadinessSucceeds_TransitionsToRunning()
    {
        var launcher = new FakeProcessLauncher();
        var statuses = new List<RouterStatus>();
        var supervisor = new RouterSupervisor(launcher, _ => Task.FromResult(true), TestPaths(), NoDelay);
        supervisor.StatusChanged += (_, s) => statuses.Add(s);

        await supervisor.StartAsync(8787, "tok");

        Assert.Equal(RouterProcessState.Running, supervisor.Status.State);
        Assert.Equal(8787, supervisor.Status.Port);
        Assert.Contains(statuses, s => s.State == RouterProcessState.Starting);
        Assert.Contains(statuses, s => s.State == RouterProcessState.Running);
    }

    [Fact]
    public async Task StartAsync_ReadinessNeverPasses_TransitionsToError()
    {
        var launcher = new FakeProcessLauncher();
        var supervisor = new RouterSupervisor(
            launcher, _ => Task.FromResult(false), TestPaths(), NoDelay, readinessTimeout: TimeSpan.FromMilliseconds(50));

        await supervisor.StartAsync(8787, "tok");

        Assert.Equal(RouterProcessState.Error, supervisor.Status.State);
        Assert.Contains("timed out", supervisor.Status.Message ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartAsync_ProcessExitsBeforeReady_TransitionsToError()
    {
        var launcher = new FakeProcessLauncher();
        var handle = new FakeProcessHandle();
        launcher.Enqueue(handle);
        var checks = 0;

        var supervisor = new RouterSupervisor(
            launcher,
            readinessCheck: _ =>
            {
                checks++;
                if (checks == 2)
                {
                    handle.SimulateExit();
                }

                return Task.FromResult(false);
            },
            TestPaths(),
            NoDelay);

        await supervisor.StartAsync(8787, "tok");

        Assert.Equal(RouterProcessState.Error, supervisor.Status.State);
        Assert.Contains("port in use", supervisor.Status.Message ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnexpectedExit_WhileRunning_AutoRestarts_BackToRunning()
    {
        var launcher = new FakeProcessLauncher();
        var first = new FakeProcessHandle();
        launcher.Enqueue(first);

        var supervisor = new RouterSupervisor(launcher, _ => Task.FromResult(true), TestPaths(), NoDelay);
        var secondRunning = new TaskCompletionSource();
        var runningCount = 0;
        supervisor.StatusChanged += (_, s) =>
        {
            if (s.State == RouterProcessState.Running && ++runningCount == 2)
            {
                secondRunning.TrySetResult();
            }
        };

        await supervisor.StartAsync(8787, "tok");
        Assert.Equal(RouterProcessState.Running, supervisor.Status.State);

        first.SimulateExit();

        var completed = await Task.WhenAny(secondRunning.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(secondRunning.Task, completed);
        Assert.Equal(RouterProcessState.Running, supervisor.Status.State);
        Assert.Equal(2, launcher.StartCount);
    }

    [Fact]
    public async Task RepeatedRestartFailures_ExhaustAttempts_TransitionsToError()
    {
        var launcher = new FakeProcessLauncher();
        var initial = new FakeProcessHandle();
        launcher.Enqueue(initial);

        // Ready for the initial start only; every restart attempt after the crash times out.
        var reachedRunning = false;
        var supervisor = new RouterSupervisor(
            launcher,
            readinessCheck: _ => Task.FromResult(!reachedRunning),
            TestPaths(),
            NoDelay,
            readinessTimeout: TimeSpan.FromMilliseconds(20),
            maxRestartAttempts: 2);

        var errorReached = new TaskCompletionSource();
        supervisor.StatusChanged += (_, s) =>
        {
            if (s.State == RouterProcessState.Error)
            {
                errorReached.TrySetResult();
            }
        };

        await supervisor.StartAsync(8787, "tok");
        Assert.Equal(RouterProcessState.Running, supervisor.Status.State);
        reachedRunning = true;

        initial.SimulateExit();

        var completed = await Task.WhenAny(errorReached.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(errorReached.Task, completed);
        Assert.Equal(RouterProcessState.Error, supervisor.Status.State);

        // 1 initial launch + 2 exhausted restart attempts.
        Assert.Equal(3, launcher.StartCount);
    }

    [Fact]
    public async Task StopAsync_SetsOff_AndDoesNotTriggerRestart()
    {
        var launcher = new FakeProcessLauncher();
        var handle = new FakeProcessHandle();
        launcher.Enqueue(handle);

        var supervisor = new RouterSupervisor(launcher, _ => Task.FromResult(true), TestPaths(), NoDelay);
        await supervisor.StartAsync(8787, "tok");
        Assert.Equal(RouterProcessState.Running, supervisor.Status.State);

        await supervisor.StopAsync();

        Assert.Equal(RouterProcessState.Off, supervisor.Status.State);
        Assert.Equal(1, handle.KillCount);

        // Give any wrongly-fired restart path a chance to run before asserting it didn't.
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.Equal(RouterProcessState.Off, supervisor.Status.State);
        Assert.Equal(1, launcher.StartCount);
    }
}
