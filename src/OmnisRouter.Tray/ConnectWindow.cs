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
/// show-only — it displays the base URL and token to paste into Cursor's settings, with copy buttons.
/// Actions are gated until the router is Running, matching the keys window. Built in code, matching
/// KeysWindow/SetupWindow's style; no designer files.
/// </summary>
internal sealed class ConnectWindow : Form
{
    private readonly RouterController _router;
    private readonly Label _notReady;
    private readonly Label _error;
    private readonly List<ClientRow> _rows = [];

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
        Font = new Font("Segoe UI", 9.75f);
        ClientSize = new Size(560, 420);

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
            Text = "Point a coding tool at your local router. Claude Code and Codex are configured for "
                 + "you (a backup is made first, and Revert puts them back exactly). Cursor has no safe "
                 + "config file — copy the values below into Cursor Settings → Models.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
            MaximumSize = new Size(520, 0),
        };

        _notReady = new Label
        {
            Text = "Start the router first — connecting is disabled until it is ready.",
            AutoSize = true,
            ForeColor = Color.Firebrick,
            Margin = new Padding(0, 0, 0, 10),
        };

        AddRow(layout, intro);
        AddRow(layout, _notReady);

        foreach (var link in ClientDetection.All())
        {
            var card = BuildClientCard(link);
            AddRow(layout, card);
        }

        _error = new Label
        {
            AutoSize = true,
            ForeColor = Color.Firebrick,
            MaximumSize = new Size(520, 0),
            Margin = new Padding(0, 4, 0, 8),
        };

        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK, Padding = new Padding(12, 5, 12, 5) };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 6, 0, 0),
        };
        buttons.Controls.Add(close);

        AddRow(layout, _error);
        AddRow(layout, buttons);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        Controls.Add(layout);
        CancelButton = close;

        _router.StatusChanged += OnRouterStatusChanged;
        FormClosed += (_, _) => _router.StatusChanged -= OnRouterStatusChanged;

        RefreshRows();
    }

    private Control BuildClientCard(IClientLink link)
    {
        var (title, detail) = Describe(link.Kind);

        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(10),
            BackColor = SystemColors.ControlLightLight,
            BorderStyle = BorderStyle.FixedSingle,
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var name = new Label { Text = title, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 2, 8, 0) };
        var status = new Label { Text = "—", AutoSize = true, Margin = new Padding(0, 2, 8, 0) };
        var action = new Button { AutoSize = true, Padding = new Padding(10, 3, 10, 3), Margin = new Padding(0, 0, 6, 0) };
        var revert = new Button { Text = "Revert", AutoSize = true, Padding = new Padding(10, 3, 10, 3) };

        card.Controls.Add(name, 0, 0);
        card.Controls.Add(action, 1, 0);
        card.Controls.Add(revert, 2, 0);

        var detailLabel = new Label { Text = detail, AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(520, 0), Margin = new Padding(0, 2, 0, 0) };
        card.Controls.Add(detailLabel, 0, 1);
        card.SetColumnSpan(detailLabel, 3);
        card.Controls.Add(status, 0, 2);
        card.SetColumnSpan(status, 3);

        var row = new ClientRow(link, status, action, revert);

        // Cursor is show-only: display the values to paste, with copy buttons, instead of writing.
        if (link.ConfigPath is null)
        {
            row.CursorBaseUrl = AddCopyableValue(card, "Base URL");
            row.CursorApiKey = AddCopyableValue(card, "API key");
        }

        action.Click += (_, _) => OnConnect(row);
        revert.Click += (_, _) => OnRevert(row);

        _rows.Add(row);
        return card;
    }

    private static TextBox AddCopyableValue(TableLayoutPanel card, string label)
    {
        var caption = new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 8, 0) };
        var box = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Margin = new Padding(0, 4, 6, 0) };
        var copy = new Button { Text = "Copy", AutoSize = true, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(0, 3, 0, 0) };
        copy.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(box.Text))
            {
                Clipboard.SetText(box.Text);
            }
        };

        // Give the caption + value box + copy button their own full-width line inside the card.
        var line = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, Margin = new Padding(0) };
        line.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70f));
        line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        line.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        line.Controls.Add(caption, 0, 0);
        line.Controls.Add(box, 1, 0);
        line.Controls.Add(copy, 2, 0);

        var r = card.RowStyles.Count;
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.Controls.Add(line, 0, r);
        card.SetColumnSpan(line, 3);
        return box;
    }

    private static (string Title, string Detail) Describe(ClientKind kind) => kind switch
    {
        ClientKind.ClaudeCode => ("Claude Code", "Sets ANTHROPIC_BASE_URL and ANTHROPIC_AUTH_TOKEN in ~/.claude/settings.json."),
        ClientKind.Codex => ("Codex", "Adds a model-providers block to ~/.codex/config.toml and sets OMNISROUTER_API_KEY."),
        ClientKind.Cursor => ("Cursor", "No safe config file — paste these into Cursor Settings → Models."),
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
                    "Remove the OmnisRouter base URL and API key from Cursor Settings → Models to finish reverting.",
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
                : installed ? "Detected · not connected" : "Not detected";
            row.Status.ForeColor = connected ? Color.SeaGreen : Color.DimGray;

            row.Action.Text = row.Link.ConfigPath is null ? "Mark connected" : "Connect";
            row.Action.Enabled = ready && !connected;
            // Revert is a local config rewrite that needs no running router, so it stays available
            // whenever a client is connected — including when the router has failed, which is exactly
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

    private static void AddRow(TableLayoutPanel layout, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(control, 0, layout.RowStyles.Count - 1);
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
