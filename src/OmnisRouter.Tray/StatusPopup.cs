using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using OmnisRouter.Collect;
using OmnisRouter.LocalProxy;

namespace OmnisRouter.Tray;

/// <summary>
/// The left-click liveness panel: a small borderless window bound to <see cref="CollectionStatus"/>,
/// showing state, last-post time, today's count, and any error, plus a link to the dashboard for the
/// real analytics (contracts/tray-ux.md). It is top-most so the Windows tray overflow flyout can't
/// obscure it, and it persists (no auto-hide on focus loss) so the flyout can be dismissed while the
/// status stays visible; close it with the ✕, Esc, or another click on the tray icon.
///
/// Rows auto-size to their text (descenders included) and the window sizes to its content, so lines
/// are never cropped and the panel grows when the error line appears.
/// </summary>
internal sealed class StatusPopup : Form
{
    private const int FixedWidth = 268;

    private static readonly Color Amber = Color.FromArgb(219, 154, 4);

    private readonly TableLayoutPanel _layout;
    private readonly Label _mode;
    private readonly Label _state;
    private readonly Label _lastPosted;
    private readonly Label _today;
    private string _endpoint = CollectConfig.DefaultEndpoint;

    public StatusPopup()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(32, 34, 37);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9f);

        _layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(14, 10, 14, 12),
            AutoSize = false,
        };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var header = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 4), Dock = DockStyle.Fill };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = new Label { Text = $"OmnisRouter  {AppVersion.Display}", AutoSize = true, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Margin = new Padding(0) };
        var close = new Label { Text = "✕", AutoSize = true, Cursor = Cursors.Hand, ForeColor = Color.Silver, Margin = new Padding(6, 1, 0, 0) };
        close.Click += (_, _) => Hide();
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(close, 1, 0);

        _mode = MakeLine();
        _mode.Font = new Font(Font, FontStyle.Bold);
        _mode.Margin = new Padding(0, 0, 0, 6);
        _state = MakeLine();
        _lastPosted = MakeLine();
        _today = MakeLine();

        var dashboard = new LinkLabel
        {
            Text = "Full dashboard →",
            AutoSize = true,
            LinkColor = Color.FromArgb(88, 166, 255),
            Margin = new Padding(0, 8, 0, 0),
        };
        dashboard.LinkClicked += (_, _) => OpenDashboard();

        AddRow(header);
        AddRow(_mode);
        AddRow(_state);
        AddRow(_lastPosted);
        AddRow(_today);
        AddRow(dashboard);

        Controls.Add(_layout);
        ClientSize = new Size(FixedWidth, 160);
    }

    private static Label MakeLine() => new()
    {
        AutoSize = true,
        AutoEllipsis = true,
        Margin = new Padding(0, 2, 0, 0),
        MaximumSize = new Size(FixedWidth - 28, 0),
    };

    private void AddRow(Control control)
    {
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.Controls.Add(control, 0, _layout.RowStyles.Count - 1);
    }

    public void ShowAt(CollectionStatus status, RouterStatus router, string endpoint, bool hasProviderKeys, int connectedClients)
    {
        _endpoint = endpoint;
        Update(status, router, hasProviderKeys, connectedClients);

        var area = Screen.GetWorkingArea(Cursor.Position);
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
        Show();
        BringToFront();
        Activate();
    }

    public void Toggle(CollectionStatus status, RouterStatus router, string endpoint, bool hasProviderKeys, int connectedClients)
    {
        if (Visible)
        {
            Hide();
        }
        else
        {
            ShowAt(status, router, endpoint, hasProviderKeys, connectedClients);
        }
    }

    public void Update(CollectionStatus status, RouterStatus router, bool hasProviderKeys, int connectedClients)
    {
        _mode.Text = RouterStatusText.ModeLine(status.State, router, hasProviderKeys, connectedClients);
        _mode.ForeColor = router.State == RouterProcessState.Error ? Amber : ForeColor;
        _state.Text = StatusFormat.StateLine(status);
        _state.ForeColor = status.State == CollectState.Error ? Amber : ForeColor;
        _lastPosted.Text = StatusFormat.LastPostedLine(status);
        _today.Text = StatusFormat.TodayLine(status);

        // Size the window to its content so nothing is cropped and it grows when a line wraps.
        var height = _layout.GetPreferredSize(new Size(FixedWidth, 0)).Height;
        ClientSize = new Size(FixedWidth, height);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            Hide();
        }
    }

    private void OpenDashboard()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_endpoint) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }
}
