using System.Runtime.InteropServices;

namespace Auxer;

/// <summary>Lives in the tray, owns the hotkeys. The settings window is only created while open.</summary>
sealed class TrayApp : ApplicationContext
{
    readonly Osd osd = new();
    readonly HotkeyManager hotkeys = new();
    readonly NotifyIcon tray;
    readonly ToolStripMenuItem pauseItem;
    readonly SynchronizationContext ui;
    readonly RegisteredWaitHandle showWait;
    readonly GameModeWatcher gameMode;
    MainForm window;
    TrayFlyout flyout;
    long flyoutClosedAt;

    public AppSettings Settings { get; }
    public bool Paused { get; private set; }
    public event Action BindingsChanged;
    /// <summary>Hidden apps, popup or game mode settings changed.</summary>
    public event Action SettingsChanged;

    public TrayApp(bool showWindow, EventWaitHandle showSignal)
    {
        ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        Settings = AppSettings.Load();
        Startup.MigrateOldName();
        gameMode = new GameModeWatcher(() => Settings.GameMode);
        gameMode.Apply();

        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            Font = Theme.Body,
            DropShadowEnabled = false,
            Padding = new Padding(0, Theme.S(4), 0, Theme.S(4)),
        };
        menu.HandleCreated += (_, _) => Theme.StyleWindow(menu.Handle, smallCorners: true);
        var open = menu.Items.Add("Open Auxer", null, (_, _) => ShowWindow());
        open.Font = Theme.Bold;
        pauseItem = new ToolStripMenuItem("Pause hotkeys", null, (_, _) => SetPaused(!Paused));
        menu.Items.Add(pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Quit());
        foreach (ToolStripItem item in menu.Items)
            if (item is ToolStripMenuItem) item.Padding = new Padding(Theme.S(4), Theme.S(5), Theme.S(16), Theme.S(5));

        tray = new NotifyIcon { Icon = AppIcon.Tray(), Text = "Auxer: click for volume", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleFlyout(); };
        tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };

        ApplyHotkeys();

        showWait = ThreadPool.RegisterWaitForSingleObject(showSignal,
            (_, _) => ui.Post(_ => ShowWindow(), null), null, -1, false);

        if (showWindow) ShowWindow();
        else TrimMemorySoon();
    }

    /// <summary>Left-click on the tray icon: quick per-app volume panel.</summary>
    void ToggleFlyout()
    {
        if (flyout != null) { flyout.Close(); return; }
        // Clicking the icon while the flyout is open first closes it (focus loss); don't reopen right away.
        if (Environment.TickCount64 - flyoutClosedAt < 350) return;
        flyout = new TrayFlyout(this);
        flyout.FormClosed += (_, _) => { flyout = null; flyoutClosedAt = Environment.TickCount64; TrimMemorySoon(); };
        flyout.ShowNear(Cursor.Position);
    }

    public void ShowWindow()
    {
        if (window == null)
        {
            window = new MainForm(this);
            window.FormClosed += (_, _) => { window = null; TrimMemorySoon(); };
            window.Show();
        }
        if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
        window.Activate();
    }

    // ---------- hidden apps ----------

    /// <summary>Current audio apps minus the hidden ones. Caller disposes the results.</summary>
    public List<AudioApp> GetVisibleApps()
    {
        var all = AudioSessions.GetApps();
        var visible = new List<AudioApp>();
        foreach (var a in all)
            if (Settings.IsHidden(a.Name)) a.Dispose(); else visible.Add(a);
        return visible;
    }

    public void HideApp(string name)
    {
        if (!Settings.IsHidden(name)) Settings.HiddenApps.Add(name);
        SettingsSaved();
        window?.RefreshMixer();
    }

    public void UnhideApp(string name)
    {
        Settings.HiddenApps.RemoveAll(h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        SettingsSaved();
        window?.RefreshMixer();
    }

    // ---------- popup & game mode ----------

    /// <summary>Saves settings and lets open windows refresh.</summary>
    public void SettingsSaved()
    {
        Settings.Save();
        SettingsChanged?.Invoke();
    }

    public void ApplyGameMode()
    {
        gameMode.Apply();
        SettingsSaved();
    }

    void ConfigureOsd()
    {
        osd.Position = Settings.OverlayPosition;
        osd.DurationMs = Settings.OverlayDurationMs;
    }

    /// <summary>Shows a sample popup so position/duration changes can be seen right away.</summary>
    public void PreviewOverlay()
    {
        ConfigureOsd();
        osd.ShowLevel("Preview", 60, false);
    }

    /// <summary>(Re)registers every binding's hotkey and records whether it worked.</summary>
    public void ApplyHotkeys()
    {
        hotkeys.UnregisterAll();
        foreach (var b in Settings.Bindings)
        {
            var hk = Hotkey.Parse(b.Hotkey);
            if (hk.IsEmpty) b.Status = "No hotkey";
            else if (Paused) b.Status = "Paused";
            else
            {
                var binding = b;
                b.Status = hotkeys.Register(hk, b.Repeats, () => Execute(binding)) ? "Active" : "Key combo already in use";
            }
        }
        BindingsChanged?.Invoke();
    }

    /// <summary>Releases all hotkeys (while the user records a new combo).</summary>
    public void SuspendHotkeys() => hotkeys.UnregisterAll();

    public void SetPaused(bool paused)
    {
        Paused = paused;
        pauseItem.Checked = paused;
        tray.Text = paused ? "Auxer (hotkeys paused)" : "Auxer: click for volume";
        ApplyHotkeys();
    }

    void Execute(KeyBinding b)
    {
        var target = b.IsFocused ? AudioSessions.ForegroundProcessName() : b.Target;
        if (string.IsNullOrWhiteSpace(target)) return;

        var apps = AudioSessions.GetApps(target);
        ConfigureOsd();
        try
        {
            var app = apps.FirstOrDefault();
            if (app == null)
            {
                if (Settings.ShowOverlay) osd.ShowMessage(target, "No audio");
                return;
            }

            switch (b.Action)
            {
                case BindAction.VolumeDown:
                    app.SetVolume((app.Percent - b.Amount) / 100f);
                    break;
                case BindAction.VolumeUp:
                    app.SetVolume((app.Percent + b.Amount) / 100f);
                    if (app.Muted) app.SetMute(false);
                    break;
                case BindAction.ToggleMute:
                    app.SetMute(!app.Muted);
                    break;
                case BindAction.SetVolume:
                    app.SetVolume(b.Amount / 100f);
                    if (app.Muted) app.SetMute(false);
                    break;
            }

            if (Settings.ShowOverlay) osd.ShowLevel(app.Name, app.Percent, app.Muted);
        }
        finally
        {
            foreach (var a in apps) a.Dispose();
        }
        window?.RefreshMixer();
    }

    void Quit()
    {
        showWait.Unregister(null);
        gameMode.Dispose(); // puts the music volume back if a game is still in front
        flyout?.Close();
        window?.Close();
        hotkeys.Dispose();
        tray.Visible = false;
        tray.Dispose();
        osd.Dispose();
        ExitThread();
    }

    /// <summary>Hand unused memory back to Windows once we're idle in the tray.</summary>
    void TrimMemorySoon()
    {
        var t = new System.Windows.Forms.Timer { Interval = 2000 };
        t.Tick += (_, _) =>
        {
            t.Dispose();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
        };
        t.Start();
    }

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);
}
