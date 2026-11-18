using System.Diagnostics;

namespace Auxer;

/// <summary>Game mode settings: which app to lower, how far, and what counts as "playing".</summary>
sealed class GameModeDialog : ThemedForm
{
    static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Auxer", "explorer", "ApplicationFrameHost", "TextInputHost", "SystemSettings", "ShellExperienceHost",
        "StartMenuExperienceHost", "SearchHost", "LockApp", "Taskmgr",
    };

    readonly DropdownSelect music, addGame;
    readonly Stepper level;
    readonly ToggleSwitch fullscreen;
    readonly BgFlowPanel chips;
    readonly TextLabel error;
    readonly FlatButton save, cancel;
    readonly List<string> games;
    readonly int width, pad, listTop;

    public GameModeSettings Result { get; private set; }

    public GameModeDialog(GameModeSettings current, IList<string> audioApps) : base(minimizeButton: false)
    {
        Text = "Game mode";
        Icon = AppIcon.Window;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        games = new List<string>(current.Games);

        width = S(500);
        pad = S(24);
        int w = width - pad * 2, y = TitleHeight + S(18);

        var intro = new TextLabel("Turns your music down while you play, and back up when you leave the game.", Theme.Small, Theme.TextDim)
        { Wrap = true, Location = new Point(pad, y) };
        intro.Fit(w);
        Controls.Add(intro);
        y += intro.Height + S(18);

        // Lower [app] to [level]
        int rightW = S(150), gap = S(14), leftW = w - rightW - gap;
        Caption("Lower", pad, y, leftW);
        Caption("To", pad + leftW + gap, y, rightW);
        y += S(22);
        music = new DropdownSelect { AllowCustom = true, SelectPlaceholder = "Choose an app", Bounds = new Rectangle(pad, y, leftW, S(38)) };
        var names = audioApps.Append(current.MusicApp).Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        music.SetItems(names.Select(n => new DropdownSelect.Item(Theme.Pretty(n), n)));
        music.Value = current.MusicApp;
        Controls.Add(music);
        level = new Stepper { Bounds = new Rectangle(pad + leftW + gap, y, rightW, S(38)) };
        level.Minimum = 0;
        level.Value = current.Level;
        Controls.Add(level);
        y += S(38) + S(20);

        // While playing: any fullscreen app, plus optional windowed games
        Caption("While playing", pad, y, w);
        y += S(22);
        var fsRow = new SegmentRow(56) { Location = new Point(pad, y), Width = w };
        fsRow.Add(new TwoLineLabel { Title = "Any fullscreen app", Description = "Covers most games, and fullscreen video" }, 0, stretch: true);
        fullscreen = fsRow.Add(new ToggleSwitch(), S(42), separator: false);
        fullscreen.Checked = current.FullscreenApps;
        Controls.Add(fsRow);
        y += fsRow.Height + S(10);

        addGame = new DropdownSelect
        {
            AllowCustom = true,
            SelectPlaceholder = "Add a game that runs in a window…",
            CustomPlaceholder = "Type the game's program name, e.g. cs2",
            Bounds = new Rectangle(pad, y, w, S(38)),
        };
        addGame.SelectionChanged += (_, _) =>
        {
            if (addGame.IsCustom || addGame.Value == null) return;
            var picked = addGame.Value;
            BeginInvoke(() => { AddGame(picked); addGame.Value = null; });
        };
        Controls.Add(addGame);
        listTop = y + S(38) + S(10);

        chips = new BgFlowPanel { Location = new Point(pad, listTop), Size = new Size(w, 0), Margin = Padding.Empty, Padding = Padding.Empty };
        Controls.Add(chips);

        error = new TextLabel("", Theme.Small, Theme.Danger) { Size = new Size(w, S(18)) };
        Controls.Add(error);
        save = new FlatButton("Save", primary: true);
        cancel = new FlatButton("Cancel") { DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => Accept();
        Controls.Add(save);
        Controls.Add(cancel);
        AcceptButton = save;
        CancelButton = cancel;

        RebuildChips();
    }

    void Caption(string text, int x, int y, int w) =>
        Controls.Add(new TextLabel(text, Theme.Small, Theme.TextDim) { Bounds = new Rectangle(x, y, w, S(18)) });

    /// <summary>Programs with a visible window right now (likely game candidates).</summary>
    IEnumerable<string> RunningApps()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { if (p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0) names.Add(p.ProcessName); }
            catch { }
            finally { p.Dispose(); }
        }
        return names.Where(n => !NotGames.Contains(n) && !games.Contains(n, StringComparer.OrdinalIgnoreCase)
                                && !n.Equals(music.Value, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
    }

    void AddGame(string name)
    {
        name = (name ?? "").Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0 || games.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
        games.Add(name);
        RebuildChips();
    }

    void RebuildChips()
    {
        chips.SuspendLayout();
        foreach (var c in chips.Controls.Cast<Control>().ToList()) c.Dispose();
        foreach (var g in games)
        {
            var chip = new Chip(Theme.Pretty(g), removable: true) { Tag = g };
            chip.Click += (_, _) => BeginInvoke(() => { games.Remove((string)chip.Tag); RebuildChips(); });
            chips.Controls.Add(chip);
        }
        chips.ResumeLayout();
        addGame.SetItems(RunningApps().Select(n => new DropdownSelect.Item(Theme.Pretty(n), n)));
        Relayout();
    }

    /// <summary>The game list grows as games are added; move the bottom part and resize the dialog.</summary>
    void Relayout()
    {
        int w = width - pad * 2;
        int h = games.Count == 0 ? 0 : chips.GetPreferredSize(new Size(w, 0)).Height;
        chips.Size = new Size(w, h);
        int y = listTop + h + (h > 0 ? S(2) : 0);
        error.Location = new Point(pad, y);
        y += S(26);
        save.Location = new Point(width - pad - save.Width, y);
        cancel.Location = new Point(save.Left - S(8) - cancel.Width, y);
        Size = new Size(width, y + save.Height + pad);
        Invalidate(true); // repaint everything, including the area the dialog just grew into
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ActiveControl = null;
    }

    void Accept()
    {
        if (addGame.IsCustom) AddGame(addGame.Text); // a typed name counts even without pressing anything else
        var app = (music.Value ?? "").Trim();
        if (app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) app = app[..^4];
        if (app.Length == 0)
        {
            error.Text = "Choose the app to turn down.";
            return;
        }
        Result = new GameModeSettings
        {
            Enabled = true,
            MusicApp = app,
            Level = level.Value,
            FullscreenApps = fullscreen.Checked,
            Games = games.ToList(),
        };
        DialogResult = DialogResult.OK;
    }
}
