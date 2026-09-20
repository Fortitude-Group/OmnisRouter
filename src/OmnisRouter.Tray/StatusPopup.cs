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
    private readonly Label _cacheWaste;
    private readonly Label _cacheRecovered;
    private readonly Label _cacheBasis;
    private readonly Label _fixLabel;
    private readonly CheckBox _fixLine;
    private readonly CheckBox _fixTrailing;
    private readonly CheckBox _fixTool;
    private readonly Label _fixManaged;
    private readonly Label _fixNote;
    private bool _suppressFixEvents;
    private string _endpoint = CollectConfig.DefaultEndpoint;

    /// <summary>Raised when the user toggles a fix; the argument is the wire names now enabled. The tray
    /// wires this to the router's fixes endpoint. Null until wired. Not a designer-serialised property.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action<IReadOnlyList<string>>? FixesChanged { get; set; }

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

        _cacheWaste = MakeLine();
        _cacheWaste.Margin = new Padding(0, 8, 0, 0);
        _cacheWaste.Text = "Cache hygiene: nothing measured yet";
        _cacheRecovered = MakeLine();
        _cacheRecovered.Visible = false;
        _cacheBasis = MakeLine();
        _cacheBasis.ForeColor = Color.Gray;
        _cacheBasis.Font = new Font(Font.FontFamily, 8f);
        _cacheBasis.Visible = false;

        _fixLabel = MakeLine();
        _fixLabel.Text = "Recover:";
        _fixLabel.Margin = new Padding(0, 8, 0, 0);
        _fixLabel.Visible = false;
        _fixLine = MakeFixToggle("Line endings");
        _fixTrailing = MakeFixToggle("Trailing spaces");
        _fixTool = MakeFixToggle("Tool order");
        var fixRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            MaximumSize = new Size(FixedWidth - 28, 0),
            Margin = new Padding(0, 0, 0, 0),
            Dock = DockStyle.Fill,
        };
        fixRow.Controls.Add(_fixLine);
        fixRow.Controls.Add(_fixTrailing);
        fixRow.Controls.Add(_fixTool);
        // Read-only, light-text summary shown instead of the checkboxes when an OmnisVigil policy governs
        // the fixes: a disabled checkbox renders dark-grey text that is invisible on this dark popup.
        _fixManaged = MakeLine();
        _fixManaged.Visible = false;
        _fixNote = MakeLine();
        _fixNote.ForeColor = Amber;
        _fixNote.Visible = false;

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
        AddRow(_cacheWaste);
        AddRow(_cacheRecovered);
        AddRow(_cacheBasis);
        AddRow(_fixLabel);
        AddRow(fixRow);
        AddRow(_fixManaged);
        AddRow(_fixNote);
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

    private CheckBox MakeFixToggle(string text)
    {
        var box = new CheckBox
        {
            Text = text,
            AutoSize = true,
            ForeColor = ForeColor,
            FlatStyle = FlatStyle.Standard,
            Margin = new Padding(0, 0, 8, 0),
            Visible = false,
        };
        box.CheckedChanged += (_, _) => OnFixToggled();
        return box;
    }

    private void OnFixToggled()
    {
        if (_suppressFixEvents)
        {
            return;   // programmatic set from SetFixState, not a user action
        }

        var enabled = new List<string>();
        if (_fixLine.Checked)
        {
            enabled.Add("line_ending");
        }

        if (_fixTrailing.Checked)
        {
            enabled.Add("trailing_whitespace");
        }

        if (_fixTool.Checked)
        {
            enabled.Add("tool_ordering");
        }

        FixesChanged?.Invoke(enabled);
    }

    /// <summary>
    /// Render the collect-mode cache line (US3): the observed cache-write shadow cost, an estimate, with
    /// no cause classification and no fix toggles (collect mode is content-free and cannot recover).
    /// </summary>
    public void SetCollectCacheHygiene(CollectionStatus status)
    {
        _cacheWaste.Text = StatusFormat.CacheLine(status);
        _cacheRecovered.Visible = false;
        _cacheBasis.Text = "shadow USD from observed cache writes; collect mode cannot classify cause";
        _cacheBasis.Visible = true;
        SetFixState(null);   // no fix control in collect mode (also calls ResizeToContent)
    }

    /// <summary>Render the fix toggles from the router's fix state (null = router not available: hide them).</summary>
    public void SetFixState(CacheFixStateInfo? state)
    {
        _suppressFixEvents = true;
        try
        {
            var show = state is not null;
            var effective = state?.Effective ?? [];
            var policy = state?.PolicyOverrides == true;

            _fixLabel.Visible = show;

            if (show && policy)
            {
                // Governed by OmnisVigil: show a readable light-text summary, not disabled checkboxes
                // (whose grey text vanishes on the dark popup).
                _fixLine.Visible = _fixTrailing.Visible = _fixTool.Visible = false;
                _fixNote.Visible = false;
                _fixManaged.Visible = true;
                var names = effective.Count == 0
                    ? "none"
                    : string.Join(", ", effective.Select(FriendlyFixName));
                _fixManaged.Text = $"{names} (managed by OmnisVigil)";
            }
            else
            {
                // Editable: enabled checkboxes with the popup's light text.
                _fixManaged.Visible = false;
                _fixNote.Visible = false;
                _fixLine.Checked = effective.Contains("line_ending");
                _fixTrailing.Checked = effective.Contains("trailing_whitespace");
                _fixTool.Checked = effective.Contains("tool_ordering");
                _fixLine.Enabled = _fixTrailing.Enabled = _fixTool.Enabled = show;
                _fixLine.Visible = _fixTrailing.Visible = _fixTool.Visible = show;
            }
        }
        finally
        {
            _suppressFixEvents = false;
        }

        ResizeToContent();
    }

    private static string FriendlyFixName(string wire) => wire switch
    {
        "line_ending" => "line endings",
        "trailing_whitespace" => "trailing spaces",
        "tool_ordering" => "tool order",
        _ => wire,
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

        ResizeToContent();
    }

    /// <summary>Render the cache-hygiene section from the router's summary (null = not measuring).</summary>
    public void SetCacheHygiene(CacheHygieneSummaryInfo? summary)
    {
        var lines = CacheHygieneDisplay.Format(summary);
        _cacheWaste.Text = lines.Primary;
        _cacheRecovered.Text = lines.Secondary ?? string.Empty;
        _cacheRecovered.Visible = lines.Secondary is not null;
        _cacheBasis.Text = lines.Basis ?? string.Empty;
        _cacheBasis.Visible = lines.Basis is not null;
        ResizeToContent();
    }

    // Size the window to its content so nothing is cropped and it grows when a line wraps or appears,
    // then re-anchor to the bottom-right of the working area. Re-anchoring matters because the cache
    // section is filled in AFTER the popup is first placed, so without this the extra height would push
    // the window down over the taskbar and off the bottom of the screen.
    private void ResizeToContent()
    {
        var height = _layout.GetPreferredSize(new Size(FixedWidth, 0)).Height;
        ClientSize = new Size(FixedWidth, height);

        var anchor = IsHandleCreated && Location != Point.Empty ? Bounds : new Rectangle(Cursor.Position, Size);
        var area = Screen.GetWorkingArea(anchor);
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
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
