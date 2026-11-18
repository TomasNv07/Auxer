using System.Runtime.InteropServices;

namespace Auxer;

[Flags]
enum Mods : uint { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

/// <summary>A key + modifiers, stored in settings as text like "Ctrl+Alt+Down".</summary>
readonly record struct Hotkey(Mods Modifiers, Keys Key)
{
    static readonly Dictionary<Keys, string> Names = new()
    {
        [Keys.D0] = "0", [Keys.D1] = "1", [Keys.D2] = "2", [Keys.D3] = "3", [Keys.D4] = "4",
        [Keys.D5] = "5", [Keys.D6] = "6", [Keys.D7] = "7", [Keys.D8] = "8", [Keys.D9] = "9",
        [Keys.NumPad0] = "Num0", [Keys.NumPad1] = "Num1", [Keys.NumPad2] = "Num2", [Keys.NumPad3] = "Num3",
        [Keys.NumPad4] = "Num4", [Keys.NumPad5] = "Num5", [Keys.NumPad6] = "Num6", [Keys.NumPad7] = "Num7",
        [Keys.NumPad8] = "Num8", [Keys.NumPad9] = "Num9",
        [Keys.Add] = "NumPlus", [Keys.Subtract] = "NumMinus", [Keys.Multiply] = "NumMultiply",
        [Keys.Divide] = "NumDivide", [Keys.Decimal] = "NumDecimal",
        [Keys.PageUp] = "PageUp", [Keys.PageDown] = "PageDown", [Keys.Enter] = "Enter",
        [Keys.Back] = "Backspace", [Keys.CapsLock] = "CapsLock", [Keys.Scroll] = "ScrollLock",
        [Keys.PrintScreen] = "PrintScreen",
        [Keys.Oemplus] = "=", [Keys.OemMinus] = "-", [Keys.Oemcomma] = ",", [Keys.OemPeriod] = ".",
        [Keys.OemQuestion] = "/", [Keys.OemSemicolon] = ";", [Keys.OemQuotes] = "'", [Keys.Oemtilde] = "`",
        [Keys.OemOpenBrackets] = "[", [Keys.OemCloseBrackets] = "]", [Keys.OemPipe] = "\\",
        [Keys.OemBackslash] = "<>",
    };

    public bool IsEmpty => Key == Keys.None;

    public static string ModsText(Mods m)
    {
        var parts = new List<string>();
        if (m.HasFlag(Mods.Ctrl)) parts.Add("Ctrl");
        if (m.HasFlag(Mods.Alt)) parts.Add("Alt");
        if (m.HasFlag(Mods.Shift)) parts.Add("Shift");
        if (m.HasFlag(Mods.Win)) parts.Add("Win");
        return string.Join("+", parts);
    }

    public override string ToString()
    {
        if (IsEmpty) return "";
        var key = Names.TryGetValue(Key, out var n) ? n : Key.ToString();
        return Modifiers == Mods.None ? key : ModsText(Modifiers) + "+" + key;
    }

    public static Hotkey Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return default;

        var mods = Mods.None;
        foreach (var p in parts[..^1])
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= Mods.Ctrl; break;
                case "alt": mods |= Mods.Alt; break;
                case "shift": mods |= Mods.Shift; break;
                case "win": mods |= Mods.Win; break;
                default: return default;
            }
        }

        var last = parts[^1];
        foreach (var (k, name) in Names)
            if (name.Equals(last, StringComparison.OrdinalIgnoreCase)) return new Hotkey(mods, k);
        return Enum.TryParse<Keys>(last, true, out var key) && !int.TryParse(last, out _)
            ? new Hotkey(mods, key)
            : default;
    }
}

/// <summary>Global hotkeys via RegisterHotKey. They fire even while a fullscreen game has focus.</summary>
sealed class HotkeyManager : NativeWindow, IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const uint MOD_NOREPEAT = 0x4000;

    readonly Dictionary<int, Action> actions = new();
    int nextId = 1;

    public HotkeyManager()
    {
        // Message-only window: invisible, receives WM_HOTKEY.
        CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
    }

    /// <param name="repeat">true = keeps firing while the key is held (good for volume steps).</param>
    public bool Register(Hotkey hk, bool repeat, Action action)
    {
        int id = nextId++;
        uint mods = (uint)hk.Modifiers | (repeat ? 0 : MOD_NOREPEAT);
        if (!RegisterHotKey(Handle, id, mods, (uint)hk.Key)) return false;
        actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in actions.Keys) UnregisterHotKey(Handle, id);
        actions.Clear();
        nextId = 1;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && actions.TryGetValue((int)m.WParam, out var action))
        {
            action();
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

/// <summary>
/// Hotkey field with three states:
///   empty     – looks like a button: "Click to set hotkey"
///   recording – after the click, waits for a key combo (Esc or × cancels)
///   set       – shows the combo as key chips; × removes it and goes back to empty
/// </summary>
sealed class HotkeyInput : PaintedControl
{
    readonly IconButton clear = new("\uE711", "Remove") { GlyphSize = 9f, BackColor = Theme.Field };
    Hotkey value;
    string partial; // e.g. "Ctrl+Alt+…" while modifiers are held
    bool recording;

    public HotkeyInput()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Height = S(38);
        clear.Size = new Size(S(28), S(28));
        clear.Click += (_, _) => { value = default; StopRecording(); };
        Controls.Add(clear);
        UpdateState();
    }

    public Hotkey Value
    {
        get => value;
        set { this.value = value; partial = null; UpdateState(); }
    }

    void UpdateState()
    {
        if (clear == null) return;
        clear.Visible = recording || !value.IsEmpty;
        Theme.SetTip(clear, recording ? "Cancel" : "Remove hotkey");
        Cursor = !recording && value.IsEmpty ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    void StartRecording()
    {
        recording = true;
        partial = null;
        Focus();
        UpdateState();
    }

    void StopRecording()
    {
        recording = false;
        partial = null;
        UpdateState();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (clear == null) return; // base constructor can trigger a layout first
        clear.Location = new Point(Width - clear.Width - S(5), (Height - clear.Height) / 2);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (!recording && value.IsEmpty) StartRecording();
    }

    // While recording, every key (Tab, Enter, arrows…) is part of the combo.
    protected override bool IsInputKey(Keys keyData) => recording || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!recording)
        {
            if (e.KeyCode == Keys.Space && value.IsEmpty) { StartRecording(); e.Handled = true; }
            base.OnKeyDown(e);
            return;
        }

        // Handled = true also keeps a lone Alt press from opening the window menu.
        e.Handled = true;
        e.SuppressKeyPress = true;

        var mods = Mods.None;
        if (e.Control) mods |= Mods.Ctrl;
        if (e.Alt) mods |= Mods.Alt;
        if (e.Shift) mods |= Mods.Shift;
        if (GetKeyState(0x5B) < 0 || GetKeyState(0x5C) < 0) mods |= Mods.Win;

        var key = e.KeyCode;
        if (key == Keys.Escape && mods == Mods.None) { StopRecording(); return; }
        if (key is Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LWin or Keys.RWin
            or Keys.LShiftKey or Keys.RShiftKey or Keys.LControlKey or Keys.RControlKey or Keys.LMenu or Keys.RMenu)
        {
            partial = Hotkey.ModsText(mods) + "+…";
            Invalidate();
            return;
        }
        value = new Hotkey(mods, key);
        StopRecording();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        e.Handled = true; // swallow the Alt release after a combo so the window menu doesn't open
        if (recording && Control.ModifierKeys == Keys.None && partial != null) { partial = null; Invalidate(); }
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }

    protected override void OnLostFocus(EventArgs e)
    {
        if (recording && !clear.Focused) StopRecording();
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = new RectangleF(0, 0, Width, Height);
        bool idle = !recording && value.IsEmpty;
        Theme.FillRound(g, idle && Hover ? Theme.Surface : Theme.Field, r, S(8));
        Theme.DrawRound(g, recording ? Theme.Accent : idle && Hover ? Theme.BorderStrong : Theme.Border, r, S(8));

        int x = S(10), right = clear.Visible ? clear.Left - S(4) : Width - S(10);
        var area = new Rectangle(x + S(2), 0, right - x, Height);
        if (recording)
        {
            TextRenderer.DrawText(g, partial ?? "Press a key combo…", Theme.Body, area, Theme.AccentText, Theme.LeftMiddle);
        }
        else if (idle)
        {
            const string label = "Click to set hotkey";
            var fg = Hover || (Focused && ShowFocusCues) ? Theme.Text : Theme.TextDim;
            int tw = TextRenderer.MeasureText(g, label, Theme.Bold, Size.Empty, TextFormatFlags.NoPadding).Width;
            int gx = (Width - tw - S(22)) / 2;
            Theme.Glyph(g, "\uE765", 10f, fg, new Rectangle(gx, 0, S(16), Height));
            TextRenderer.DrawText(g, label, Theme.Bold, new Rectangle(gx + S(22), 0, tw + 2, Height), fg, Theme.LeftMiddle);
        }
        else
        {
            KeyCaps.DrawCaps(g, value.ToString().Split('+'), x, Height / 2, right);
        }
    }

    [DllImport("user32.dll")]
    static extern short GetKeyState(int vk);
}