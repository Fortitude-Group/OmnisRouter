using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using OmnisRouter.Collect;

namespace OmnisRouter.Tray;

/// <summary>
/// The left-click liveness panel: a small borderless window bound to <see cref="CollectionStatus"/>,
/// showing state, last-post time, today's count, and any error, plus a link to the dashboard for the
/// real analytics (contracts/tray-ux.md). It is top-most so the Windows tray overflow flyout can't
/// obscure it, and it persists (no auto-hide on focus loss) so the flyout can be dismissed while the
/// status stays visible; close it with the ✕, Esc, or another click on the tray icon.
/// </summary>
internal sealed class StatusPopup : Form
{
    private readonly Label _state;
    private readonly Label _lastPosted;
    private readonly Label _today;
    private readonly Label _error;
    private readonly LinkLabel _dashboard;
    private string _endpoint = CollectConfig.DefaultEndpoint;

    public StatusPopup()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(32, 34, 37);
        ForeColor = Color.Gainsboro;
        ClientSize = new Size(264, 156);
        Padding = new Padding(14, 10, 14, 12);
        Font = new Font("Segoe UI", 9f);

        var header = new Panel { Dock = DockStyle.Top, Height = 24 };
        var title = new Label { Text = "OmnisRouter", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
        var close = new Label
        {
            Text = "✕",
            Dock = DockStyle.Right,
            Width = 22,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
            ForeColor = Color.Silver,
        };
        close.Click += (_, _) => Hide();
        header.Controls.Add(title);
        header.Controls.Add(close);

        _state = new Label { Dock = DockStyle.Top, Height = 20 };
        _lastPosted = new Label { Dock = DockStyle.Top, Height = 20 };
        _today = new Label { Dock = DockStyle.Top, Height = 20 };
        _error = new Label { Dock = DockStyle.Top, Height = 20, ForeColor = Color.FromArgb(219, 154, 4), AutoEllipsis = true };
        _dashboard = new LinkLabel
        {
            Text = "Full dashboard →",
            Dock = DockStyle.Bottom,
            Height = 20,
            LinkColor = Color.FromArgb(88, 166, 255),
        };
        _dashboard.LinkClicked += (_, _) => OpenDashboard();

        Controls.Add(_error);
        Controls.Add(_today);
        Controls.Add(_lastPosted);
        Controls.Add(_state);
        Controls.Add(header);
        Controls.Add(_dashboard);
    }

    public void ShowAt(CollectionStatus status, string endpoint)
    {
        _endpoint = endpoint;
        Update(status);

        var area = Screen.GetWorkingArea(Cursor.Position);
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
        Show();
        BringToFront();
        Activate();
    }

    public void Toggle(CollectionStatus status, string endpoint)
    {
        if (Visible)
        {
            Hide();
        }
        else
        {
            ShowAt(status, endpoint);
        }
    }

    public void Update(CollectionStatus status)
    {
        _state.Text = StatusFormat.StateLine(status);
        _lastPosted.Text = StatusFormat.LastPostedLine(status);
        _today.Text = StatusFormat.TodayLine(status);
        _error.Text = status.State == CollectState.Error ? status.LastError ?? "" : "";
        _error.Visible = status.State == CollectState.Error;
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
