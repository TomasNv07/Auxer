using System.Runtime.InteropServices;

namespace Auxer;

/// <summary>
/// Compact volume panel that pops up above the tray icon (like the Windows volume flyout):
/// a slider for every app with audio, plus a button to open the full Auxer window.
/// Closes when it loses focus or on Esc.
/// </summary>
sealed class TrayFlyout : Form
{
    static int S(int v) => Theme.S(v);

    const int MaxVisibleRows = 6;
    readonly PageHeader header;
    readonly StackPanel list;
    readonly EmptyState empty;
    readonly System.Windows.Forms.Timer refresh = new() { Interval = 1500 };
    readonly List<MixerRow> rows = new();
    readonly TrayApp app;
    List<AudioApp> snapshot = new();
    Point anchor;

    public TrayFlyout(TrayApp app)
    {
        this.app = app;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        DoubleBuffered = true;
        Padding = new Padding(S(14), S(10), S(14), S(14));
        Width = S(430);

        header = new PageHeader("Volume", "");
        var open = new FlatButton("Open Auxer", glyph: "");
        // Opening the main window takes focus, which closes this flyout via OnDeactivate.
        open.Click += (_, _) => BeginInvoke(app.ShowWindow);
        header.Controls.Add(open);

        list = new StackPanel { Dock = DockStyle.Fill, Spacing = S(6) };
        empty = new EmptyState("", "Nothing is playing", "Apps show up here once they open audio.") { Visible = false };

        Controls.Add(list);
        Controls.Add(empty);
        empty.BringToFront();
        Controls.Add(header);

        refresh.Tick += (_, _) => RefreshApps();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW: not in Alt+Tab
            cp.ClassStyle |= 0x20000;  // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.StyleWindow(Handle);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintSolid(this, e);

    /// <summary>Opens next to the tray icon the user clicked.</summary>
    public void ShowNear(Point cursor)
    {
        anchor = cursor;
        RefreshApps();
        Show();
        Activate();
        ActiveControl = null; // don't start with a slider focused (it would grab the wheel / arrow keys)
        refresh.Start();
    }

    void RefreshApps()
    {
        if (IsDisposed) return;
        var fresh = app.GetVisibleApps();
        bool sameApps = fresh.Count == rows.Count &&
                        fresh.Select(a => a.Name).SequenceEqual(rows.Select(r => r.App.Name), StringComparer.OrdinalIgnoreCase);
        var old = snapshot;
        snapshot = fresh;

        if (sameApps)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].App = fresh[i];
                rows[i].Sync();
            }
        }
        else
        {
            list.BeginUpdate();
            foreach (var r in rows) r.Dispose();
            rows.Clear();
            foreach (var a in snapshot)
            {
                var row = new MixerRow(a, hide: n => { app.HideApp(n); RefreshApps(); }, compact: true);
                rows.Add(row);
                list.Items.Add(row);
                row.Sync();
            }
            list.EndUpdate();
            empty.Visible = rows.Count == 0;
            FitAndPlace();
        }
        header.Subtitle = rows.Count switch
        {
            0 => "No apps have audio open",
            1 => "1 app",
            var n => $"{n} apps",
        };
        foreach (var a in old) a.Dispose();
    }

    /// <summary>Sizes to the rows (scrolls past a few) and sits in the screen corner nearest the tray icon.</summary>
    void FitAndPlace()
    {
        int visible = Math.Min(rows.Count, MaxVisibleRows);
        int listH = visible == 0 ? S(130) : visible * S(52) + (visible - 1) * S(6);
        Height = Padding.Vertical + header.Height + listH;

        var wa = Screen.FromPoint(anchor).WorkingArea;
        int gap = S(12);
        int x = Math.Clamp(anchor.X - Width / 2, wa.Left + gap, wa.Right - Width - gap);
        bool bottom = anchor.Y > wa.Top + wa.Height / 2;
        int y = bottom ? wa.Bottom - Height - gap : wa.Top + gap;

        // An auto-hide taskbar isn't excluded from the working area, so step clear of it explicitly.
        var bar = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        if (SHAppBarMessage(5 /*ABM_GETTASKBARPOS*/, ref bar) != IntPtr.Zero)
        {
            var tb = Rectangle.FromLTRB(bar.rc.Left, bar.rc.Top, bar.rc.Right, bar.rc.Bottom);
            if (tb.IntersectsWith(new Rectangle(x, y, Width, Height)))
            {
                switch (bar.uEdge)
                {
                    case 0: x = tb.Right + gap; break;            // left
                    case 1: y = tb.Bottom + gap; break;           // top
                    case 2: x = tb.Left - Width - gap; break;     // right
                    default: y = tb.Top - Height - gap; break;    // bottom
                }
            }
        }
        Location = new Point(x, y);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")]
    static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        BeginInvoke(() => { if (!IsDisposed) Close(); });
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        refresh.Stop();
        refresh.Dispose();
        foreach (var a in snapshot) a.Dispose();
        snapshot.Clear();
        base.OnFormClosed(e);
    }
}
