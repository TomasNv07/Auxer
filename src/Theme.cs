using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Auxer;

/// <summary>Colors, fonts and drawing helpers: near-black greys with a dark-blue accent.</summary>
static class Theme
{
    public static readonly Color Bg = Rgb(0x15161A);
    public static readonly Color Chrome = Rgb(0x15161A);
    public static readonly Color Divider = Rgb(0x2C2E37);
    public static readonly Color Surface = Rgb(0x1C1D23);
    public static readonly Color SurfaceHover = Rgb(0x24262D);
    public static readonly Color SurfacePressed = Rgb(0x2B2D35);
    public static readonly Color Border = Rgb(0x2A2C34);
    public static readonly Color BorderStrong = Rgb(0x383B46);
    public static readonly Color Field = Rgb(0x101114);
    public static readonly Color Text = Rgb(0xE7E9EE);
    public static readonly Color TextDim = Rgb(0x9298A5);
    public static readonly Color TextFaint = Rgb(0x5E636F);
    public static readonly Color Accent = Rgb(0x6882D6);
    public static readonly Color AccentHover = Rgb(0x7A92E0);
    public static readonly Color AccentPressed = Rgb(0x5A72C2);
    public static readonly Color AccentSoft = Rgb(0x232A45);
    public static readonly Color AccentText = Rgb(0x8FA3EC);
    public static readonly Color Danger = Rgb(0xE5484D);
    public static readonly Color CloseHover = Rgb(0xC42B1C);
    public static readonly Color Success = Rgb(0x3DD68C);
    public static readonly Color Warning = Rgb(0xF5A524);

    public static readonly Font Body = new("Segoe UI", 9.5f);
    public static readonly Font Bold = new("Segoe UI Semibold", 9.5f);
    public static readonly Font Small = new("Segoe UI", 8.5f);
    public static readonly Font Title = new("Segoe UI Semibold", 10.5f);
    public static readonly Font Heading = new("Segoe UI Semibold", 13f);
    public static readonly Font Number = new("Segoe UI Semibold", 11f);

    static readonly float scale = GetDpiForSystem() / 96f;
    static readonly string iconFamily = PickIconFont();
    static readonly Dictionary<float, Font> iconFonts = new();

    public static int S(int v) => (int)Math.Round(v * scale);

    static Color Rgb(int rgb) => Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);

    public static Font Icon(float size)
    {
        if (!iconFonts.TryGetValue(size, out var f)) iconFonts[size] = f = new Font(iconFamily, size);
        return f;
    }

    static string PickIconFont()
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }

    /// <summary>"spotify" → "Spotify" (display only).</summary>
    public static string Pretty(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name[1..];

    // ---------- drawing ----------

    public const TextFormatFlags Centered = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                                            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

    public const TextFormatFlags LeftMiddle = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                                              TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

    public static void Glyph(Graphics g, string glyph, float size, Color color, Rectangle r) =>
        TextRenderer.DrawText(g, glyph, Icon(size), r, color, Centered);

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d < 1) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var p = RoundRect(r, radius);
        using var b = new SolidBrush(c);
        g.FillPath(b, p);
    }

    public static void DrawRound(Graphics g, Color c, RectangleF r, float radius)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var p = RoundRect(new RectangleF(r.X + .5f, r.Y + .5f, r.Width - 1, r.Height - 1), radius);
        using var pen = new Pen(c);
        g.DrawPath(pen, p);
    }

    // ---------- windows ----------

    /// <summary>Dark title-bar bits, rounded corners and a subtle border (Windows 11). Returns false if rounding isn't supported.</summary>
    public static bool StyleWindow(IntPtr hwnd, bool smallCorners = false)
    {
        int on = 1;
        DwmSetWindowAttribute(hwnd, 20, ref on, 4);                 // DWMWA_USE_IMMERSIVE_DARK_MODE
        int border = Border.R | (Border.G << 8) | (Border.B << 16);
        DwmSetWindowAttribute(hwnd, 34, ref border, 4);             // DWMWA_BORDER_COLOR
        int corner = smallCorners ? 3 : 2;
        return DwmSetWindowAttribute(hwnd, 33, ref corner, 4) == 0; // DWMWA_WINDOW_CORNER_PREFERENCE
    }

    /// <summary>Paint a solid background ourselves (the default erase came out lighter on some setups).</summary>
    public static void PaintSolid(Control c, PaintEventArgs e)
    {
        using var b = new SolidBrush(c.BackColor);
        e.Graphics.FillRectangle(b, c.ClientRectangle);
    }

    public static void DarkScrollbars(Control c)
    {
        try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
    }

    static ToolTip tip;

    public static void SetTip(Control c, string text)
    {
        if (tip == null)
        {
            tip = new ToolTip { OwnerDraw = true, InitialDelay = 350, ReshowDelay = 100 };
            tip.Popup += (_, e) =>
            {
                var sz = TextRenderer.MeasureText(tip.GetToolTip(e.AssociatedControl), Small);
                e.ToolTipSize = new Size(sz.Width + S(16), sz.Height + S(10));
            };
            tip.Draw += (_, e) =>
            {
                e.Graphics.Clear(Chrome);
                using var pen = new Pen(BorderStrong);
                e.Graphics.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, Small, e.Bounds, Text, Centered);
            };
        }
        if (tip.GetToolTip(c) != text) tip.SetToolTip(c, text);
    }

    [DllImport("user32.dll")] static extern uint GetDpiForSystem();
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
}

/// <summary>Dark dropdown/context menus in the app style.</summary>
static class DarkMenu
{
    public static ContextMenuStrip Create(int minWidth = 0)
    {
        var m = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            Font = Theme.Body,
            DropShadowEnabled = false,
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Padding = new Padding(0, Theme.S(4), 0, Theme.S(4)),
            MinimumSize = new Size(minWidth, 0),
        };
        m.HandleCreated += (_, _) => Theme.StyleWindow(m.Handle, smallCorners: true);
        return m;
    }

    public static ToolStripMenuItem Add(ContextMenuStrip menu, string text, bool isChecked, Action onClick)
    {
        var item = new ToolStripMenuItem(text)
        {
            Checked = isChecked,
            Enabled = onClick != null,
            Padding = new Padding(Theme.S(2), Theme.S(5), Theme.S(8), Theme.S(5)),
        };
        if (onClick != null) item.Click += (_, _) => onClick();
        menu.Items.Add(item);
        return item;
    }

    /// <summary>Shows a one-off menu that disposes itself when closed.</summary>
    public static void ShowOnce(ContextMenuStrip menu, Control at, Point location)
    {
        menu.Closed += (_, _) => at.BeginInvoke(menu.Dispose);
        menu.Show(at, location);
    }
}

/// <summary>Dark tray menu.</summary>
sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Theme.Chrome);
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        Theme.FillRound(e.Graphics, Theme.SurfaceHover,
            new RectangleF(Theme.S(4), 1, e.Item.Width - Theme.S(8), e.Item.Height - 2), Theme.S(4));
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // The default layout puts the text ~2px above the middle of the item (and of the hover fill),
        // so draw it ourselves, vertically centred on the whole item.
        var r = new Rectangle(e.TextRectangle.X, 0, e.Item.Width - e.TextRectangle.X, e.Item.Height);
        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, r, e.Item.Enabled ? Theme.Text : Theme.TextFaint,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.HidePrefix | TextFormatFlags.EndEllipsis);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawLine(pen, Theme.S(10), y, e.Item.Width - Theme.S(10), y);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) =>
        // Centred on the item like the text; this glyph's ink sits ~2px low in its box, so lift it.
        Theme.Glyph(e.Graphics, "", 9f, Theme.AccentText,
            new Rectangle(e.ImageRectangle.X, -Theme.S(2), e.ImageRectangle.Width, e.Item.Height));
}
