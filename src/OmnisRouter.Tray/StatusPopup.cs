using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using OmnisRouter.Collect;

namespace OmnisRouter.Tray;

/// <summary>
/// The left-click liveness panel: a small borderless window bound to <see cref="CollectionStatus"/>,
/// showing state, last-post time, today's count, and any error, plus a link to the dashboard for the
/// real analytics (contracts/tray-ux.md). It ships liveness-only; richer fields can bind later.
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
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(32, 34, 37);
        ForeColor = Color.Gainsboro;
        ClientSize = new Size(260, 132);
        Padding = new Padding(14, 12, 14, 12);
        Font = new Font("Segoe UI", 9f);

        var title = new Label { Text = "OmnisRouter", Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
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
        Controls.Add(title);
        Controls.Add(_dashboard);
    }

    protected override bool ShowWithoutActivation => false;

    public void ShowAt(CollectionStatus status, string endpoint)
    {
        _endpoint = endpoint;
        Update(status);

        var area = Screen.GetWorkingArea(Cursor.Position);
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
        Show();
        Activate();
    }

    public void Update(CollectionStatus status)
    {
        _state.Text = StatusFormat.StateLine(status);
        _lastPosted.Text = StatusFormat.LastPostedLine(status);
        _today.Text = StatusFormat.TodayLine(status);
        _error.Text = status.State == CollectState.Error ? status.LastError ?? "" : "";
        _error.Visible = status.State == CollectState.Error;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Hide();   // dismiss when focus leaves
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
