using System.Runtime.InteropServices;

namespace Auxer;

/// <summary>
/// Window with custom-drawn chrome: dark title bar with icon buttons, a thin accent line, optional status bar.
/// Keeps the native frame styles, so snapping, the shadow and the minimize animation still work.
/// </summary>
class ThemedForm : Form
{
    protected static int S(int v) => Theme.S(v);

    const int DividerLine = 1;
    readonly List<IconButton> left = new();
    readonly List<IconButton> right = new();
    string statusLeft = "", statusRight = "";
    Color statusDot = Color.Empty;

    public int TitleHeight { get; } = S(42);
    public int StatusHeight { get; }
    public bool Resizable { get; set; }

    public ThemedForm(bool minimizeButton = true, bool statusBar = false)
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = minimizeButton;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        StatusHeight = statusBar ? S(28) : 0;
        Padding = new Padding(S(5), TitleHeight + DividerLine, S(5), StatusHeight);

        var close = AddTitleButton("", null, true);
        close.GlyphSize = 9f;
        close.DangerHover = true;
        close.Click += (_, _) => Close();
        if (minimizeButton)
        {
            var min = AddTitleButton("", null, true);
            min.GlyphSize = 9f;
            min.Click += (_, _) => WindowState = FormWindowState.Minimized;
        }
    }

    /// <summary>Adds an icon button to the title bar (left side, or right side next to minimize/close).</summary>
    protected IconButton AddTitleButton(string glyph, string tip, bool onRight = false)
    {
        var b = new IconButton(glyph, tip) { Size = new Size(S(34), S(30)) };
        (onRight ? right : left).Add(b);
        Controls.Add(b);
        PositionTitleButtons();
        return b;
    }

    protected void SetStatus(string leftText, string rightText, Color dot)
    {
        statusLeft = leftText;
        statusRight = rightText;
        statusDot = dot;
        Invalidate(new Rectangle(0, ClientSize.Height - StatusHeight, ClientSize.Width, StatusHeight));
    }

    void PositionTitleButtons()
    {
        int y = (TitleHeight - S(30)) / 2;
        int x = S(8);
        foreach (var b in left) { b.Location = new Point(x, y); x += b.Width + S(2); }
        x = ClientSize.Width - S(8);
        foreach (var b in right) { x -= b.Width; b.Location = new Point(x, y); x -= S(2); }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PositionTitleButtons();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate(new Rectangle(0, 0, ClientSize.Width, TitleHeight));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.StyleWindow(Handle);
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0020 | 0x0002 | 0x0001 | 0x0004 | 0x0010); // FRAMECHANGED
    }

    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintSolid(this, e);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int w = ClientSize.Width, h = ClientSize.Height;

        using (var b = new SolidBrush(Theme.Chrome)) g.FillRectangle(b, 0, 0, w, TitleHeight);
        using (var b = new SolidBrush(Theme.Divider)) g.FillRectangle(b, 0, TitleHeight, w, DividerLine);
        TextRenderer.DrawText(g, Text, Theme.Title, new Rectangle(0, 0, w, TitleHeight), Theme.Text, Theme.Centered);

        if (StatusHeight > 0)
        {
            int top = h - StatusHeight;
            using (var b = new SolidBrush(Theme.Chrome)) g.FillRectangle(b, 0, top, w, StatusHeight);
            using (var p = new Pen(Theme.Divider)) g.DrawLine(p, 0, top, w, top);
            int x = S(14);
            if (!statusDot.IsEmpty)
            {
                int d = S(7);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var b = new SolidBrush(statusDot);
                g.FillEllipse(b, x, top + (StatusHeight - d) / 2f, d, d);
                x += d + S(8);
            }
            TextRenderer.DrawText(g, statusLeft, Theme.Small, new Rectangle(x, top, w / 2, StatusHeight), Theme.TextDim, Theme.LeftMiddle);
            TextRenderer.DrawText(g, statusRight, Theme.Small, new Rectangle(w / 2, top, w / 2 - S(14), StatusHeight), Theme.TextFaint,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCCALCSIZE = 0x83, WM_NCHITTEST = 0x84;
        if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            m.Result = IntPtr.Zero; // the whole window is client area
            return;
        }
        if (m.Msg == WM_NCHITTEST)
        {
            long lp = m.LParam.ToInt64();
            var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            m.Result = (IntPtr)HitTest(p);
            return;
        }
        base.WndProc(ref m);
    }

    int HitTest(Point p)
    {
        var cs = ClientSize;
        if (Resizable && WindowState == FormWindowState.Normal)
        {
            int g = S(6);
            bool l = p.X < g, r = p.X >= cs.Width - g, t = p.Y < g, b = p.Y >= cs.Height - g;
            if (t && l) return 13;
            if (t && r) return 14;
            if (b && l) return 16;
            if (b && r) return 17;
            if (l) return 10;
            if (r) return 11;
            if (t) return 12;
            if (b) return 15;
        }
        return p.Y < TitleHeight ? 2 /*HTCAPTION*/ : 1 /*HTCLIENT*/;
    }

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
