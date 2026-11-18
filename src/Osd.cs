using System.Runtime.InteropServices;

namespace Auxer;

/// <summary>Small click-through popup ("Spotify 35%") that never steals focus from the game.</summary>
sealed class Osd : Form
{
    readonly System.Windows.Forms.Timer hideTimer = new() { Interval = 1400 };
    string title = "", detail = "";
    int percent;
    bool muted, showBar;

    static int S(int v) => Theme.S(v);

    public Osd()
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Chrome;
        DoubleBuffered = true;
        Size = new Size(S(300), S(66));
        hideTimer.Tick += (_, _) => { hideTimer.Stop(); Hide(); };
    }

    public OverlayPosition Position { get; set; } = OverlayPosition.TopCenter;
    public int DurationMs { get; set; } = 1400;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 /*NOACTIVATE*/ | 0x80 /*TOOLWINDOW*/ | 0x8 /*TOPMOST*/ | 0x20 /*TRANSPARENT*/;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!Theme.StyleWindow(Handle))
        {
            var rgn = CreateRoundRectRgn(0, 0, Width + 1, Height + 1, S(14), S(14));
            Region = Region.FromHrgn(rgn);
            DeleteObject(rgn);
        }
    }

    public void ShowLevel(string app, int pct, bool isMuted)
    {
        title = Theme.Pretty(app);
        percent = pct;
        muted = isMuted;
        detail = isMuted ? "Muted" : pct + "%";
        showBar = true;
        Pop();
    }

    public void ShowMessage(string app, string message)
    {
        title = Theme.Pretty(app);
        detail = message;
        showBar = false;
        Pop();
    }

    void Pop()
    {
        // Show on the monitor of the window you're using (e.g. the game).
        var area = Screen.FromHandle(GetForegroundWindow()).WorkingArea;
        int m = S(32);
        int x = Position switch
        {
            OverlayPosition.TopLeft or OverlayPosition.BottomLeft => area.Left + m,
            OverlayPosition.TopRight or OverlayPosition.BottomRight => area.Right - Width - m,
            _ => area.Left + (area.Width - Width) / 2,
        };
        bool bottom = Position is OverlayPosition.BottomCenter or OverlayPosition.BottomLeft or OverlayPosition.BottomRight;
        Location = new Point(x, bottom ? area.Bottom - Height - m : area.Top + m);
        Invalidate();
        if (!Visible) Show();
        SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // TOPMOST, NOSIZE|NOMOVE|NOACTIVATE
        hideTimer.Stop();
        hideTimer.Interval = Math.Clamp(DurationMs, 500, 10000);
        hideTimer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        // Icon tile
        int tile = S(38), tx = S(14), ty = (Height - tile) / 2;
        Theme.FillRound(g, muted || !showBar ? Theme.SurfacePressed : Theme.AccentSoft, new RectangleF(tx, ty, tile, tile), S(9));
        string glyph = !showBar ? "" : muted ? "" : percent < 34 ? "" : percent < 67 ? "" : "";
        Theme.Glyph(g, glyph, 12f, muted ? Theme.Danger : !showBar ? Theme.TextDim : Theme.AccentText, new Rectangle(tx, ty, tile, tile));

        int x = tx + tile + S(12), right = Width - S(16), w = right - x;
        var textRect = showBar ? new Rectangle(x, S(11), w, S(24)) : new Rectangle(x, 0, w, Height);
        int dw = TextRenderer.MeasureText(g, detail, Theme.Bold).Width;
        TextRenderer.DrawText(g, title, Theme.Bold, textRect with { Width = w - dw - S(8) }, Theme.Text, Theme.LeftMiddle);
        TextRenderer.DrawText(g, detail, Theme.Bold, textRect, muted ? Theme.Danger : Theme.TextDim,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        if (!showBar) return;
        var bar = new RectangleF(x, S(42), w, S(5));
        Theme.FillRound(g, Theme.BorderStrong, bar, bar.Height / 2);
        if (percent > 0)
            Theme.FillRound(g, muted ? Theme.TextFaint : Theme.Accent,
                bar with { Width = Math.Max(bar.Height, bar.Width * Math.Min(percent, 100) / 100f) }, bar.Height / 2);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) hideTimer.Dispose();
        base.Dispose(disposing);
    }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
}
