using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Auxer;

/// <summary>Program icons, read from the exe at the exact pixel size and cached (read once per app).</summary>
static class AppIcons
{
    static readonly Dictionary<string, Bitmap> cache = new(StringComparer.OrdinalIgnoreCase);

    /// <returns>The icon, or null when the program has none we can read.</returns>
    public static Bitmap Get(string exePath, int size)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        var key = exePath + "|" + size;
        if (cache.TryGetValue(key, out var cached)) return cached;

        Bitmap bmp = null;
        try
        {
            var handles = new IntPtr[1];
            var ids = new uint[1];
            // PrivateExtractIcons picks the best image in the exe and scales it to exactly size x size.
            uint n = PrivateExtractIcons(exePath, 0, size, size, handles, ids, 1, 0);
            if (n != 0 && n != uint.MaxValue && handles[0] != IntPtr.Zero)
            {
                using (var icon = Icon.FromHandle(handles[0])) bmp = icon.ToBitmap();
                DestroyIcon(handles[0]);
            }
            else
            {
                using var icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon != null) bmp = new Bitmap(icon.ToBitmap(), size, size);
            }
        }
        catch { bmp = null; }

        cache[key] = bmp;
        return bmp;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint count, uint flags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);
}

/// <summary>Draws an app's icon centred; falls back to a tile with the app's first letter.</summary>
sealed class AppIconView : PaintedControl
{
    Bitmap image;
    string name = "";
    bool dim;

    public int IconSize { get; set; } = Theme.S(24);

    public void Set(string exePath, string appName, bool isDim)
    {
        var img = AppIcons.Get(exePath, IconSize);
        if (ReferenceEquals(img, image) && appName == name && isDim == dim) return;
        image = img;
        name = appName ?? "";
        dim = isDim;
        Theme.SetTip(this, Theme.Pretty(name));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = new Rectangle((Width - IconSize) / 2, (Height - IconSize) / 2, IconSize, IconSize);
        if (image != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (!dim) g.DrawImage(image, r);
            else
            {
                // Muted: draw the icon faded.
                using var attrs = new System.Drawing.Imaging.ImageAttributes();
                attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.4f });
                g.DrawImage(image, r, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attrs);
            }
            return;
        }
        Theme.FillRound(g, Theme.SurfacePressed, r, S(6));
        var letter = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?";
        TextRenderer.DrawText(g, letter, Theme.Bold, r, dim ? Theme.TextFaint : Theme.TextDim, Theme.Centered);
    }
}
