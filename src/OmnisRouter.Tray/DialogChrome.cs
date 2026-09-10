using System.Drawing;
using System.Windows.Forms;

namespace OmnisRouter.Tray;

/// <summary>
/// Shared look and sizing for the tray's dialogs, so they stay consistent: one body font, bold static
/// section titles, a docked bottom button bar, and a window sized to the content's ACTUAL laid-out
/// height. Nested auto-sizing containers (GroupBox, nested TableLayoutPanels) under-measure their
/// height in WinForms and overflow, so every dialog uses a single flat grid and takes its height from
/// <see cref="TableLayoutPanel.Height"/> rather than GetPreferredSize.
/// </summary>
internal static class DialogChrome
{
    public static readonly Font BodyFont = new("Segoe UI", 9f);
    public static readonly Font TitleFont = new("Segoe UI", 9.75f, FontStyle.Bold);

    /// <summary>Apply the standard chrome (fixed dialog, centred, no icon, DPI-aware, body font).</summary>
    public static void Apply(Form f, string title)
    {
        f.Text = title;
        f.FormBorderStyle = FormBorderStyle.FixedDialog;
        f.StartPosition = FormStartPosition.CenterScreen;
        f.MaximizeBox = false;
        f.MinimizeBox = false;
        f.ShowIcon = false;
        f.AutoScaleMode = AutoScaleMode.Dpi;
        f.Font = BodyFont;
    }

    /// <summary>A flat content grid docked to the top, auto-sizing to its rows.</summary>
    public static TableLayoutPanel Grid(params ColumnStyle[] columnStyles)
    {
        var g = new TableLayoutPanel { Dock = DockStyle.Top, Padding = new Padding(14), ColumnCount = columnStyles.Length, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        foreach (var cs in columnStyles)
        {
            g.ColumnStyles.Add(cs);
        }

        return g;
    }

    /// <summary>A bold static section title.</summary>
    public static Label Title(string text) =>
        new() { Text = text, AutoSize = true, Font = TitleFont, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) };

    /// <summary>The standard right-aligned bottom button bar. Pass buttons right-to-left (the primary
    /// action first).</summary>
    public static FlowLayoutPanel ButtonBar(params Button[] rightToLeft)
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14, 10, 14, 14) };
        foreach (var b in rightToLeft)
        {
            bar.Controls.Add(b);
        }

        return bar;
    }

    /// <summary>A standard dialog button.</summary>
    public static Button Button(string text, DialogResult result = DialogResult.None) =>
        new() { Text = text, AutoSize = true, MinimumSize = new Size(84, 26), DialogResult = result, Margin = new Padding(0, 0, 8, 0) };

    /// <summary>Dock the grid above the button bar and size the window to the grid's real height once it
    /// has laid out (GetPreferredSize under-reports here), then keep it centred.</summary>
    public static void Compose(Form f, int width, TableLayoutPanel grid, FlowLayoutPanel bottomBar)
    {
        f.Controls.Add(bottomBar);
        f.Controls.Add(grid);
        f.ClientSize = new Size(width, 400);
        f.Load += (_, _) =>
        {
            f.ClientSize = new Size(width, grid.Height + bottomBar.Height);
            var area = Screen.GetWorkingArea(f);
            f.Location = new Point(area.Left + ((area.Width - f.Width) / 2), area.Top + ((area.Height - f.Height) / 2));
        };
    }
}
