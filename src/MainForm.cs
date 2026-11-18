using System.Diagnostics;

namespace Auxer;

/// <summary>Main window: Mixer, Keybinds and Settings pages behind icon tabs in the title bar.</summary>
sealed class MainForm : ThemedForm
{
    readonly TrayApp app;
    readonly IconButton[] nav;
    readonly Control[] pages;
    readonly PageHeader mixerHeader, keysHeader;
    readonly StackPanel mixerList, keyList;
    readonly EmptyState mixerEmpty, keysEmpty;
    readonly ToggleSwitch tglOverlay, tglStartup, tglPause, tglGame;
    readonly DropdownSelect popupPosition;
    readonly SegmentedControl popupDuration;
    readonly TwoLineLabel gameSummary, hiddenSummary;
    readonly FlatButton showHidden;
    static readonly int[] Durations = { 500, 1400, 2500 }; // Short / Normal / Long (ms)
    readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 1500 };
    readonly List<MixerRow> rows = new();
    readonly string version;
    List<AudioApp> snapshot = new();
    bool syncing;

    public MainForm(TrayApp app) : base(minimizeButton: true, statusBar: true)
    {
        this.app = app;
        Text = "Auxer";
        Icon = AppIcon.Window;
        StartPosition = FormStartPosition.CenterScreen;

        // Fixed size: tall enough for exactly VisibleRows cards, so each scroll step reveals one whole card.
        const int VisibleRows = 5;
        int listHeight = VisibleRows * S(58) + (VisibleRows - 1) * S(8);
        int height = TitleHeight + 1 + StatusHeight + S(8) + S(12) + S(58) + listHeight; // title, divider, status, padding, page header
        Resizable = false;
        Size = MinimumSize = MaximumSize = new Size(S(720), height);
        version = "v" + Application.ProductVersion.Split('+')[0];

        nav = new[]
        {
            AddTitleButton("", "Mixer  (Ctrl+1)"),
            AddTitleButton("", "Keybinds  (Ctrl+2)"),
            AddTitleButton("", "Settings  (Ctrl+3)"),
        };

        var host = new BgPanel { Dock = DockStyle.Fill, Padding = new Padding(S(14), S(8), S(14), S(12)) };

        // ---------- Mixer ----------
        mixerHeader = new PageHeader("Mixer", "Apps with open audio");
        mixerList = new StackPanel { Dock = DockStyle.Fill };
        mixerEmpty = new EmptyState("", "Nothing is playing", "Apps show up here once they open audio, e.g. Spotify after pressing play.");
        var mixerPage = Page(mixerHeader, mixerList, mixerEmpty);

        // ---------- Keybinds ----------
        keysHeader = new PageHeader("Keybinds", "Global hotkeys, work in games too");
        var add = new FlatButton("Add keybind", primary: true, glyph: "");
        add.Click += (_, _) => AddBinding();
        keysHeader.Controls.Add(add);
        keyList = new StackPanel { Dock = DockStyle.Fill };
        keysEmpty = new EmptyState("", "No keybinds yet", "Click “Add keybind” to create one.");
        var keysPage = Page(keysHeader, keyList, keysEmpty);

        // ---------- Settings ----------
        var settingsList = new StackPanel { Dock = DockStyle.Fill };

        tglOverlay = ToggleRow(settingsList, "Volume popup", "Show a small popup when a hotkey changes the volume");

        var posRow = SettingRow(settingsList, "Popup position", "Where the popup appears on screen");
        popupPosition = posRow.Add(new DropdownSelect(), S(190), separator: false);
        popupPosition.SetItems(new[]
        {
            new DropdownSelect.Item("Top center", nameof(OverlayPosition.TopCenter)),
            new DropdownSelect.Item("Top left", nameof(OverlayPosition.TopLeft)),
            new DropdownSelect.Item("Top right", nameof(OverlayPosition.TopRight)),
            new DropdownSelect.Item("Bottom center", nameof(OverlayPosition.BottomCenter)),
            new DropdownSelect.Item("Bottom left", nameof(OverlayPosition.BottomLeft)),
            new DropdownSelect.Item("Bottom right", nameof(OverlayPosition.BottomRight)),
        });

        var durRow = SettingRow(settingsList, "Popup duration", "How long the popup stays on screen");
        popupDuration = durRow.Add(new SegmentedControl("Short", "Normal", "Long"), S(250), separator: false);

        var gameRow = new SegmentRow(58);
        gameSummary = gameRow.Add(new TwoLineLabel { Title = "Game mode" }, 0, stretch: true);
        var editGame = new FlatButton("Edit");
        editGame.Click += (_, _) => BeginInvoke(EditGameMode);
        gameRow.Add(editGame, editGame.Width);
        tglGame = gameRow.Add(new ToggleSwitch(), S(42), separator: false);
        settingsList.Items.Add(gameRow);

        tglStartup = ToggleRow(settingsList, "Start with Windows", "Launch quietly in the tray when you sign in");
        tglPause = ToggleRow(settingsList, "Pause hotkeys", "Turn all hotkeys off for now (also in the tray menu)");

        var hiddenRow = new SegmentRow(58);
        hiddenSummary = hiddenRow.Add(new TwoLineLabel { Title = "Hidden apps" }, 0, stretch: true);
        showHidden = new FlatButton("Show…");
        showHidden.Click += (_, _) => ShowHiddenMenu();
        hiddenRow.Add(showHidden, showHidden.Width, separator: false);
        settingsList.Items.Add(hiddenRow);

        var fileRow = SettingRow(settingsList, "Settings file", @"%APPDATA%\Auxer\settings.json");
        var open = new FlatButton("Open folder", glyph: "\uE838");
        open.Click += (_, _) => OpenSettingsFolder();
        fileRow.Add(open, open.Width, separator: false);

        var settingsPage = Page(new PageHeader("Settings", "Auxer " + version), settingsList, null);

        try { tglStartup.Checked = Startup.IsEnabled; } catch { tglStartup.Enabled = false; }
        SyncSettings();

        tglOverlay.CheckedChanged += (_, _) => { if (syncing) return; app.Settings.ShowOverlay = tglOverlay.Checked; app.SettingsSaved(); };
        popupPosition.SelectionChanged += (_, _) =>
        {
            if (syncing || !Enum.TryParse<OverlayPosition>(popupPosition.Value, out var pos)) return;
            app.Settings.OverlayPosition = pos;
            app.SettingsSaved();
            app.PreviewOverlay();
        };
        popupDuration.SelectedIndexChanged += (_, _) =>
        {
            if (syncing) return;
            app.Settings.OverlayDurationMs = Durations[popupDuration.SelectedIndex];
            app.SettingsSaved();
            app.PreviewOverlay();
        };
        tglGame.CheckedChanged += (_, _) =>
        {
            if (syncing) return;
            app.Settings.GameMode.Enabled = tglGame.Checked;
            app.ApplyGameMode();
        };
        tglStartup.CheckedChanged += (_, _) =>
        {
            if (syncing) return;
            try { Startup.Set(tglStartup.Checked); }
            catch { syncing = true; tglStartup.Checked = !tglStartup.Checked; syncing = false; }
        };
        tglPause.CheckedChanged += (_, _) => { if (!syncing) app.SetPaused(tglPause.Checked); };
        pages = new Control[] { mixerPage, keysPage, settingsPage };
        foreach (var p in pages) host.Controls.Add(p);
        Controls.Add(host);
        for (int i = 0; i < nav.Length; i++)
        {
            int page = i;
            nav[i].Click += (_, _) => ShowPage(page);
        }
        ShowPage(0);

        refreshTimer.Tick += (_, _) => RefreshMixer();
        app.BindingsChanged += OnBindingsChanged;
        app.SettingsChanged += SyncSettings;
        OnBindingsChanged();
    }

    static Control Page(PageHeader header, Control list, Control empty)
    {
        var page = new BgPanel { Dock = DockStyle.Fill, Visible = false };
        page.Controls.Add(list);
        if (empty != null) { empty.Visible = false; page.Controls.Add(empty); empty.BringToFront(); }
        page.Controls.Add(header);
        return page;
    }

    /// <summary>Settings card with a title/description on the left; the caller adds a control on the right.</summary>
    static SegmentRow SettingRow(StackPanel list, string title, string description)
    {
        var row = new SegmentRow(58);
        row.Add(new TwoLineLabel { Title = title, Description = description }, 0, stretch: true);
        list.Items.Add(row);
        return row;
    }

    static ToggleSwitch ToggleRow(StackPanel list, string title, string description) =>
        SettingRow(list, title, description).Add(new ToggleSwitch(), S(42), separator: false);

    /// <summary>Puts the current settings into the Settings page controls.</summary>
    void SyncSettings()
    {
        var st = app.Settings;
        syncing = true;
        tglOverlay.Checked = st.ShowOverlay;
        popupPosition.Value = st.OverlayPosition.ToString();
        popupDuration.SelectedIndex = st.OverlayDurationMs <= 1000 ? 0 : st.OverlayDurationMs <= 1800 ? 1 : 2;
        popupPosition.Enabled = popupDuration.Enabled = st.ShowOverlay;
        tglGame.Checked = st.GameMode.Enabled;
        gameSummary.Description = GameSummary(st.GameMode);
        Theme.SetTip(gameSummary, gameSummary.Description);
        tglPause.Checked = app.Paused;
        hiddenSummary.Description = st.HiddenApps.Count == 0
            ? "Right-click an app in the mixer to hide it"
            : string.Join(", ", st.HiddenApps.Select(Theme.Pretty));
        showHidden.Enabled = st.HiddenApps.Count > 0;
        syncing = false;
    }

    static string GameSummary(GameModeSettings g)
    {
        var when = new List<string>();
        if (g.FullscreenApps) when.Add("a fullscreen app");
        when.AddRange(g.Games.Select(Theme.Pretty));
        if (when.Count == 0) return "Click Edit to choose when your music is turned down";
        return $"Lowers {Theme.Pretty(g.MusicApp)} to {g.Level}% while {string.Join(" or ", when)} is in front";
    }

    void EditGameMode()
    {
        var names = snapshot.Select(a => a.Name).ToList();
        using var dlg = new GameModeDialog(app.Settings.GameMode, names);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        app.Settings.GameMode = dlg.Result;
        app.ApplyGameMode();
    }

    void ShowHiddenMenu()
    {
        var menu = DarkMenu.Create(S(200));
        foreach (var name in app.Settings.HiddenApps.ToList())
            DarkMenu.Add(menu, $"Show {Theme.Pretty(name)}", false, () => app.UnhideApp(name));
        if (app.Settings.HiddenApps.Count > 1)
        {
            menu.Items.Add(new ToolStripSeparator());
            DarkMenu.Add(menu, "Show all", false, () =>
            {
                app.Settings.HiddenApps.Clear();
                app.SettingsSaved();
                RefreshMixer();
            });
        }
        DarkMenu.ShowOnce(menu, showHidden, new Point(0, showHidden.Height + S(4)));
    }
    void ShowPage(int index)
    {
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i].Visible = i == index;
            nav[i].Checked = i == index;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is (Keys.Control | Keys.D1) or (Keys.Control | Keys.D2) or (Keys.Control | Keys.D3))
        {
            ShowPage((keyData & Keys.KeyCode) - Keys.D1);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RefreshMixer();
        refreshTimer.Start();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ActiveControl = null;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        refreshTimer.Stop();
        refreshTimer.Dispose();
        app.BindingsChanged -= OnBindingsChanged;
        app.SettingsChanged -= SyncSettings;
        foreach (var a in snapshot) a.Dispose();
        snapshot.Clear();
        base.OnFormClosed(e);
    }

    void OpenSettingsFolder()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Auxer");
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    // ---------- Mixer ----------

    public void RefreshMixer()
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
            mixerList.BeginUpdate();
            foreach (var r in rows) r.Dispose();
            rows.Clear();
            foreach (var a in snapshot)
            {
                var row = new MixerRow(a, n => BeginInvoke(() => AddBinding(n)), n => app.HideApp(n));
                rows.Add(row);
                mixerList.Items.Add(row);
                row.Sync();
            }
            mixerList.EndUpdate();
            mixerEmpty.Visible = rows.Count == 0;
        }
        mixerHeader.Subtitle = rows.Count switch
        {
            0 => "No apps have audio open",
            1 => "1 app with open audio",
            var n => $"{n} apps with open audio",
        };
        foreach (var a in old) a.Dispose();
    }

    // ---------- Keybinds ----------

    void OnBindingsChanged()
    {
        if (IsDisposed) return;
        keyList.BeginUpdate();
        foreach (Control c in keyList.Items.Cast<Control>().ToList()) c.Dispose();
        for (int i = 0; i < app.Settings.Bindings.Count; i++)
            keyList.Items.Add(new KeyRow(this, app.Settings.Bindings[i], i));
        keyList.EndUpdate();
        keysEmpty.Visible = app.Settings.Bindings.Count == 0;

        int active = app.Settings.Bindings.Count(b => b.Status == "Active");
        int broken = app.Settings.Bindings.Count(b => b.Status != "Active" && b.Status != "Paused");
        keysHeader.Subtitle = "Global hotkeys, work in games too";
        if (app.Paused)
            SetStatus("Hotkeys paused", version, Theme.Warning);
        else
            SetStatus(active == 1 ? "1 hotkey active" : $"{active} hotkeys active", version, broken > 0 ? Theme.Danger : Theme.Success);

        syncing = true;
        tglPause.Checked = app.Paused;
        syncing = false;
    }

    internal void AddBinding(string presetTarget = null) => EditBinding(-1, presetTarget);

    internal void EditBinding(int index, string presetTarget = null)
    {
        var existing = index >= 0 ? app.Settings.Bindings[index] : null;
        var appNames = snapshot.Select(a => a.Name)
            .Concat(app.Settings.Bindings.Where(b => !b.IsFocused).Select(b => b.Target))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        app.SuspendHotkeys(); // so the combo being recorded isn't swallowed by our own hotkeys
        try
        {
            using var dlg = new BindingDialog(existing, appNames, presetTarget);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                if (index >= 0) app.Settings.Bindings[index] = dlg.Result;
                else app.Settings.Bindings.Add(dlg.Result);
                app.Settings.Save();
            }
        }
        finally
        {
            app.ApplyHotkeys();
        }
    }

    internal void RemoveBinding(int index)
    {
        app.Settings.Bindings.RemoveAt(index);
        app.Settings.Save();
        app.ApplyHotkeys();
    }
}

/// <summary>Mixer card: name | slider | 45% | mute, add-hotkey.</summary>
sealed class MixerRow : SegmentRow
{
    public AudioApp App;
    readonly TwoLineLabel name;
    readonly VolumeSlider slider;
    readonly ValueLabel value;
    readonly IconButton mute;
    bool syncing;
    long lastUserChange;

    /// <param name="addHotkey">Shows an "add hotkey" button that calls this with the app name; null hides it.</param>
    /// <param name="hide">Right-click menu "Hide" calls this with the app name.</param>
    /// <param name="compact">Smaller card for the tray flyout.</param>
    public MixerRow(AudioApp app, Action<string> addHotkey = null, Action<string> hide = null, bool compact = false) : base(compact ? 52 : 58)
    {
        App = app;
        name = Add(new TwoLineLabel(), S(compact ? 104 : 150), stretch: true);
        slider = Add(new VolumeSlider(), 0);
        value = Add(new ValueLabel(), S(50), separator: false, stretch: true);
        mute = Add(new IconButton("", "Mute"), S(34));

        slider.Scroll += (_, _) =>
        {
            if (syncing) return;
            lastUserChange = Environment.TickCount64;
            App.SetVolume(slider.Value / 100f);
            value.Set(slider.Value, App.Muted);
        };
        mute.Click += (_, _) =>
        {
            lastUserChange = 0;
            App.SetMute(!App.Muted);
            Sync();
        };
        if (addHotkey != null)
        {
            var bind = Add(new IconButton("\uE765", "Add a hotkey for " + Theme.Pretty(app.Name)), S(34), separator: false);
            bind.Click += (_, _) => addHotkey(App.Name);
        }

        // Right-click anywhere on the card: small menu with "Hide" (keeps the card itself uncluttered).
        if (hide != null)
        {
            void ShowMenu(Control source, Point at)
            {
                var menu = DarkMenu.Create();
                var n = App.Name;
                DarkMenu.Add(menu, $"Hide {Theme.Pretty(n)}", false, () => BeginInvoke(() => hide(n)));
                DarkMenu.ShowOnce(menu, source, at);
            }
            foreach (Control c in new Control[] { this, name, value })
                c.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowMenu((Control)s, e.Location); };
        }
    }

    public void Sync()
    {
        // Don't fight the user while they're dragging.
        if (slider.Dragging || Environment.TickCount64 - lastUserChange < 1000) return;
        syncing = true;
        int pct = Math.Clamp(App.Percent, 0, 100);
        bool muted = App.Muted;
        name.Title = Theme.Pretty(App.Name);
        name.Description = muted ? "Muted" : App.IsActive ? "Playing" : "Idle";
        slider.Value = pct;
        slider.Muted = muted;
        value.Set(pct, muted);
        mute.Glyph = muted ? "" : "";
        mute.GlyphColor = muted ? Theme.Danger : null;
        Theme.SetTip(mute, muted ? "Unmute" : "Mute");
        syncing = false;
    }
}

/// <summary>Keybind card: [Ctrl][Alt][Down] | app | action | status | edit, delete.</summary>
sealed class KeyRow : SegmentRow
{
    public KeyRow(MainForm form, KeyBinding b, int index) : base(58)
    {
        var caps = Add(new KeyCaps { Combo = Hotkey.Parse(b.Hotkey).ToString() }, S(160), stretch: true);
        var target = Add(new TwoLineLabel
        {
            Title = b.IsFocused ? "Focused app" : Theme.Pretty(b.Target),
            Description = b.IsFocused ? "Whatever window is in front" : null,
        }, 0, stretch: true);
        var action = Add(new TwoLineLabel { Title = b.ActionText, TitleFont = Theme.Body, TitleColor = Theme.TextDim }, S(120), stretch: true);
        bool ok = b.Status is "Active" or "Paused";
        var status = Add(new StatusLabel
        {
            Text = b.Status == "Key combo already in use" ? "In use" : b.Status,
            Dot = b.Status switch { "Active" => Theme.Success, "Paused" => Theme.Warning, _ => Theme.Danger },
        }, S(90), stretch: true);
        if (!ok) Theme.SetTip(status, b.Status == "Key combo already in use"
            ? "Another app (or another keybind) already uses this combo. Pick a different one."
            : b.Status);
        var edit = Add(new IconButton("", "Edit"), S(32));
        var delete = Add(new IconButton("", "Remove"), S(32), separator: false);

        // Deferred: these rebuild the list, which disposes this row.
        edit.Click += (_, _) => form.BeginInvoke(() => form.EditBinding(index));
        delete.Click += (_, _) => form.BeginInvoke(() => form.RemoveBinding(index));
        foreach (Control c in new Control[] { this, caps, target, action, status })
            c.DoubleClick += (_, _) => form.BeginInvoke(() => form.EditBinding(index));
    }
}
