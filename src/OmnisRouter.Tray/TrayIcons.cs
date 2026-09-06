using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OmnisRouter.Collect;

namespace OmnisRouter.Tray;

/// <summary>
/// The tray status icon: the Fortitude lion mark, tinted by state so health reads at a glance
/// (contracts/tray-ux.md) — green watching/healthy, grey paused/idle, amber a failing post. The lion
/// is one embedded white silhouette recoloured at runtime, so there is a single asset for every state.
/// </summary>
internal static class TrayIcons
{
    private static readonly Bitmap Lion = LoadLion();
    private static readonly Icon Green = Tinted(Color.FromArgb(46, 160, 67));
    private static readonly Icon Grey = Tinted(Color.FromArgb(140, 148, 158));
    private static readonly Icon Amber = Tinted(Color.FromArgb(219, 154, 4));

    public static Icon For(CollectState state) => state switch
    {
        CollectState.Watching or CollectState.Backfilling => Green,
        CollectState.Error => Amber,
        _ => Grey,
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private static Bitmap LoadLion()
    {
        var asm = typeof(TrayIcons).Assembly;
        using var stream = asm.GetManifestResourceStream("OmnisRouter.Tray.assets.lion.png")
            ?? throw new InvalidOperationException("Embedded lion.png not found.");
        return new Bitmap(stream);
    }

    private static Icon Tinted(Color color)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Map the white silhouette to the status colour, keeping its alpha (shape).
            var matrix = new ColorMatrix
            {
                Matrix00 = 0, Matrix11 = 0, Matrix22 = 0, Matrix33 = 1,
                Matrix40 = color.R / 255f, Matrix41 = color.G / 255f, Matrix42 = color.B / 255f,
            };
            using var attrs = new ImageAttributes();
            attrs.SetColorMatrix(matrix);

            var dest = new Rectangle(1, 1, size - 2, size - 2);
            g.DrawImage(Lion, dest, 0, 0, Lion.Width, Lion.Height, GraphicsUnit.Pixel, attrs);
        }

        var handle = bmp.GetHicon();
        using var fromHandle = Icon.FromHandle(handle);
        var icon = (Icon)fromHandle.Clone();
        DestroyIcon(handle);
        return icon;
    }
}
