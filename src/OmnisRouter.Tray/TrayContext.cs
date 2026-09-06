using System.Diagnostics;
using System.Windows.Forms;
using OmnisRouter.Collect;

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
    private readonly StatusPopup _popup;
    private readonly RollingFileLog _log;

    private CollectConfig? _config;
    private HttpReceiptSink? _sink;
    private CollectEngine? _engine;
    private Task? _supervisor;
    private CancellationTokenSource _cts = new();
    private CollectionStatus _last = new();
    private RegisteredWaitHandle? _showRegistration;
    private bool _disposed;

    public TrayContext()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _popup = new StatusPopup();

        _config = CollectConfig.Load();
        _log = new RollingFileLog(RollingFileLog.DefaultDir, _config?.LogMaxBytes ?? 1_048_576, _config?.LogMaxFiles ?? 3);

        _pauseResume = new ToolStripMenuItem("Pause", null, (_, _) => TogglePause());
        var menu = new ContextMenuStrip();
        menu.Items.Add(_pauseResume);
        menu.Items.Add(new ToolStripMenuItem("Open dashboard", null, (_, _) => OpenUrl(_config?.Endpoint)));
        menu.Items.Add(new ToolStripMenuItem("Open logs", null, (_, _) => OpenLogs()));
        menu.Items.Add(new ToolStripMenuItem("Clear logs", null, (_, _) => _log.Clear()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => OnSettings()));
        menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => Quit()));
        menu.Opening += (_, _) => _pauseResume.Text = _engine?.Status.State == CollectState.Paused ? "Resume" : "Pause";

        _notify = new NotifyIcon
        {
            Icon = TrayIcons.For(CollectState.Idle),
            Text = "OmnisRouter",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notify.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _popup.Toggle(_last, _config?.Endpoint ?? CollectConfig.DefaultEndpoint);
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
    }

    /// <summary>Watch the cross-instance event so a second launch surfaces this instance's popup (FR-005).</summary>
    public void ListenForShowRequests(EventWaitHandle handle) =>
        _showRegistration = ThreadPool.RegisterWaitForSingleObject(
            handle,
            (_, _) => _ui.Post(_ =>
            {
                if (!_disposed)
                {
                    _popup.ShowAt(_last, _config?.Endpoint ?? CollectConfig.DefaultEndpoint);
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

        _sink = new HttpReceiptSink(_config.Endpoint, key);
        _log.Info($"starting: endpoint={_config.Endpoint} root={_config.ResolveRoot()}");
        _supervisor = Task.Run(() => SuperviseAsync(_cts.Token));
    }

    private async Task SuperviseAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var engine = new CollectEngine(_config!.ToEngineOptions(), _sink!, _config.Endpoint, log: _log);
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
        _notify.Icon = TrayIcons.For(status.State);
        _notify.Text = Truncate(StatusFormat.Tooltip(status));
        if (_popup.Visible)
        {
            _popup.Update(status);
        }
    }, null);

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
        }

        base.Dispose(disposing);
    }
}
