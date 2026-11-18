namespace Auxer;

/// <summary>Minimal 0–100 slider in the app theme. Mouse drag, wheel and arrow keys.</summary>
sealed class VolumeSlider : PaintedControl
{
    int value;
    bool muted;

    /// <summary>Raised only for changes made by the user (mouse, wheel, keys).</summary>
    public event EventHandler Scroll;

    public bool Dragging { get; private set; }

    public VolumeSlider()
    {
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
        Height = S(30);
        Cursor = Cursors.Hand;
    }

    public int Value
    {
        get => value;
        set
        {
            value = Math.Clamp(value, 0, 100);
            if (this.value == value) return;
            this.value = value;
            Invalidate();
        }
    }

    public bool Muted { get => muted; set { if (muted != value) { muted = value; Invalidate(); } } }

    RectangleF Track => new(S(9), Height / 2f - S(2), Math.Max(1, Width - S(18)), S(4));

    void UserSet(int v)
    {
        v = Math.Clamp(v, 0, 100);
        if (v == value) return;
        value = v;
        Invalidate();
        Scroll?.Invoke(this, EventArgs.Empty);
    }

    void SetFromX(int x)
    {
        var t = Track;
        UserSet((int)Math.Round((x - t.Left) / t.Width * 100));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        Dragging = true;
        Capture = true;
        SetFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Dragging) SetFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Dragging = false;
        Capture = false;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        // Only adjust with the wheel after the slider was clicked; otherwise let the list scroll.
        if (!Focused) { base.OnMouseWheel(e); return; }
        UserSet(value + Math.Sign(e.Delta) * 2);
        if (e is HandledMouseEventArgs h) h.Handled = true;
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Left: case Keys.Down: UserSet(value - 1); break;
            case Keys.Right: case Keys.Up: UserSet(value + 1); break;
            case Keys.PageDown: UserSet(value - 10); break;
            case Keys.PageUp: UserSet(value + 10); break;
            case Keys.Home: UserSet(0); break;
            case Keys.End: UserSet(100); break;
        }
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var t = Track;
        float x = t.Left + t.Width * value / 100f;

        Theme.FillRound(g, Theme.BorderStrong, t, t.Height / 2);
        if (x - t.Left > 0)
            Theme.FillRound(g, muted ? Theme.TextFaint : Theme.Accent, t with { Width = Math.Max(t.Height, x - t.Left) }, t.Height / 2);

        bool active = Hover || Dragging || Focused;
        float r = active ? S(8) : S(7);
        float cy = t.Top + t.Height / 2;
        using (var glow = new SolidBrush(Color.FromArgb(active ? 60 : 0, Theme.Accent)))
            if (active) g.FillEllipse(glow, x - r - S(3), cy - r - S(3), (r + S(3)) * 2, (r + S(3)) * 2);
        using (var b = new SolidBrush(muted ? Theme.TextDim : Theme.Text))
            g.FillEllipse(b, x - r, cy - r, r * 2, r * 2);
    }
}
