using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using OmnisRouter.ClientLink;
using OmnisRouter.LocalProxy;

namespace OmnisRouter.Tray;

/// <summary>
/// Connect a coding tool to the local router with one click, and revert it (US3). Reimplements the
/// npm installer's write wiring in C# and adds the revert the CLI lacks. Claude Code and Codex are
/// written to disk (with a backup) and reverted exactly from the state captured at connect; Cursor is
/// show-only, displaying the base URL and token to paste into its settings, with copy buttons.
/// Connecting is gated until the router is Running; Revert stays available whenever a client is wired.
///
/// Laid out as one flat four-column grid (caption, content, action, revert). Nested auto-sizing
/// containers (GroupBox, nested TableLayoutPanels) under-measure their height in WinForms and overflow
/// their bounds, so every control goes directly into this grid, and the window height is taken from
/// the grid's actual laid-out height with the Close button docked below it.
/// </summary>
internal sealed class ConnectWindow : Form
{
    private const int DialogWidth = 520;
    private static readonly Font TitleFont = new("Segoe UI", 9.75f, FontStyle.Bold);

    private readonly RouterController _router;
    private readonly TableLayoutPanel _grid;
    private readonly FlowLayoutPanel _buttonBar;
    private readonly Label _notReady;
    private readonly Label _error;
    private readonly List<ClientRow> _rows = [];
    private int _row;

    public ConnectWindow(RouterController router)
    {
        _router = router;

        Text = "OmnisRouter — connect an app";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);

        _grid = new TableLayoutPanel { Dock = DockStyle.Top, Padding = new Padding(14), ColumnCount = 4, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));           // value caption
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));      // content
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));           // action button
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));           // revert / copy button

        AddFull(new Label
        {
            Text = "Point a coding tool at your local router. Claude Code and Codex are set up for you "
                 + "(with a backup, and Revert puts them back). Cursor has no safe config file, so copy "
                 + "its values into Cursor Settings > Models.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
            MaximumSize = new Size(DialogWidth - 28, 0),
        });

        _notReady = new Label { Text = "Start the router first. Connecting is disabled until it is ready.", AutoSize = true, ForeColor = Color.Firebrick, Margin = new Padding(0, 0, 0, 6) };
        AddFull(_notReady);

        var first = true;
        foreach (var link in ClientDetection.All())
        {
            AddApp(link, first);
            first = false;
        }

        _error = new Label { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(DialogWidth - 28, 0), Margin = new Padding(0, 6, 0, 0) };
        AddFull(_error);

        var close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(84, 26), DialogResult = DialogResult.OK, Margin = new Padding(0) };
        _buttonBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14, 10, 14, 14) };
        _buttonBar.Controls.Add(close);

        Controls.Add(_buttonBar);
        Controls.Add(_grid);
        CancelButton = close;

        // Size to the grid's actual laid-out height (GetPreferredSize under-reports here). The Close
        // bar is docked to the bottom, so it is never part of the measured content and can't be clipped.
        ClientSize = new Size(DialogWidth, 400);
        Load += (_, _) =>
        {
            ClientSize = new Size(DialogWidth, _grid.Height + _buttonBar.Height);
            CenterToScreen();
        };

        _router.StatusChanged += OnRouterStatusChanged;
        FormClosed += (_, _) => _router.StatusChanged -= OnRouterStatusChanged;

        RefreshRows();
    }

    private void AddApp(IClientLink link, bool first)
    {
        var (title, detail) = Describe(link.Kind);

        if (!first)
        {
            AddFull(new Panel { Height = 1, Dock = DockStyle.Top, BackColor = SystemColors.ControlLight, Margin = new Padding(0, 10, 0, 10) });
        }

        // Title (spanning the caption + content columns) with the action buttons in the last two columns.
        var name = new Label { Text = title, AutoSize = true, Font = TitleFont, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) };
        var action = new Button { AutoSize = true, MinimumSize = new Size(92, 26), Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, 6, 0) };
        var revert = new Button { Text = "Revert", AutoSize = true, MinimumSize = new Size(80, 26), Anchor = AnchorStyles.Right, Margin = new Padding(0) };
        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _grid.Controls.Add(name, 0, _row);
        _grid.SetColumnSpan(name, 2);
        _grid.Controls.Add(action, 2, _row);
        _grid.Controls.Add(revert, 3, _row);
        _row++;

        AddFull(new Label { Text = detail, AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(DialogWidth - 28, 0), Margin = new Padding(0, 3, 0, 0) });

        var status = new Label { Text = "—", AutoSize = true, Margin = new Padding(0, 3, 0, 0) };
        AddFull(status);

        var row = new ClientRow(link, status, action, revert);

        // Cursor is show-only: display the values to paste, with copy buttons, instead of writing.
        if (link.ConfigPath is null)
        {
            row.CursorBaseUrl = AddValueRow("Base URL");
            row.CursorApiKey = AddValueRow("API key");
        }

        action.Click += (_, _) => OnConnect(row);
        revert.Click += (_, _) => OnRevert(row);
        _rows.Add(row);
    }

    private TextBox AddValueRow(string label)
    {
        var caption = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) };
        var box = new TextBox { ReadOnly = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 6, 6, 0) };
        var copy = new Button { Text = "Copy", AutoSize = true, MinimumSize = new Size(64, 24), Anchor = AnchorStyles.Right, Margin = new Padding(0, 5, 0, 0) };
        copy.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(box.Text))
            {
                Clipboard.SetText(box.Text);
            }
        };

        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _grid.Controls.Add(caption, 0, _row);
        _grid.Controls.Add(box, 1, _row);
        _grid.SetColumnSpan(box, 2);
        _grid.Controls.Add(copy, 3, _row);
        _row++;
        return box;
    }

    private void AddFull(Control control)
    {
        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _grid.Controls.Add(control, 0, _row);
        _grid.SetColumnSpan(control, 4);
        _row++;
    }

    private static (string Title, string Detail) Describe(ClientKind kind) => kind switch
    {
        ClientKind.ClaudeCode => ("Claude Code", "Sets ANTHROPIC_BASE_URL and ANTHROPIC_AUTH_TOKEN in ~/.claude/settings.json."),
        ClientKind.Codex => ("Codex", "Adds a model-providers block to ~/.codex/config.toml and sets OMNISROUTER_API_KEY."),
        ClientKind.Cursor => ("Cursor", "No safe config file, so paste these into Cursor Settings > Models."),
        _ => (kind.ToString(), ""),
    };

    private bool IsReady => _router.Status.State == RouterProcessState.Running && _router.Token is not null;

    private void OnRouterStatusChanged(object? sender, RouterStatus status) => RefreshRows();

    private void OnConnect(ClientRow row)
    {
        _error.Text = "";
        try
        {
            var record = _router.ConnectClient(row.Link);
            if (record.BackupPath is not null)
            {
                _error.ForeColor = Color.SeaGreen;
                _error.Text = $"Connected {Describe(row.Link.Kind).Title}. Backed up the previous config to {record.BackupPath}.";
            }
        }
        catch (ClientLinkException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowError(ex.Message);
        }

        RefreshRows();
    }

    private void OnRevert(ClientRow row)
    {
        _error.Text = "";
        try
        {
            _router.RevertClient(row.Link);
            if (row.Link.ConfigPath is null)
            {
                MessageBox.Show(
                    "Remove the OmnisRouter base URL and API key from Cursor Settings > Models to finish reverting.",
                    "Revert Cursor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(ex.Message);
        }

        RefreshRows();
    }

    private void ShowError(string message)
    {
        _error.ForeColor = Color.Firebrick;
        _error.Text = message;
    }

    private void RefreshRows()
    {
        var ready = IsReady;
        _notReady.Visible = !ready;

        foreach (var row in _rows)
        {
            var connected = _router.IsConnected(row.Link.Kind);
            var installed = row.Link.IsInstalled;

            row.Status.Text = connected
                ? "Connected"
                : installed ? "Detected, not connected" : "Not detected";
            row.Status.ForeColor = connected ? Color.SeaGreen : Color.DimGray;

            row.Action.Text = row.Link.ConfigPath is null ? "Mark connected" : "Connect";
            row.Action.Enabled = ready && !connected;
            // Revert is a local config rewrite that needs no running router, so it stays available
            // whenever a client is connected, including when the router has failed, which is exactly
            // when a stranded client needs putting back.
            row.Revert.Enabled = connected;

            if (row.CursorBaseUrl is not null && row.CursorApiKey is not null)
            {
                // Fill the show-only values from the pure transform (single source of truth).
                if (ready)
                {
                    var values = row.Link.Connect(_router.Root, _router.Token!, null).DisplayValues;
                    row.CursorBaseUrl.Text = values.TryGetValue("OPENAI_BASE_URL", out var url) ? url : "";
                    row.CursorApiKey.Text = values.TryGetValue("OPENAI_API_KEY", out var key) ? key : "";
                }
                else
                {
                    row.CursorBaseUrl.Text = "";
                    row.CursorApiKey.Text = "";
                }
            }
        }
    }

    private sealed class ClientRow(IClientLink link, Label status, Button action, Button revert)
    {
        public IClientLink Link { get; } = link;

        public Label Status { get; } = status;

        public Button Action { get; } = action;

        public Button Revert { get; } = revert;

        public TextBox? CursorBaseUrl { get; set; }

        public TextBox? CursorApiKey { get; set; }
    }
}
