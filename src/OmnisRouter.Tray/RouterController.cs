using System.Windows.Forms;
using OmnisRouter.LocalProxy;

namespace OmnisRouter.Tray;

/// <summary>
/// Ties the tray to the local router proxy (feature 003, US1/US2): loads/saves <see cref="RouterSettings"/>,
/// owns the <see cref="RouterSupervisor"/> and <see cref="RouterManagementClient"/>, shows the
/// first-enable billing confirmation (FR-007), and opens the keys window when routing first comes up
/// with no provider keys set. Every status change is marshalled onto the UI thread via the same
/// <see cref="SynchronizationContext"/> TrayContext uses for the collect engine (TrayContext.Apply),
/// so both features update the icon/popup through one mechanism.
/// </summary>
internal sealed class RouterController : IDisposable
{
    private readonly SynchronizationContext _ui;
    private readonly ISecretProtector _protector = new DpapiSecretProtector();
    private readonly RouterPaths _paths = new();
    private readonly string _settingsPath = RouterSettings.DefaultPath();

    private readonly RouterSettings _settings;
    private HttpClient? _http;
    private RouterSupervisor? _supervisor;
    private bool _keysPromptedThisStart;
    private bool _disposed;

    public RouterController(SynchronizationContext ui)
    {
        _ui = ui;
        _settings = RouterSettings.Load(_settingsPath, _protector);
    }

    /// <summary>Raised on the UI thread whenever the router's supervised state changes.</summary>
    public event EventHandler<RouterStatus>? StatusChanged;

    public RouterStatus Status { get; private set; } = new(RouterProcessState.Off, 0);

    /// <summary>Whether the toggle is on (independent of whether the process has reached Running).</summary>
    public bool Enabled => _settings.Enabled;

    /// <summary>The management client for the current run, or null before the router has ever started.</summary>
    public RouterManagementClient? ManagementClient { get; private set; }

    /// <summary>Restore the proxy's last-known on/off state at tray startup (FR-004, US1 scenario 4).
    /// Confirmation was already given the first time the toggle was turned on, so this restores
    /// quietly rather than asking again.</summary>
    public async Task RestoreAtStartupAsync()
    {
        if (_settings.Enabled)
        {
            await StartRouterAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Flip the tray toggle. Shows the first-enable billing confirmation before the first
    /// start; declining leaves the proxy off.</summary>
    public async Task ToggleAsync()
    {
        if (_settings.Enabled)
        {
            await DisableAsync().ConfigureAwait(true);
            return;
        }

        if (!_settings.RoutingConfirmed)
        {
            var confirmed = MessageBox.Show(
                "Turning this on routes your coding tool traffic through your own provider API keys "
                    + "(Anthropic, OpenAI, Gemini or OpenRouter). That traffic is billed per token, "
                    + "directly by the provider — a different model from a flat-rate subscription.\n\n"
                    + "Turn on local routing?",
                "Local router proxy",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button2);

            if (confirmed != DialogResult.Yes)
            {
                return;
            }

            _settings.RoutingConfirmed = true;
            SaveSettings();
        }

        _settings.Enabled = true;
        SaveSettings();
        await StartRouterAsync().ConfigureAwait(true);
    }

    private async Task DisableAsync()
    {
        if (_supervisor is not null)
        {
            await _supervisor.StopAsync().ConfigureAwait(true);
        }

        _settings.Enabled = false;
        SaveSettings();
    }

    private async Task StartRouterAsync()
    {
        var token = _settings.GetToken(_protector);
        if (token is null)
        {
            token = RouterToken.Generate();
            _settings.SetToken(token, _protector);
            SaveSettings();
        }

        _http?.Dispose();
        var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_settings.Port}") };
        _http = http;
        var mgmt = new RouterManagementClient(http, token);
        ManagementClient = mgmt;

        _keysPromptedThisStart = false;
        var supervisor = new RouterSupervisor(new SystemProcessLauncher(), ct => mgmt.IsReadyAsync(ct), _paths);
        supervisor.StatusChanged += OnSupervisorStatusChanged;
        _supervisor = supervisor;

        await supervisor.StartAsync(_settings.Port, token).ConfigureAwait(true);
    }

    private void OnSupervisorStatusChanged(object? sender, RouterStatus status) => _ui.Post(_ =>
    {
        if (_disposed)
        {
            return;
        }

        Status = status;
        StatusChanged?.Invoke(this, status);

        if (status.State == RouterProcessState.Running && !_keysPromptedThisStart)
        {
            _keysPromptedThisStart = true;
            PromptForFirstKeyIfNone();
        }
    }, null);

    // US2 scenario 1: first time routing comes up with no provider keys set, open the keys window.
    private async void PromptForFirstKeyIfNone()
    {
        var mgmt = ManagementClient;
        if (mgmt is null)
        {
            return;
        }

        try
        {
            var keys = await mgmt.ListKeysAsync().ConfigureAwait(true);
            if (keys.Count == 0)
            {
                using var win = new KeysWindow(this);
                win.ShowDialog();
            }
        }
        catch (HttpRequestException)
        {
            // Not actually reachable yet despite readiness passing; the keys window's own gate
            // covers this if the user opens it by hand.
        }
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Save(_settingsPath);
        }
        catch (IOException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_supervisor is not null)
        {
            _supervisor.StatusChanged -= OnSupervisorStatusChanged;
        }

        _http?.Dispose();
    }
}
