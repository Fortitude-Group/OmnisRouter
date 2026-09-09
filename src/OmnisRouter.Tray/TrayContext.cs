using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using OmnisRouter.ClientLink;
using OmnisRouter.Collect;
using OmnisRouter.LocalProxy;

namespace OmnisRouter.Tray;

/// <summary>
/// The tray application: hosts the shared <see cref="CollectEngine"/> in the user's session, drives
/// the icon/tooltip/popup from its status, and supervises it (restart on failure) so it behaves like
/// a managed background agent without a session-0 service. All collection logic lives in the engine;
/// this only renders status and wires menu actions.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly SynchronizationContext _ui;
    private readonly NotifyIcon _notify;
    private readonly ToolStripMenuItem _pauseResume;
    private readonly ToolStripMenuItem _routerToggle;
    private readonly StatusPopup _popup;
    private readonly RollingFileLog _log;
    private readonly RouterController _router;

    // The live set of sources excluded from collection because they are connected to the proxy (US4).
    // The engine reads it each tick, so mutating it (on connect/disconnect) takes effect without a
    // restart. Holds ClientKind names (e.g. "ClaudeCode"), matching OmnisRouter.Collect.CollectSource.
    private readonly HashSet<string> _routedClients = new(StringComparer.Ordinal);

    private CollectConfig? _config;
    private HttpReceiptSink? _sink;
    private CollectEngine? _engine;
    private Task? _supervisor;
    private CancellationTokenSource _cts = new();
    private CollectionStatus _last = new();
    private RouterStatus _lastRouter = new(RouterProcessState.Off, 0);
    private RegisteredWaitHandle? _showRegistration;
    private bool _disposed;

    public TrayContext()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _popup = new StatusPopup();
        _router = new RouterController(_ui, BuildReportEnvironment);
        _router.StatusChanged += (_, status) => OnRouterStatusChanged(status);

        _config = CollectConfig.Load();
        _log = new RollingFileLog(RollingFileLog.DefaultDir, _config?.LogMaxBytes ?? 1_048_576, _config?.LogMaxFiles ?? 3);

        _pauseResume = new ToolStripMenuItem("Pause", null, (_, _) => TogglePause());
        _routerToggle = new ToolStripMenuItem("Local router proxy", null, (_, _) => OnToggleRouter()) { CheckOnClick = false };
        var menu = new ContextMenuStrip();
        menu.Items.Add(_pauseResume);
        menu.Items.Add(new ToolStripMenuItem("Open dashboard", null, (_, _) => OpenUrl(_config?.Endpoint)));
        menu.Items.Add(new ToolStripMenuItem("Open logs", null, (_, _) => OpenLogs()));
        menu.Items.Add(new ToolStripMenuItem("Clear logs", null, (_, _) => _log.Clear()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_routerToggle);
        menu.Items.Add(new ToolStripMenuItem("Provider keys…", null, (_, _) => OnProviderKeys()));
        menu.Items.Add(new ToolStripMenuItem("Connect an app…", null, (_, _) => OnConnectApp()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => OnSettings()));
        menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => Quit()));
        menu.Opening += (_, _) =>
        {
            _pauseResume.Text = _engine?.Status.State == CollectState.Paused ? "Resume" : "Pause";
            _routerToggle.Checked = _router.Enabled;
            _routerToggle.Text = _router.Status.State switch
            {
                RouterProcessState.Starting => "Local router proxy (starting…)",
                RouterProcessState.Error => "Local router proxy (error)",
                _ => "Local router proxy",
            };
        };

        _notify = new NotifyIcon
        {
            Icon = TrayIcons.For(CollectState.Idle, RouterProcessState.Off),
            Text = "OmnisRouter",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notify.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _popup.Toggle(_last, _lastRouter, _config?.Endpoint ?? CollectConfig.DefaultEndpoint);
            }
        };

        // Configured? Start. Otherwise onboard, then start if the user saved.
        if (_config is null || _config.Validate() is not null)
        {
            if (ShowSetupDialog())
            {
                StartEngine();
            }
            else
            {
                _notify.Text = "OmnisRouter · not configured";
            }
        }
        else
        {
            StartEngine();
        }

        // US1 scenario 4: come back in the same on/off state the proxy was in at last quit.
        RestoreRouterAtStartup();
    }

    /// <summary>Watch the cross-instance event so a second launch surfaces this instance's popup (FR-005).</summary>
    public void ListenForShowRequests(EventWaitHandle handle) =>
        _showRegistration = ThreadPool.RegisterWaitForSingleObject(
            handle,
            (_, _) => _ui.Post(_ =>
            {
                if (!_disposed)
                {
                    _popup.ShowAt(_last, _lastRouter, _config?.Endpoint ?? CollectConfig.DefaultEndpoint);
                }
            }, null),
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);

    private void StartEngine()
    {
        if (_config is null || !_config.TryResolveKey(out var key))
        {
            return;
        }

        // Make sure the login task exists so a reinstall or an existing-config first run auto-starts.
        var exe = Environment.ProcessPath;
        if (exe is not null && !LoginTask.IsRegistered())
        {
            LoginTask.Register(exe);
        }

        // Exclude any client already connected to the proxy from collection before the engine's
        // backfill runs, so routed traffic is not double-counted from the first tick (US4).
        RefreshRoutedClients();

        _sink = new HttpReceiptSink(_config.Endpoint, key);
        _log.Info($"starting: endpoint={_config.Endpoint} root={_config.ResolveRoot()}");
        _supervisor = Task.Run(() => SuperviseAsync(_cts.Token));
    }

    private async Task SuperviseAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var engine = new CollectEngine(_config!.ToEngineOptions(_routedClients), _sink!, _config.Endpoint, log: _log);
            engine.Emitted += e => Apply(e.Status);
            _engine = engine;
            if (_config.Paused)
            {
                engine.Pause();
            }

            try
            {
                await engine.RunAsync(ct).ConfigureAwait(false);
                break;   // watch mode returns only when cancelled
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Error($"engine failed, restarting in 15s: {ex.Message.Split('\n')[0]}");
                Apply(new CollectionStatus { State = CollectState.Error, LastError = ex.Message.Split('\n')[0], Endpoint = _config.Endpoint });
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    // Marshal every status change to the UI thread before touching NotifyIcon/popup.
    private void Apply(CollectionStatus status) => _ui.Post(_ =>
    {
        if (_disposed)
        {
            return;
        }

        _last = status;
        RefreshDisplay();
    }, null);

    // RouterController already marshals onto _ui before raising StatusChanged, so this runs on the
    // UI thread already — no further Post needed (see RouterController's own doc comment).
    private void OnRouterStatusChanged(RouterStatus status)
    {
        if (_disposed)
        {
            return;
        }

        _lastRouter = status;
        var line = status.Message is null ? $"router {status.State}" : $"router {status.State}: {status.Message}";
        if (status.State == RouterProcessState.Error)
        {
            _log.Error(line);
        }
        else
        {
            _log.Info(line);
        }

        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        _notify.Icon = TrayIcons.For(_last.State, _lastRouter.State);
        _notify.Text = Truncate(StatusFormat.Tooltip(_last) + RouterStatusText.TooltipSuffix(_lastRouter.State));
        if (_popup.Visible)
        {
            _popup.Update(_last, _lastRouter);
        }
    }

    private async void OnToggleRouter()
    {
        // Turning routing on: if there is no OmnisVigil project key yet, offer to capture one so routed
        // spend can be reported to the dashboard (US4 / T040). Declining just leaves the router
        // standalone — routing still works, it simply reports nothing.
        if (!_router.Enabled)
        {
            PromptForProjectKeyIfNoneForReporting();
        }

        try
        {
            await _router.ToggleAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            MessageBox.Show($"Local router proxy: {ex.Message}", "OmnisRouter", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // The OmnisVigil reporting environment for the supervised router: enabled with the collector's
    // endpoint and project key when one is set, otherwise empty (router runs standalone). Invoked by
    // RouterController on each start/restart, so a key captured later is picked up next time.
    private IReadOnlyDictionary<string, string> BuildReportEnvironment()
    {
        var env = new Dictionary<string, string>();
        if (_config is not null && _config.TryResolveKey(out var key))
        {
            env["OmnisVigil__Enabled"] = "true";
            env["OmnisVigil__Endpoint"] = _config.Endpoint;
            env["OmnisVigil__ProjectKey"] = key;
        }

        return env;
    }

    // Mirror the persisted connected-clients set into the live exclusion set the engine reads.
    private void RefreshRoutedClients()
    {
        _routedClients.Clear();
        foreach (var client in _router.ConnectedClients)
        {
            _routedClients.Add(client.Kind.ToString());
        }
    }

    private void PromptForProjectKeyIfNoneForReporting()
    {
        if (_config is not null && _config.TryResolveKey(out _))
        {
            return;
        }

        using var win = new SetupWindow(_config ?? CollectConfig.Load() ?? new CollectConfig());
        if (win.ShowDialog() == DialogResult.OK)
        {
            _config = win.Result;
        }
    }

    private void OnProviderKeys()
    {
        using var win = new KeysWindow(_router);
        win.ShowDialog();
    }

    private void OnConnectApp()
    {
        using var win = new ConnectWindow(_router);
        win.ShowDialog();

        // Connecting/disconnecting a client changes what the collector must exclude (US4). The engine
        // reads the live set each tick, so refreshing it here is enough — no restart needed.
        RefreshRoutedClients();
    }

    private async void RestoreRouterAtStartup()
    {
        try
        {
            await _router.RestoreAtStartupAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            _log.Error($"router restore failed: {ex.Message.Split('\n')[0]}");
        }
    }

    private void TogglePause()
    {
        if (_engine is null || _config is null)
        {
            return;
        }

        if (_engine.Status.State == CollectState.Paused)
        {
            _engine.Resume();
            _config.Paused = false;
        }
        else
        {
            _engine.Pause();
            _config.Paused = true;
        }

        try
        {
            _config.Save();
        }
        catch (IOException)
        {
        }
    }

    private bool ShowSetupDialog()
    {
        using var win = new SetupWindow(_config ?? CollectConfig.Load() ?? new CollectConfig());
        if (win.ShowDialog() != DialogResult.OK)
        {
            return false;
        }

        _config = win.Result;
        return true;
    }

    private async void OnSettings()
    {
        if (!ShowSetupDialog())
        {
            return;
        }

        // Restart cleanly onto the new configuration.
        _cts.Cancel();
        try
        {
            if (_supervisor is not null)
            {
                await _supervisor.ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException)
        {
        }

        _sink?.Dispose();
        _cts = new CancellationTokenSource();
        StartEngine();
    }

    private void OpenLogs()
    {
        var target = File.Exists(_log.ActivePath) ? _log.ActivePath : RollingFileLog.DefaultDir;
        Directory.CreateDirectory(RollingFileLog.DefaultDir);
        OpenUrl(target);
    }

    private static void OpenUrl(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    private static string Truncate(string s) => s.Length <= 63 ? s : s[..63];

    private void Quit()
    {
        _cts.Cancel();
        _notify.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _cts.Cancel();
            _showRegistration?.Unregister(null);
            _notify.Dispose();
            _popup.Dispose();
            _sink?.Dispose();
            _router.Dispose();
        }

        base.Dispose(disposing);
    }
}
