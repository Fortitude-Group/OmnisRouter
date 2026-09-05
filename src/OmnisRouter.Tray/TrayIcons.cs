using System.Drawing;
using System.Runtime.InteropServices;
using OmnisRouter.Collect;

namespace OmnisRouter.Tray;

/// <summary>
/// The tray status icons, drawn once at runtime as coloured dots so there are no binary assets to
/// ship: green = running/healthy, grey = paused/idle, amber = last post failed (contracts/tray-ux.md).
/// </summary>
internal static class TrayIcons
{
    private static readonly Icon Green = Dot(Color.FromArgb(46, 160, 67));
    private static readonly Icon Grey = Dot(Color.FromArgb(140, 148, 158));
    private static readonly Icon Amber = Dot(Color.FromArgb(219, 154, 4));

    public static Icon For(CollectState state) => state switch
    {
        CollectState.Watching or CollectState.Backfilling => Green,
        CollectState.Error => Amber,
        _ => Grey,
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private static Icon Dot(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 12, 12);
        }

        var handle = bmp.GetHicon();
        // Clone into a managed Icon we keep for the app's lifetime, then free the GDI handle.
        using var fromHandle = Icon.FromHandle(handle);
        var icon = (Icon)fromHandle.Clone();
        DestroyIcon(handle);
        return icon;
    }
}
