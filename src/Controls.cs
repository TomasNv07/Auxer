namespace Auxer;

/// <summary>Base for the owner-drawn controls: double-buffered, transparent by default, tracks hover/press.</summary>
abstract class PaintedControl : Control
{
    protected static int S(int v) => Theme.S(v);
    protected bool Hover, Pressed;

    protected PaintedControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Font = Theme.Body;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (BackColor.A < 255) base.OnPaintBackground(e); // transparent: let the parent show through
        else Theme.PaintSolid(this, e);
    }

    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = Pressed = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { Pressed = true; Invalidate(); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        Pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
}

sealed class IconButton : PaintedControl
{
    string glyph;
    bool isChecked;
    Color? glyphColor;

    public IconButton(string glyph, string tip = null)
    {
        this.glyph = glyph;
        Size = new Size(S(32), S(30));
        Cursor = Cursors.Hand;
        if (tip != null) Theme.SetTip(this, tip);
    }

    public string Glyph { get => glyph; set { if (glyph != value) { glyph = value; Invalidate(); } } }
    public bool Checked { get => isChecked; set { if (isChecked != value) { isChecked = value; Invalidate(); } } }
    public Color? GlyphColor { get => glyphColor; set { glyphColor = value; Invalidate(); } }
    public float GlyphSize { get; set; } = 10.5f;
    public bool DangerHover { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Color bg = Checked ? Theme.AccentSoft
            : Hover && DangerHover ? Theme.CloseHover
            : Pressed ? Theme.SurfacePressed
            : Hover ? Theme.SurfaceHover
            : Color.Empty;
        if (!bg.IsEmpty) Theme.FillRound(g, bg, new RectangleF(0, 0, Width, Height), S(6));

        Color fg = !Enabled ? Theme.TextFaint
            : Checked ? Theme.AccentText
            : Hover && DangerHover ? Color.White
            : Hover ? Theme.Text
            : glyphColor ?? Theme.TextDim;
        Theme.Glyph(g, glyph, GlyphSize, fg, ClientRectangle);
    }
}

/// <summary>Text button: primary (dark blue) or secondary (grey).</summary>
sealed class FlatButton : PaintedControl, IButtonControl
{
    readonly bool primary;
    readonly string glyph;

    public FlatButton(string text, bool primary = false, string glyph = null)
    {
        Text = text;
        this.primary = primary;
        this.glyph = glyph;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
        int w = TextRenderer.MeasureText(text, Theme.Bold).Width + S(32) + (glyph != null ? S(22) : 0);
        Size = new Size(Math.Max(S(86), w), S(34));
    }

    public DialogResult DialogResult { get; set; }
    public void NotifyDefault(bool value) { }
    public void PerformClick() => OnClick(EventArgs.Empty);

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (DialogResult != DialogResult.None && FindForm() is Form f) f.DialogResult = DialogResult;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Space) PerformClick();
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = new RectangleF(0, 0, Width, Height);
        Color bg = primary
            ? (Pressed ? Theme.AccentPressed : Hover ? Theme.AccentHover : Theme.Accent)
            : (Pressed ? Theme.SurfacePressed : Hover ? Theme.SurfaceHover : Theme.Surface);
        Theme.FillRound(g, bg, r, S(8));
        if (!primary) Theme.DrawRound(g, Theme.BorderStrong, r, S(8));
        if (Focused && ShowFocusCues) Theme.DrawRound(g, Theme.AccentText, r, S(8));

        Color fg = !Enabled ? Theme.TextFaint : primary ? Color.White : Theme.Text;
        int tw = TextRenderer.MeasureText(g, Text, Theme.Bold, Size.Empty, TextFormatFlags.NoPadding).Width;
        int gw = glyph != null ? S(16) + S(6) : 0;
        int x = (Width - tw - gw) / 2;
        if (glyph != null) Theme.Glyph(g, glyph, 9.5f, fg, new Rectangle(x, 0, S(16), Height));
        TextRenderer.DrawText(g, Text, Theme.Bold, new Rectangle(x + gw, 0, tw + 2, Height), fg, Theme.LeftMiddle);
    }
}

/// <summary>Plain text, optionally wrapping.</summary>
sealed class TextLabel : PaintedControl
{
    public TextLabel(string text, Font font = null, Color? color = null)
    {
        Text = text;
        Font = font ?? Theme.Body;
        ForeColor = color ?? Theme.Text;
    }

    public bool Wrap { get; set; }

    /// <summary>Sets width and grows height to fit the text.</summary>
    public void Fit(int width)
    {
        var flags = Wrap ? TextFormatFlags.WordBreak | TextFormatFlags.NoPadding : TextFormatFlags.NoPadding;
        var sz = TextRenderer.MeasureText(Text, Font, new Size(width, int.MaxValue), flags);
        Size = new Size(width, sz.Height);
    }

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                    (Wrap ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, flags);
    }
}

/// <summary>Bold title with a dim line underneath.</summary>
sealed class TwoLineLabel : PaintedControl
{
    string title = "", description;

    public string Title { get => title; set { if (title != value) { title = value; Invalidate(); } } }
    public string Description { get => description; set { if (description != value) { description = value; Invalidate(); } } }
    public Font TitleFont { get; set; } = Theme.Bold;
    public Color TitleColor { get; set; } = Theme.Text;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (string.IsNullOrEmpty(description))
        {
            TextRenderer.DrawText(g, title, TitleFont, ClientRectangle, TitleColor, Theme.LeftMiddle);
            return;
        }
        int th = TitleFont.Height, dh = Theme.Small.Height;
        int y = (Height - th - dh) / 2;
        TextRenderer.DrawText(g, title, TitleFont, new Rectangle(0, y, Width, th), TitleColor, Theme.LeftMiddle);
        TextRenderer.DrawText(g, description, Theme.Small, new Rectangle(0, y + th, Width, dh), Theme.TextDim, Theme.LeftMiddle);
    }
}

/// <summary>Page title + subtitle; any child controls are right-aligned (e.g. an "Add" button).</summary>
sealed class PageHeader : PaintedControl
{
    readonly string title;
    string subtitle;

    public PageHeader(string title, string subtitle)
    {
        this.title = title;
        this.subtitle = subtitle;
        Dock = DockStyle.Top;
        Height = S(58);
        BackColor = Theme.Bg;
    }

    public string Subtitle { get => subtitle; set { if (subtitle != value) { subtitle = value; Invalidate(); } } }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int x = Width;
        foreach (Control c in Controls)
        {
            x -= c.Width;
            c.Location = new Point(x, (Height - S(10) - c.Height) / 2);
            x -= S(8);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int th = Theme.Heading.Height;
        TextRenderer.DrawText(g, title, Theme.Heading, new Rectangle(0, S(4), Width, th), Theme.Text, Theme.LeftMiddle);
        TextRenderer.DrawText(g, subtitle, Theme.Small, new Rectangle(0, S(4) + th, Width, Theme.Small.Height + S(2)), Theme.TextDim, Theme.LeftMiddle);
    }
}

/// <summary>Centered icon + message for empty lists.</summary>
sealed class EmptyState : PaintedControl
{
    readonly string glyph, title, text;

    public EmptyState(string glyph, string title, string text)
    {
        this.glyph = glyph;
        this.title = title;
        this.text = text;
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int cy = Height / 2 - S(30);
        Theme.Glyph(g, glyph, 22f, Theme.TextFaint, new Rectangle(0, cy - S(24), Width, S(40)));
        TextRenderer.DrawText(g, title, Theme.Bold, new Rectangle(0, cy + S(22), Width, S(22)), Theme.Text, Theme.Centered);
        TextRenderer.DrawText(g, text, Theme.Small, new Rectangle(0, cy + S(44), Width, S(20)), Theme.TextDim, Theme.Centered);
    }
}

/// <summary>
/// Rounded card that lays its children out in a row, with thin dividers between segments.
/// Widths are in pixels; 0 means "share the remaining space".
/// </summary>
class SegmentRow : PaintedControl
{
    readonly List<(Control control, int width, bool separator, bool stretch)> items = new();
    readonly List<int> separators = new();

    public SegmentRow(int height = 54)
    {
        Height = S(height);
        BackColor = Theme.Bg;
    }

    public T Add<T>(T control, int width, bool separator = true, bool stretch = false) where T : Control
    {
        items.Add((control, width, separator && items.Count > 0, stretch));
        Controls.Add(control);
        PerformLayout();
        return control;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int pad = S(16), sepGap = S(12), tight = S(2);
        int fixedSum = 0, fills = 0, gaps = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].width > 0) fixedSum += items[i].width; else fills++;
            if (i > 0) gaps += items[i].separator ? sepGap * 2 : tight;
        }
        int fillW = fills > 0 ? Math.Max(S(24), (Width - pad * 2 - fixedSum - gaps) / fills) : 0;

        separators.Clear();
        int x = pad;
        for (int i = 0; i < items.Count; i++)
        {
            var (c, width, separator, stretch) = items[i];
            if (i > 0)
            {
                if (separator) { separators.Add(x + sepGap); x += sepGap * 2; }
                else x += tight;
            }
            int w = width > 0 ? width : fillW;
            int h = stretch ? Height - S(8) : Math.Min(c.Height, Height - S(8));
            c.SetBounds(x, (Height - h) / 2, w, h);
            x += w;
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = new RectangleF(0, 0, Width, Height);
        Theme.FillRound(g, Theme.Surface, r, S(10));
        Theme.DrawRound(g, Theme.Border, r, S(10));
        using var pen = new Pen(Theme.Border);
        foreach (var sx in separators) g.DrawLine(pen, sx, S(14), sx, Height - S(14));
    }
}

/// <summary>
/// Vertical list that stretches each row to the full width, with smooth wheel scrolling and a thin themed
/// scrollbar. Rows live in one inner panel; scrolling just slides that panel, so Windows moves the pixels
/// in one go instead of re-laying out and repainting every row (keeps dragging smooth).
/// Add rows through <see cref="Items"/>.
/// </summary>
sealed class StackPanel : Panel
{
    static int S(int v) => Theme.S(v);

    readonly BgPanel content = new();
    readonly System.Windows.Forms.Timer anim = new() { Interval = 10 };
    float offset;
    int target, contentHeight;
    bool hoverBar, dragging, arranging;
    int dragStartY;
    float dragStartOffset;

    public StackPanel()
    {
        BackColor = Theme.Bg;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        Controls.Add(content);
        content.Layout += (_, _) => ArrangeRows();
        anim.Tick += (_, _) => Animate();
    }

    public Control.ControlCollection Items => content.Controls;
    public int Spacing { get; set; } = Theme.S(8);

    public void BeginUpdate() => content.SuspendLayout();
    public void EndUpdate() { content.ResumeLayout(false); ArrangeRows(); }

    int Gutter => S(14);
    int MaxOffset => Math.Max(0, contentHeight - ClientSize.Height);
    bool CanScroll => MaxOffset > 0;
    Rectangle GutterRect => new(ClientSize.Width - Gutter, 0, Gutter, ClientSize.Height);
    Rectangle Track => new(ClientSize.Width - S(9), S(2), S(7), ClientSize.Height - S(4));

    float ThumbHeight => Math.Max(S(30), Track.Height * (float)ClientSize.Height / Math.Max(1, contentHeight));

    RectangleF Thumb
    {
        get
        {
            var t = Track;
            float h = ThumbHeight;
            float y = t.Y + (t.Height - h) * (MaxOffset == 0 ? 0 : offset / MaxOffset);
            float w = hoverBar || dragging ? S(7) : S(4);
            return new RectangleF(t.Right - w, y, w, h);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintSolid(this, e);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!CanScroll) return;
        var th = Thumb;
        Theme.FillRound(e.Graphics, hoverBar || dragging ? Theme.TextFaint : Theme.BorderStrong, th, th.Width / 2);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        ArrangeRows();
    }

    /// <summary>Stacks the rows inside the inner panel and sizes it; only runs when rows or size change.</summary>
    void ArrangeRows()
    {
        if (arranging) return;
        arranging = true;
        try
        {
            int total = 0;
            foreach (Control c in content.Controls)
                if (c.Visible) total += c.Height + Spacing;
            contentHeight = Math.Max(0, total - Spacing);
            target = Math.Clamp(target, 0, MaxOffset);
            offset = Math.Clamp(offset, 0, MaxOffset);

            int w = ClientSize.Width - (CanScroll ? Gutter : 0), y = 0;
            foreach (Control c in content.Controls)
            {
                if (!c.Visible) continue;
                var want = new Rectangle(0, y, w, c.Height);
                if (c.Bounds != want) c.Bounds = want;
                y += c.Height + Spacing;
            }
            var bounds = new Rectangle(0, -(int)Math.Round(offset), w, Math.Max(1, contentHeight));
            if (content.Bounds != bounds) content.Bounds = bounds;
        }
        finally { arranging = false; }
        Invalidate(GutterRect);
    }

    /// <summary>Scrolling: move the inner panel only.</summary>
    void ApplyOffset()
    {
        int top = -(int)Math.Round(offset);
        if (content.Top != top) content.Top = top;
        Invalidate(GutterRect);
        Update();
    }

    void ScrollTo(float value, bool smooth = true)
    {
        target = (int)Math.Round(Math.Clamp(value, 0, MaxOffset));
        if (smooth) { if (!anim.Enabled) anim.Start(); }
        else { anim.Stop(); offset = Math.Clamp(value, 0, MaxOffset); ApplyOffset(); }
    }

    void Animate()
    {
        float diff = target - offset;
        if (Math.Abs(diff) < 0.6f) { offset = target; anim.Stop(); }
        else offset += diff * 0.35f;
        ApplyOffset();
    }

    /// <summary>Distance between the tops of two rows (rows share one height).</summary>
    float RowStep
    {
        get
        {
            foreach (Control c in content.Controls)
                if (c.Visible) return c.Height + Spacing;
            return S(90);
        }
    }

    /// <summary>Nearest row boundary, so a row is never left half cut off at the top.</summary>
    float Snap(float value)
    {
        float step = RowStep;
        return Math.Clamp((float)Math.Round(value / step) * step, 0, MaxOffset);
    }

    int wheelDelta;

    /// <summary>One wheel notch = exactly one row. Wheel events from rows bubble up to here.</summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (CanScroll)
        {
            wheelDelta += e.Delta; // touchpads send small deltas; act per full notch
            int notches = wheelDelta / 120;
            wheelDelta -= notches * 120;
            if (notches != 0) ScrollTo(Snap(target) - notches * RowStep);
            if (e is HandledMouseEventArgs h) h.Handled = true;
        }
        base.OnMouseWheel(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging)
        {
            float range = Track.Height - ThumbHeight;
            if (range > 0) ScrollTo(dragStartOffset + (e.Y - dragStartY) * MaxOffset / range, smooth: false);
            return;
        }
        bool over = CanScroll && e.X >= ClientSize.Width - Gutter;
        if (over != hoverBar) { hoverBar = over; Invalidate(GutterRect); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !CanScroll || e.X < ClientSize.Width - Gutter) return;
        var th = Thumb;
        if (e.Y < th.Top - S(4) || e.Y > th.Bottom + S(4))
        {
            // Clicked the track: jump so the thumb is centred under the cursor, then keep dragging from there.
            var t = Track;
            float range = t.Height - th.Height;
            if (range > 0) ScrollTo((e.Y - t.Y - th.Height / 2) / range * MaxOffset, smooth: false);
        }
        dragging = true;
        dragStartY = e.Y;
        dragStartOffset = offset;
        Capture = true;
        Invalidate(GutterRect);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!dragging) return;
        dragging = false;
        Capture = false;
        ScrollTo(Snap(offset)); // settle on a row boundary
        Invalidate(GutterRect);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoverBar && !dragging) { hoverBar = false; Invalidate(GutterRect); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) anim.Dispose();
        base.Dispose(disposing);
    }
}
/// <summary>Plain container with a solid themed background.</summary>
sealed class BgPanel : Panel
{
    public BgPanel()
    {
        BackColor = Theme.Bg;
        DoubleBuffered = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintSolid(this, e);
}

sealed class BgFlowPanel : FlowLayoutPanel
{
    public BgFlowPanel()
    {
        BackColor = Theme.Bg;
        DoubleBuffered = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e) => Theme.PaintSolid(this, e);
}

/// <summary>Keyboard-key chips: [Ctrl] [Alt] [Down].</summary>
sealed class KeyCaps : PaintedControl
{
    string[] parts = Array.Empty<string>();

    public string Combo
    {
        set
        {
            parts = string.IsNullOrEmpty(value) ? Array.Empty<string>() : value.Split('+');
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (parts.Length == 0)
        {
            TextRenderer.DrawText(g, "No hotkey", Theme.Body, ClientRectangle, Theme.TextFaint, Theme.LeftMiddle);
            return;
        }
        DrawCaps(g, parts, 0, Height / 2, Width);
    }

    /// <summary>Draws key chips left to right starting at x, vertically centred on centerY, stopping before maxRight.</summary>
    public static void DrawCaps(Graphics g, string[] parts, int x, int centerY, int maxRight)
    {
        int h = S(24), y = centerY - h / 2;
        foreach (var p in parts)
        {
            int w = Math.Max(h, TextRenderer.MeasureText(g, p, Theme.Bold, Size.Empty, TextFormatFlags.NoPadding).Width + S(14));
            if (x + w > maxRight) break;
            var r = new RectangleF(x, y, w, h);
            Theme.FillRound(g, Color.FromArgb(10, 11, 13), r with { Y = r.Y + S(2) }, S(5));
            Theme.FillRound(g, Theme.SurfacePressed, r, S(5));
            Theme.DrawRound(g, Theme.BorderStrong, r, S(5));
            TextRenderer.DrawText(g, p, Theme.Bold, Rectangle.Round(r), Theme.Text, Theme.Centered);
            x += w + S(5);
        }
    }
}

/// <summary>Colored dot + text.</summary>
sealed class StatusLabel : PaintedControl
{
    public Color Dot { get; set; } = Theme.Success;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int d = S(8);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Dot)) g.FillEllipse(b, 0, (Height - d) / 2f, d, d);
        TextRenderer.DrawText(g, Text, Theme.Body, new Rectangle(d + S(8), 0, Width - d - S(8), Height),
            Dot == Theme.Danger ? Theme.Danger : Theme.TextDim, Theme.LeftMiddle);
    }
}

/// <summary>Big number with a small unit: 45%.</summary>
sealed class ValueLabel : PaintedControl
{
    int value;
    bool dim;

    public void Set(int v, bool isDim)
    {
        if (v == value && isDim == dim) return;
        value = v;
        dim = isDim;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var num = value.ToString();
        const TextFormatFlags f = TextFormatFlags.NoPadding;
        int nw = TextRenderer.MeasureText(g, num, Theme.Number, Size.Empty, f).Width;
        int uw = TextRenderer.MeasureText(g, "%", Theme.Small, Size.Empty, f).Width;
        int x = Width - nw - uw - S(2);
        TextRenderer.DrawText(g, num, Theme.Number, new Rectangle(x, 0, nw + 2, Height), dim ? Theme.TextFaint : Theme.Text, Theme.LeftMiddle);
        TextRenderer.DrawText(g, "%", Theme.Small, new Rectangle(x + nw + S(2), S(2), uw + 2, Height), Theme.TextDim, Theme.LeftMiddle);
    }
}

sealed class ToggleSwitch : PaintedControl
{
    bool on;

    public ToggleSwitch()
    {
        Size = new Size(S(42), S(24));
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
    }

    public event EventHandler CheckedChanged;

    public bool Checked
    {
        get => on;
        set
        {
            if (on == value) return;
            on = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Space) Checked = !Checked;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = new RectangleF(0, 0, Width, Height);
        float radius = Height / 2f;
        if (on) Theme.FillRound(g, Hover ? Theme.AccentHover : Theme.Accent, r, radius);
        else
        {
            Theme.FillRound(g, Hover ? Theme.SurfaceHover : Theme.Field, r, radius);
            Theme.DrawRound(g, Theme.BorderStrong, r, radius);
        }
        float d = Height - S(8);
        float x = on ? Width - d - S(4) : S(4);
        using var b = new SolidBrush(on ? Color.White : Theme.TextDim);
        g.FillEllipse(b, x, S(4), d, d);
    }
}

/// <summary>Pick one of a few options, like [Volume down | Volume up | Mute | Set].</summary>
sealed class SegmentedControl : PaintedControl
{
    readonly string[] items;
    int selected, hoverIndex = -1;

    public SegmentedControl(params string[] items)
    {
        this.items = items;
        Height = S(38);
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
    }

    public event EventHandler SelectedIndexChanged;

    public int SelectedIndex
    {
        get => selected;
        set
        {
            value = Math.Clamp(value, 0, items.Length - 1);
            if (selected == value) return;
            selected = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    RectangleF Segment(int i)
    {
        float pad = S(3), w = (Width - pad * 2) / items.Length;
        return new RectangleF(pad + i * w, pad, w, Height - pad * 2);
    }

    int IndexAt(int x) => Math.Clamp((int)((x - S(3)) / ((Width - S(6)) / (float)items.Length)), 0, items.Length - 1);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = IndexAt(e.X);
        if (i != hoverIndex) { hoverIndex = i; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { hoverIndex = -1; base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        SelectedIndex = IndexAt(e.X);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) SelectedIndex--;
        if (e.KeyCode == Keys.Right) SelectedIndex++;
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = new RectangleF(0, 0, Width, Height);
        Theme.FillRound(g, Theme.Field, r, S(8));
        Theme.DrawRound(g, Focused && ShowFocusCues ? Theme.AccentHover : Theme.Border, r, S(8));
        for (int i = 0; i < items.Length; i++)
        {
            var s = Segment(i);
            if (i == selected) Theme.FillRound(g, Theme.Accent, s, S(6));
            else if (i == hoverIndex) Theme.FillRound(g, Theme.SurfaceHover, s, S(6));
            var fg = i == selected ? Color.White : i == hoverIndex ? Theme.Text : Theme.TextDim;
            TextRenderer.DrawText(g, items[i], i == selected ? Theme.Bold : Theme.Body, Rectangle.Round(s), fg, Theme.Centered);
        }
    }
}

/// <summary>Number with − / + buttons. Also: mouse wheel, arrow keys, typing digits.</summary>
sealed class Stepper : PaintedControl
{
    readonly System.Windows.Forms.Timer repeat = new();
    int current, min, max = 100, hoverPart, repeatStep;
    string typed = "";
    long lastTyped;

    public Stepper()
    {
        Size = new Size(S(140), S(38));
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
        repeat.Tick += (_, _) => { repeat.Interval = 55; Value += repeatStep; };
    }

    public event EventHandler ValueChanged;

    public int Minimum { get => min; set { min = value; Value = current; } }

    public int Value
    {
        get => current;
        set
        {
            value = Math.Clamp(value, min, max);
            if (current == value) return;
            current = value;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    int PartAt(int x) => x < Height ? -1 : x > Width - Height ? 1 : 0;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int p = PartAt(e.X);
        if (p != hoverPart) { hoverPart = p; Invalidate(); }
        Cursor = p != 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        repeatStep = PartAt(e.X);
        if (repeatStep == 0) return;
        Value += repeatStep;
        repeat.Interval = 400;
        repeat.Start();
    }

    protected override void OnMouseUp(MouseEventArgs e) { repeat.Stop(); base.OnMouseUp(e); }
    protected override void OnMouseLeave(EventArgs e) { repeat.Stop(); hoverPart = 0; base.OnMouseLeave(e); }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        Value += Math.Sign(e.Delta);
        if (e is HandledMouseEventArgs h) h.Handled = true;
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Up: case Keys.Right: Value++; break;
            case Keys.Down: case Keys.Left: Value--; break;
            case Keys.PageUp: Value += 10; break;
            case Keys.PageDown: Value -= 10; break;
            case Keys.Back: Value = current / 10; typed = ""; break;
        }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (!char.IsDigit(e.KeyChar)) return;
        if (Environment.TickCount64 - lastTyped > 1200) typed = "";
        lastTyped = Environment.TickCount64;
        typed += e.KeyChar;
        if (int.Parse(typed) > max) typed = e.KeyChar.ToString();
        Value = int.Parse(typed);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = new RectangleF(0, 0, Width, Height);
        Theme.FillRound(g, Theme.Field, r, S(8));
        Theme.DrawRound(g, Focused ? Theme.AccentHover : Theme.Border, r, S(8));

        Color Btn(int part) => !Enabled ? Theme.TextFaint : hoverPart == part ? Theme.Text : Theme.TextDim;
        Theme.Glyph(g, "", 9f, Btn(-1), new Rectangle(0, 0, Height, Height));
        Theme.Glyph(g, "", 9f, Btn(1), new Rectangle(Width - Height, 0, Height, Height));

        var num = current.ToString();
        const TextFormatFlags f = TextFormatFlags.NoPadding;
        int nw = TextRenderer.MeasureText(g, num, Theme.Number, Size.Empty, f).Width;
        int uw = TextRenderer.MeasureText(g, "%", Theme.Small, Size.Empty, f).Width;
        int x = (Width - nw - uw - S(2)) / 2;
        TextRenderer.DrawText(g, num, Theme.Number, new Rectangle(x, 0, nw + 2, Height), Enabled ? Theme.Text : Theme.TextFaint, Theme.LeftMiddle);
        TextRenderer.DrawText(g, "%", Theme.Small, new Rectangle(x + nw + S(2), S(2), uw + 2, Height), Theme.TextDim, Theme.LeftMiddle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) repeat.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Self-drawn single-line text field (the native TextBox can't be fully themed).
/// Supports typing, caret movement, Backspace/Delete, Ctrl+A/C/V and clicking to place the caret.
/// </summary>
class TextInput : PaintedControl
{
    const TextFormatFlags Measure = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
    readonly System.Windows.Forms.Timer blink = new() { Interval = 530 };
    int caret, scroll;
    bool allSelected, caretOn;

    public TextInput()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Height = S(38);
        Cursor = Cursors.IBeam;
        blink.Tick += (_, _) => { caretOn = !caretOn; Invalidate(); };
    }

    public string Placeholder { get; set; } = "";

    /// <summary>Space kept free on the right (for a button placed inside the field).</summary>
    public int RightPadding { get; set; } = Theme.S(12);

    bool readOnly, hoverGlyph;

    /// <summary>Optional icon drawn at the right edge (e.g. a dropdown arrow); clicking it raises TrailingClick.</summary>
    public string TrailingGlyph { get; set; }
    public event EventHandler TrailingClick;
    Rectangle GlyphRect => new(Width - S(40), 0, S(40), Height);

    /// <summary>Read-only: shows the text, no typing or caret; a click just raises Click (used as a select box).</summary>
    public bool ReadOnly
    {
        get => readOnly;
        set
        {
            readOnly = value;
            allSelected = false;
            Cursor = value ? Cursors.Hand : Cursors.IBeam;
            if (value) blink.Stop(); else if (Focused) Wake();
            Invalidate();
        }
    }

    public void SelectAll()
    {
        if (readOnly) return;
        allSelected = Text.Length > 0;
        caret = Text.Length;
        Invalidate();
    }

    int TextLeft => S(12);

    int TextWidth(string s)
    {
        if (s.Length == 0) return 0;
        // Measure with a trailing marker so trailing spaces count.
        return TextRenderer.MeasureText(s + "|", Font, Size.Empty, Measure).Width -
               TextRenderer.MeasureText("|", Font, Size.Empty, Measure).Width;
    }

    void Insert(string s)
    {
        s = s.Replace("\r", "").Replace("\n", " ");
        if (allSelected) { Text = s; caret = s.Length; allSelected = false; }
        else { Text = Text.Insert(caret, s); caret += s.Length; }
        Wake();
    }

    void Wake()
    {
        caretOn = true;
        if (Focused && !readOnly) { blink.Stop(); blink.Start(); }
        Invalidate();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        caret = Math.Clamp(caret, 0, Text.Length);
        Invalidate();
        base.OnTextChanged(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.Delete or Keys.Back
        || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (readOnly) return;
        bool ctrl = e.Control;
        switch (e.KeyCode)
        {
            case Keys.A when ctrl: SelectAll(); break;
            case Keys.C when ctrl: if (Text.Length > 0) Clipboard.SetText(Text); break;
            case Keys.V when ctrl:
                try { if (Clipboard.ContainsText()) Insert(Clipboard.GetText()); } catch { }
                break;
            case Keys.Left: caret = allSelected ? 0 : Math.Max(0, caret - 1); allSelected = false; break;
            case Keys.Right: caret = allSelected ? Text.Length : Math.Min(Text.Length, caret + 1); allSelected = false; break;
            case Keys.Home: caret = 0; allSelected = false; break;
            case Keys.End: caret = Text.Length; allSelected = false; break;
            case Keys.Back:
                if (allSelected) { allSelected = false; Text = ""; }
                else if (caret > 0) { var c = caret - 1; Text = Text.Remove(c, 1); caret = c; }
                break;
            case Keys.Delete:
                if (allSelected) { allSelected = false; Text = ""; }
                else if (caret < Text.Length) Text = Text.Remove(caret, 1);
                break;
            default: return;
        }
        e.Handled = true;
        e.SuppressKeyPress = true;
        Wake();
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (readOnly || e.KeyChar < ' ' || Control.ModifierKeys.HasFlag(Keys.Control)) return;
        Insert(e.KeyChar.ToString());
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (TrailingGlyph != null && GlyphRect.Contains(e.Location)) { TrailingClick?.Invoke(this, EventArgs.Empty); return; }
        if (readOnly) return;
        allSelected = false;
        int x = e.X - TextLeft + scroll, best = 0, bestDist = int.MaxValue;
        for (int i = 0; i <= Text.Length; i++)
        {
            int d = Math.Abs(TextWidth(Text[..i]) - x);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        caret = best;
        Wake();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool over = TrailingGlyph != null && GlyphRect.Contains(e.Location);
        if (over == hoverGlyph) return;
        hoverGlyph = over;
        Cursor = over || readOnly ? Cursors.Hand : Cursors.IBeam;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hoverGlyph = false;
        base.OnMouseLeave(e);
    }

    protected override void OnDoubleClick(EventArgs e) { SelectAll(); base.OnDoubleClick(e); }

    protected override void OnGotFocus(EventArgs e) { Wake(); base.OnGotFocus(e); }

    protected override void OnLostFocus(EventArgs e)
    {
        blink.Stop();
        allSelected = false;
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = new RectangleF(0, 0, Width, Height);
        Theme.FillRound(g, readOnly && Hover ? Theme.Surface : Theme.Field, r, S(8));
        Theme.DrawRound(g, Focused && !readOnly ? Theme.AccentHover : readOnly && Hover ? Theme.BorderStrong : Theme.Border, r, S(8));

        int left = TextLeft, avail = Width - left - RightPadding;
        if (TrailingGlyph != null)
            Theme.Glyph(g, TrailingGlyph, 9f, (readOnly && Hover) || hoverGlyph ? Theme.Text : Theme.TextDim, GlyphRect);
        if (readOnly)
        {
            TextRenderer.DrawText(g, Text.Length > 0 ? Text : Placeholder, Font, new Rectangle(left, 0, avail, Height),
                Text.Length > 0 ? Theme.Text : Theme.TextFaint, Theme.LeftMiddle);
            return;
        }
        if (Text.Length == 0 && !Focused)
        {
            TextRenderer.DrawText(g, Placeholder, Font, new Rectangle(left, 0, avail, Height), Theme.TextFaint, Theme.LeftMiddle);
            return;
        }

        // Keep the caret visible when the text is wider than the box.
        int caretX = TextWidth(Text[..caret]);
        if (caretX - scroll > avail) scroll = caretX - avail;
        if (caretX - scroll < 0) scroll = caretX;
        if (TextWidth(Text) - scroll < avail) scroll = Math.Max(0, TextWidth(Text) - avail);

        g.SetClip(new Rectangle(left - 1, 0, avail + 2, Height));
        int th = Font.Height, ty = (Height - th) / 2;
        if (allSelected && Focused)
            Theme.FillRound(g, Theme.AccentSoft, new RectangleF(left - scroll - S(2), ty, TextWidth(Text) + S(4), th), S(3));
        TextRenderer.DrawText(g, Text, Font, new Point(left - scroll, ty), Theme.Text, Measure);
        if (Focused && caretOn && !allSelected)
        {
            using var b = new SolidBrush(Theme.Text);
            g.FillRectangle(b, left - scroll + caretX, ty + S(1), Math.Max(1, S(1)), th - S(2));
        }
        g.ResetClip();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) blink.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Select box: shows the chosen item; the arrow (or a click on the field) toggles a dark list.
/// With AllowCustom, a "Custom…" entry turns the field into a text box for any value.
/// </summary>
sealed class DropdownSelect : TextInput
{
    /// <summary>A list entry. Value null = separator. MenuLabel (optional) is shown in the list instead of Label.</summary>
    public sealed record Item(string Label, string Value, string MenuLabel = null)
    {
        public static readonly Item Separator = new("-", null);
    }

    readonly List<Item> items = new();
    string selected, customText = "";
    bool custom;
    ContextMenuStrip menu;
    bool closedByThisClick; // the mouse press that just closed the list landed on this field

    public DropdownSelect()
    {
        RightPadding = S(44);
        TrailingGlyph = "\uE70D";
        ReadOnly = true;
        Placeholder = SelectPlaceholder;
        Click += (_, _) => { if (!custom) Toggle(); };          // select mode: the whole field opens the list
        TrailingClick += (_, _) => { if (custom) Toggle(); };   // custom mode: only the arrow does
        MouseUp += (_, _) => closedByThisClick = false; // that click is over either way
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Down) || (!custom && e.KeyCode == Keys.Space))
            {
                Toggle();
                e.Handled = true;
            }
        };
    }

    public event EventHandler SelectionChanged;
    public bool AllowCustom { get; set; }
    public bool IsCustom => custom;
    public string SelectPlaceholder { get; set; } = "Choose…";
    public string CustomPlaceholder { get; set; } = "Type the program's name, e.g. cs2";

    /// <summary>The chosen item's value, or the typed text in custom mode (null when nothing is chosen).</summary>
    public string Value
    {
        get => custom ? Text.Trim() : selected;
        set
        {
            var match = string.IsNullOrEmpty(value) ? null : Find(value);
            if (match != null || string.IsNullOrEmpty(value) || !AllowCustom) Pick(match?.Value, raise: false);
            else { customText = value; PickCustom(focus: false, raise: false); }
        }
    }

    public void SetItems(IEnumerable<Item> list)
    {
        items.Clear();
        items.AddRange(list);
        if (!custom) Pick(selected != null ? Find(selected)?.Value : null, raise: false);
    }

    Item Find(string value) =>
        items.FirstOrDefault(i => i.Value != null && i.Value.Equals(value, StringComparison.OrdinalIgnoreCase));

    void Pick(string value, bool raise = true)
    {
        if (custom) customText = Text;
        custom = false;
        selected = value;
        ReadOnly = true;
        Placeholder = SelectPlaceholder;
        Text = value == null ? "" : Find(value)?.Label ?? value;
        if (raise) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    void PickCustom(bool focus = true, bool raise = true)
    {
        custom = true;
        selected = null;
        ReadOnly = false;
        Placeholder = CustomPlaceholder;
        Text = customText;
        if (focus) { Focus(); SelectAll(); }
        if (raise) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    void Toggle()
    {
        if (menu != null) { menu.Close(); return; }
        // Clicking the field while the list is open: Windows closes the list on the mouse press,
        // then this click arrives. Treat it as "close" (already done) instead of reopening.
        // No timer, so you can open/close as fast as you click.
        if (closedByThisClick) { closedByThisClick = false; return; }

        var m = menu = DarkMenu.Create(Width);
        m.Closed += (_, e) =>
        {
            menu = null;
            closedByThisClick = e.CloseReason == ToolStripDropDownCloseReason.AppClicked &&
                                ClientRectangle.Contains(PointToClient(Cursor.Position));
            BeginInvoke(m.Dispose);
        };
        foreach (var it in items)
        {
            if (it.Value == null) { m.Items.Add(new ToolStripSeparator()); continue; }
            var v = it.Value;
            DarkMenu.Add(m, it.MenuLabel ?? it.Label, !custom && string.Equals(selected, v, StringComparison.OrdinalIgnoreCase), () => Pick(v));
        }
        if (AllowCustom)
        {
            if (m.Items.Count > 0) m.Items.Add(new ToolStripSeparator());
            DarkMenu.Add(m, "Custom…", custom, () => PickCustom());
        }
        if (m.Items.Count == 0) DarkMenu.Add(m, "Nothing to choose", false, null);
        m.Show(this, new Point(0, Height + S(4)));
    }
}
/// <summary>Small pill. With removable = true it shows a × and a click means "remove".</summary>
sealed class Chip : PaintedControl
{
    readonly string glyph;
    readonly bool removable;
    bool selected;

    public Chip(string text, string glyph = null, bool removable = false)
    {
        Text = text;
        this.glyph = glyph;
        this.removable = removable;
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 0, S(6), S(6));
        int w = TextRenderer.MeasureText(text, Theme.Body).Width + S(20) + (glyph != null ? S(18) : 0) + (removable ? S(16) : 0);
        Size = new Size(w, S(28));
        if (removable) Theme.SetTip(this, "Remove");
    }

    public bool Selected { get => selected; set { if (selected != value) { selected = value; Invalidate(); } } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = new RectangleF(0, 0, Width, Height);
        var fill = selected ? Theme.AccentSoft : Pressed ? Theme.SurfacePressed : Hover ? Theme.SurfaceHover : Theme.Surface;
        Theme.FillRound(g, fill, r, Height / 2f);
        Theme.DrawRound(g, selected ? Theme.Accent : Theme.Border, r, Height / 2f);
        var fg = selected ? Theme.AccentText : Hover ? Theme.Text : Theme.TextDim;
        int x = S(10);
        if (glyph != null)
        {
            Theme.Glyph(g, glyph, 8.5f, fg, new Rectangle(x, 0, S(14), Height));
            x += S(18);
        }
        int textRight = Width - S(8) - (removable ? S(16) : 0);
        TextRenderer.DrawText(g, Text, Theme.Body, new Rectangle(x, 0, textRight - x, Height), fg, Theme.LeftMiddle);
        if (removable)
            Theme.Glyph(g, "", 7f, Hover ? Theme.Danger : Theme.TextFaint, new Rectangle(Width - S(26), 0, S(18), Height));
    }
}
