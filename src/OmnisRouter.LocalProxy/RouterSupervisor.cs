namespace OmnisRouter.LocalProxy;

/// <summary>Seam over a launched child process, so supervision is testable without a real process.</summary>
public interface IProcessHandle
{
    bool HasExited { get; }

    event EventHandler Exited;

    void Kill();
}

/// <summary>Seam over process creation.</summary>
public interface IProcessLauncher
{
    IProcessHandle Start(string exePath, string workingDir, IReadOnlyDictionary<string, string> environment, IReadOnlyList<string> args);
}

/// <summary>
/// Supervises the bundled router server as a hidden child process (contracts/router-management.md
/// launch contract): launches with the loopback URL and bootstrap token, polls readiness with a
/// bounded timeout, and restarts on an unexpected exit with exponential backoff up to a max attempt
/// count. A deliberate <see cref="StopAsync"/> never triggers the restart path.
/// </summary>
public sealed class RouterSupervisor
{
    private const int DefaultMaxRestartAttempts = 3;

    private readonly IProcessLauncher _launcher;
    private readonly Func<CancellationToken, Task<bool>> _readinessCheck;
    private readonly RouterPaths _paths;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _readinessTimeout;
    private readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(200);
    private readonly int _maxRestartAttempts;
    private readonly object _gate = new();

    private IProcessHandle? _process;
    private bool _stopRequested;
    private int _lastPort;
    private string _lastToken = "";
    private IReadOnlyDictionary<string, string> _lastReportEnvironment = new Dictionary<string, string>();

    public RouterSupervisor(
        IProcessLauncher launcher,
        Func<CancellationToken, Task<bool>> readinessCheck,
        RouterPaths paths,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? readinessTimeout = null,
        int maxRestartAttempts = DefaultMaxRestartAttempts)
    {
        _launcher = launcher;
        _readinessCheck = readinessCheck;
        _paths = paths;
        _delay = delay ?? ((ts, ct) => Task.Delay(ts, ct));
        _readinessTimeout = readinessTimeout ?? TimeSpan.FromSeconds(30);
        _maxRestartAttempts = maxRestartAttempts;
    }

    public event EventHandler<RouterStatus>? StatusChanged;

    public RouterStatus Status { get; private set; } = new(RouterProcessState.Off, 0);

    /// <summary>Start (or restart) the router. <paramref name="reportEnvironment"/> carries extra
    /// environment the server should bind — the <c>OmnisVigil__*</c> reporting settings when the tray
    /// wants routed receipts pushed to OmnisVigil (US4). It is remembered so an auto-restart re-seeds
    /// it; pass an empty map (the default) for a standalone router.</summary>
    public async Task StartAsync(int port, string token, IReadOnlyDictionary<string, string>? reportEnvironment = null, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _stopRequested = false;
            _lastPort = port;
            _lastToken = token;
            _lastReportEnvironment = reportEnvironment ?? new Dictionary<string, string>();
        }

        SetStatus(RouterProcessState.Starting, message: null, port);

        var handle = LaunchProcess(port, token);
        var result = await PollReadinessAsync(handle, _readinessTimeout, ct).ConfigureAwait(false);

        switch (result)
        {
            case ReadinessResult.Ready:
                SetStatus(RouterProcessState.Running, message: null, port);
                break;
            case ReadinessResult.ProcessExited:
                SetStatus(RouterProcessState.Error, "router exited before ready / port in use", port);
                break;
            case ReadinessResult.TimedOut:
            default:
                SetStatus(RouterProcessState.Error, "readiness timed out", port);
                break;
        }
    }

    public Task StopAsync()
    {
        IProcessHandle? handle;
        lock (_gate)
        {
            _stopRequested = true;
            handle = _process;
            _process = null;
        }

        if (handle is not null)
        {
            // Unsubscribe before killing: a fake (or real) process can raise Exited synchronously
            // from within Kill(), and a deliberate stop must never fall into the crash-restart path.
            handle.Exited -= OnProcessExited;
            if (!handle.HasExited)
            {
                handle.Kill();
            }
        }

        SetStatus(RouterProcessState.Off, message: null);
        return Task.CompletedTask;
    }

    private IProcessHandle LaunchProcess(int port, string token)
    {
        var workingDir = _paths.EnsureWorkingDirectory();
        var environment = new Dictionary<string, string> { ["Omnis__BootstrapToken"] = token };
        lock (_gate)
        {
            foreach (var (key, value) in _lastReportEnvironment)
            {
                environment[key] = value;
            }
        }

        var args = new List<string> { "--urls", $"http://127.0.0.1:{port}" };

        var handle = _launcher.Start(_paths.ServerExecutablePath, workingDir, environment, args);
        lock (_gate)
        {
            _process = handle;
        }

        handle.Exited += OnProcessExited;
        return handle;
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_stopRequested)
            {
                return;
            }

            // Exits during Starting are already handled by the readiness poll loop itself; only an
            // exit while Running is an unexpected crash that warrants the restart path.
            if (Status.State != RouterProcessState.Running)
            {
                return;
            }
        }

        _ = Task.Run(HandleCrashAsync);
    }

    private async Task HandleCrashAsync()
    {
        var attempt = 0;
        while (true)
        {
            attempt++;

            lock (_gate)
            {
                if (_stopRequested)
                {
                    return;
                }
            }

            if (attempt > _maxRestartAttempts)
            {
                SetStatus(RouterProcessState.Error, "router exited unexpectedly; restart attempts exhausted");
                return;
            }

            SetStatus(RouterProcessState.Starting, $"restarting after unexpected exit (attempt {attempt}/{_maxRestartAttempts})");
            await _delay(Backoff(attempt), CancellationToken.None).ConfigureAwait(false);

            int port;
            string token;
            lock (_gate)
            {
                port = _lastPort;
                token = _lastToken;
            }

            var handle = LaunchProcess(port, token);
            var result = await PollReadinessAsync(handle, _readinessTimeout, CancellationToken.None).ConfigureAwait(false);

            if (result == ReadinessResult.Ready)
            {
                SetStatus(RouterProcessState.Running, message: null, port);
                return;
            }

            if (!handle.HasExited)
            {
                handle.Kill();
            }
        }
    }

    private async Task<ReadinessResult> PollReadinessAsync(IProcessHandle handle, TimeSpan timeout, CancellationToken ct)
    {
        var maxIterations = Math.Max(1, (int)Math.Ceiling(timeout.TotalMilliseconds / _pollInterval.TotalMilliseconds));

        for (var i = 0; i < maxIterations; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (handle.HasExited)
            {
                return ReadinessResult.ProcessExited;
            }

            if (await _readinessCheck(ct).ConfigureAwait(false))
            {
                return ReadinessResult.Ready;
            }

            await _delay(_pollInterval, ct).ConfigureAwait(false);
        }

        return ReadinessResult.TimedOut;
    }

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt - 1)));

    private void SetStatus(RouterProcessState state, string? message, int? port = null)
    {
        RouterStatus status;
        lock (_gate)
        {
            var effectivePort = port ?? Status.Port;
            status = new RouterStatus(state, effectivePort, message);
            Status = status;
        }

        StatusChanged?.Invoke(this, status);
    }

    private enum ReadinessResult
    {
        Ready,
        ProcessExited,
        TimedOut,
    }
}
