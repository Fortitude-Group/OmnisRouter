using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using OmnisRouter.Collect;

namespace OmnisRouter.Tray;

/// <summary>
/// First-run onboarding (and later Settings): capture the dashboard URL and project key, with a
/// button to open OmnisVigil to fetch a key. The key is DPAPI-encrypted on save, never kept in
/// plain text or on a command line (FR-010, FR-011). Built in code so there are no designer files.
/// </summary>
internal sealed class SetupWindow : Form
{
    private readonly TextBox _endpoint;
    private readonly TextBox _key;
    private readonly CheckBox _startAtLogin;
    private readonly Label _error;

    public SetupWindow(CollectConfig existing)
    {
        Result = existing;

        Text = "OmnisRouter setup";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(440, 260);
        Padding = new Padding(16);
        Font = new Font("Segoe UI", 9f);

        var intro = new Label
        {
            Text = "Point the watcher at your OmnisVigil dashboard and paste its project key.",
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 34,
        };

        var endpointLabel = new Label { Text = "Dashboard URL", Dock = DockStyle.Top, Height = 20 };
        _endpoint = new TextBox
        {
            Dock = DockStyle.Top,
            Text = string.IsNullOrWhiteSpace(existing.Endpoint) ? CollectConfig.DefaultEndpoint : existing.Endpoint,
        };

        var keyLabel = new Label { Text = "Project key", Dock = DockStyle.Top, Height = 20, Margin = new Padding(0, 8, 0, 0) };
        _key = new TextBox { Dock = DockStyle.Top, UseSystemPasswordChar = true, PlaceholderText = "ovk_…" };

        var connect = new Button { Text = "Get a key from OmnisVigil →", Dock = DockStyle.Top, Height = 30, Margin = new Padding(0, 8, 0, 0) };
        connect.Click += (_, _) => OpenUrl(_endpoint.Text.Trim());

        _startAtLogin = new CheckBox { Text = "Start automatically at login", Dock = DockStyle.Top, Height = 24, Checked = true };

        _error = new Label { Dock = DockStyle.Top, Height = 24, ForeColor = Color.Firebrick, AutoSize = false };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40 };
        var save = new Button { Text = "Save", Width = 90, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
        save.Click += OnSave;
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        // Docked controls add in reverse visual order.
        Controls.Add(_error);
        Controls.Add(_startAtLogin);
        Controls.Add(connect);
        Controls.Add(_key);
        Controls.Add(keyLabel);
        Controls.Add(_endpoint);
        Controls.Add(endpointLabel);
        Controls.Add(intro);
        Controls.Add(buttons);

        AcceptButton = save;
        CancelButton = cancel;
    }

    /// <summary>The saved configuration (valid only when <see cref="Form.ShowDialog()"/> returns OK).</summary>
    public CollectConfig Result { get; }

    private void OnSave(object? sender, EventArgs e)
    {
        var endpoint = _endpoint.Text.Trim().TrimEnd('/');
        var key = _key.Text.Trim();

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _error.Text = "Enter a valid http(s) dashboard URL.";
            return;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            _error.Text = "Paste your project key.";
            return;
        }

        Result.Endpoint = endpoint;
        try
        {
            Result.SetKey(key);
        }
        catch (PlatformNotSupportedException)
        {
            _error.Text = "Secure key storage is only available on Windows.";
            return;
        }

        Result.Save();

        var exe = Environment.ProcessPath;
        if (exe is not null)
        {
            if (_startAtLogin.Checked)
            {
                LoginTask.Register(exe);
            }
            else
            {
                LoginTask.Unregister();
            }
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            url = CollectConfig.DefaultEndpoint;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }
}
