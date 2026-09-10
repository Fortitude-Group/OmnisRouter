using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using OmnisRouter.Collect;

namespace OmnisRouter.Tray;

/// <summary>
/// First-run onboarding (and later Settings): capture the dashboard URL and project key, with a button
/// to open OmnisVigil to fetch a key. The key is DPAPI-encrypted on save, never kept in plain text or
/// on a command line (FR-010, FR-011). Uses the shared dialog chrome: flat grid, bold field titles, a
/// docked button bar, and content-measured sizing.
/// </summary>
internal sealed class SetupWindow : Form
{
    private const int DialogWidth = 470;

    private readonly TextBox _endpoint;
    private readonly TextBox _key;
    private readonly CheckBox _startAtLogin;
    private readonly Label _error;
    private int _row;

    public SetupWindow(CollectConfig existing)
    {
        Result = existing;

        DialogChrome.Apply(this, $"OmnisRouter {AppVersion.Display} setup");

        var grid = DialogChrome.Grid(new ColumnStyle(SizeType.Percent, 100f));

        AddRow(grid, new Label
        {
            Text = "Point the watcher at your OmnisVigil dashboard and paste its project key. The key is "
                 + "stored encrypted on this PC.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
            MaximumSize = new Size(DialogWidth - 28, 0),
        });

        AddRow(grid, DialogChrome.Title("Dashboard URL"));
        _endpoint = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 2, 0, 10),
            Text = string.IsNullOrWhiteSpace(existing.Endpoint) ? CollectConfig.DefaultEndpoint : existing.Endpoint,
        };
        AddRow(grid, _endpoint);

        AddRow(grid, DialogChrome.Title("Project key"));
        _key = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, UseSystemPasswordChar = true, PlaceholderText = "ovk_…", Margin = new Padding(0, 2, 0, 8) };
        AddRow(grid, _key);

        var getKey = new Button { Text = "Get a key from OmnisVigil", AutoSize = true, MinimumSize = new Size(0, 26), Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(8, 2, 8, 2) };
        getKey.Click += (_, _) => OpenUrl(_endpoint.Text.Trim());
        AddRow(grid, getKey);

        _startAtLogin = new CheckBox { Text = "Start automatically at login", AutoSize = true, Checked = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 0, 4) };
        AddRow(grid, _startAtLogin);

        _error = new Label { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(DialogWidth - 28, 0), Margin = new Padding(0, 6, 0, 0) };
        AddRow(grid, _error);

        var save = DialogChrome.Button("Save && start");
        var cancel = DialogChrome.Button("Cancel", DialogResult.Cancel);
        save.Click += OnSave;
        DialogChrome.Compose(this, DialogWidth, grid, DialogChrome.ButtonBar(save, cancel));
        AcceptButton = save;
        CancelButton = cancel;
    }

    /// <summary>The saved configuration (valid only when <see cref="Form.ShowDialog()"/> returns OK).</summary>
    public CollectConfig Result { get; }

    private void AddRow(TableLayoutPanel grid, Control control)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(control, 0, _row);
        _row++;
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
