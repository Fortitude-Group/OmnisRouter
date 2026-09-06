using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using OmnisRouter.Collect;

namespace OmnisRouter.Tray;

/// <summary>
/// First-run onboarding (and later Settings): capture the dashboard URL and project key, with a
/// button to open OmnisVigil to fetch a key. The key is DPAPI-encrypted on save, never kept in
/// plain text or on a command line (FR-010, FR-011). Laid out with a TableLayoutPanel so rows never
/// overlap; built in code so there are no designer files.
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
        ShowIcon = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.75f);
        ClientSize = new Size(470, 460);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            AutoSize = false,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var intro = new Label
        {
            Text = "Point the watcher at your OmnisVigil dashboard and paste its project key. "
                 + "The key is stored encrypted on this PC.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
            MaximumSize = new Size(430, 0),
        };

        var endpointLabel = MakeLabel("Dashboard URL");
        _endpoint = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 12),
            Text = string.IsNullOrWhiteSpace(existing.Endpoint) ? CollectConfig.DefaultEndpoint : existing.Endpoint,
        };

        var keyLabel = MakeLabel("Project key");
        _key = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
            UseSystemPasswordChar = true,
            PlaceholderText = "ovk_…",
        };

        var connect = new Button
        {
            Text = "Get a key from OmnisVigil  →",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(0, 0, 0, 12),
            Anchor = AnchorStyles.Left,
        };
        connect.Click += (_, _) => OpenUrl(_endpoint.Text.Trim());

        _startAtLogin = new CheckBox
        {
            Text = "Start automatically at login",
            AutoSize = true,
            Checked = true,
            Margin = new Padding(0, 0, 0, 8),
        };

        _error = new Label
        {
            AutoSize = true,
            ForeColor = Color.Firebrick,
            MaximumSize = new Size(430, 0),
            Margin = new Padding(0, 0, 0, 8),
        };

        var save = new Button { Text = "Save && start", AutoSize = true, Padding = new Padding(14, 5, 14, 5), Margin = new Padding(8, 0, 0, 0) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, Padding = new Padding(12, 5, 12, 5), DialogResult = DialogResult.Cancel };
        save.Click += OnSave;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 6, 0, 0),
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        AddRow(layout, intro);
        AddRow(layout, endpointLabel);
        AddRow(layout, _endpoint);
        AddRow(layout, keyLabel);
        AddRow(layout, _key);
        AddRow(layout, connect);
        AddRow(layout, _startAtLogin);
        AddRow(layout, _error);
        AddRow(layout, buttons);
        // A final stretch row keeps everything packed at the top.
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;
    }

    /// <summary>The saved configuration (valid only when <see cref="Form.ShowDialog()"/> returns OK).</summary>
    public CollectConfig Result { get; }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(0, 0, 0, 3),
    };

    private static void AddRow(TableLayoutPanel layout, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(control, 0, layout.RowStyles.Count - 1);
    }

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
