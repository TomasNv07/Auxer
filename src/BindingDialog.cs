namespace Auxer;

sealed class BindingDialog : ThemedForm
{
    const string FocusedText = "Focused app";
    static readonly string[] ActionNames = { "Volume down", "Volume up", "Toggle mute", "Set volume" };

    readonly DropdownSelect appField;
    readonly SegmentedControl action;
    readonly Stepper amount;
    readonly TextLabel amountCaption, error;
    readonly HotkeyInput hotkey;

    public KeyBinding Result { get; private set; }

    public BindingDialog(KeyBinding existing, IList<string> appNames, string presetTarget) : base(minimizeButton: false)
    {
        Text = existing == null ? "Add keybind" : "Edit keybind";
        Icon = AppIcon.Window;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var b = existing?.Clone() ?? new KeyBinding { Target = presetTarget ?? "", Hotkey = "" };
        int W = S(500), pad = S(24), w = W - pad * 2;
        int y = TitleHeight + S(22);

        // App: pick from the list; "Custom…" makes the field editable
        Caption("App", pad, ref y);
        appField = new DropdownSelect { AllowCustom = true, SelectPlaceholder = "Choose an app", Bounds = new Rectangle(pad, y, w, S(38)) };
        var items = new List<DropdownSelect.Item> { new(FocusedText, FocusedText, "Focused app  (whatever window is in front)") };
        if (appNames.Count > 0) items.Add(DropdownSelect.Item.Separator);
        items.AddRange(appNames.Select(n => new DropdownSelect.Item(Theme.Pretty(n), n)));
        appField.SetItems(items);
        appField.Value = b.IsFocused ? FocusedText : b.Target;
        Controls.Add(appField);
        y += appField.Height + S(18);

        // Action
        Caption("Action", pad, ref y);
        action = new SegmentedControl(ActionNames);
        action.SetBounds(pad, y, w, S(38));
        action.SelectedIndex = (int)b.Action;
        Controls.Add(action);
        y += action.Height + S(18);

        // Amount + hotkey side by side
        int leftW = S(150), gap = S(14), rightX = pad + leftW + gap, rightW = w - leftW - gap;
        amountCaption = new TextLabel("Step", Theme.Small, Theme.TextDim) { Bounds = new Rectangle(pad, y, leftW, S(18)) };
        Controls.Add(amountCaption);
        Controls.Add(new TextLabel("Hotkey", Theme.Small, Theme.TextDim) { Bounds = new Rectangle(rightX, y, rightW, S(18)) });
        y += S(22);

        amount = new Stepper { Bounds = new Rectangle(pad, y, leftW, S(38)) };
        amount.Value = Math.Clamp(b.Amount, 0, 100);
        Controls.Add(amount);

        hotkey = new HotkeyInput { Bounds = new Rectangle(rightX, y, rightW, S(38)), Value = Hotkey.Parse(b.Hotkey) };
        Controls.Add(hotkey);
        y += S(38) + S(14);

        var hint = new TextLabel("Choose Custom to type the program's name if it isn't listed (e.g. Discord, chrome, cs2).",
            Theme.Small, Theme.TextFaint) { Wrap = true, Location = new Point(pad, y) };
        hint.Fit(w);
        Controls.Add(hint);
        y += hint.Height + S(10);

        error = new TextLabel("", Theme.Small, Theme.Danger) { Bounds = new Rectangle(pad, y, w, S(18)) };
        Controls.Add(error);
        y += S(26);
        appField.SelectionChanged += (_, _) => error.Text = "";

        var save = new FlatButton(existing == null ? "Add keybind" : "Save", primary: true);
        var cancel = new FlatButton("Cancel") { DialogResult = DialogResult.Cancel };
        save.Location = new Point(W - pad - save.Width, y);
        cancel.Location = new Point(save.Left - S(8) - cancel.Width, y);
        save.Click += (_, _) => Accept(b);
        Controls.Add(save);
        Controls.Add(cancel);
        y += save.Height + pad;

        Size = new Size(W, y);
        AcceptButton = save;
        CancelButton = cancel;
        action.SelectedIndexChanged += (_, _) => UpdateAmount();
        UpdateAmount();
    }

    void Caption(string text, int x, ref int y)
    {
        Controls.Add(new TextLabel(text, Theme.Small, Theme.TextDim) { Bounds = new Rectangle(x, y, S(200), S(18)) });
        y += S(22);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ActiveControl = null;
    }

    void UpdateAmount()
    {
        var a = (BindAction)action.SelectedIndex;
        amountCaption.Text = a switch { BindAction.SetVolume => "Level", BindAction.ToggleMute => "Amount (not used)", _ => "Step" };
        amount.Enabled = a != BindAction.ToggleMute;
        amount.Minimum = a == BindAction.SetVolume ? 0 : 1;
    }

    void Accept(KeyBinding b)
    {
        var name = appField.Value ?? "";
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0)
        {
            error.Text = appField.IsCustom ? "Type the program's name." : "Choose an app.";
            if (appField.IsCustom) appField.Focus();
            return;
        }
        if (hotkey.Value.IsEmpty)
        {
            error.Text = "Click “Click to set hotkey”, then press the key combo you want.";
            return;
        }

        b.Target = !appField.IsCustom && name == FocusedText ? KeyBinding.FocusedTarget : name;
        b.Action = (BindAction)action.SelectedIndex;
        b.Amount = amount.Value;
        b.Hotkey = hotkey.Value.ToString();
        Result = b;
        DialogResult = DialogResult.OK;
    }
}
