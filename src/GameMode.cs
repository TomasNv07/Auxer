using System.Runtime.InteropServices;
using System.Text;

namespace Auxer;

/// <summary>
/// Game mode: lowers the music app while a game (a listed app, or any fullscreen app) is in front,
/// and puts the volume back when you leave it. Driven by Windows' "foreground changed" event,
/// so it costs nothing while idle (no polling).
/// </summary>
sealed class GameModeWatcher : IDisposable
{
    readonly Func<GameModeSettings> settings;
    readonly WinEventProc proc;
    // Re-check shortly after a focus change: games often go fullscreen a moment after they get focus,
    // and overlays (Discord, Steam, alt-tab) briefly steal focus without you leaving the game.
    readonly System.Windows.Forms.Timer settle = new() { Interval = 1200 };
    IntPtr hook;
    bool lowered;
    string loweredApp;
    int savedPercent, loweredPercent;

    public GameModeWatcher(Func<GameModeSettings> settings)
    {
        this.settings = settings;
        proc = OnForegroundChanged; // keep a reference so the delegate isn't collected
        settle.Tick += (_, _) => { settle.Stop(); Evaluate(settled: true); };
    }

    GameModeSettings S => settings();

    /// <summary>Starts or stops watching to match the settings.</summary>
    public void Apply()
    {
        if (S.Enabled && hook == IntPtr.Zero)
            hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, proc, 0, 0, WINEVENT_OUTOFCONTEXT);
        else if (!S.Enabled && hook != IntPtr.Zero)
        {
            UnhookWinEvent(hook);
            hook = IntPtr.Zero;
        }

        if (S.Enabled) Evaluate(settled: true);
        else { settle.Stop(); Restore(); }
    }

    void OnForegroundChanged(IntPtr hWinEventHook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        Evaluate(settled: false);
        settle.Stop();
        settle.Start();
    }

    void Evaluate(bool settled)
    {
        bool game = GameInFront();
        if (game && !lowered) Lower();
        else if (!game && lowered && settled) Restore(); // only restore once focus has really moved away
    }

    bool GameInFront()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        var name = AudioSessions.ForegroundProcessName();
        if (string.IsNullOrEmpty(name) || name.Equals("Auxer", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals(S.MusicApp, StringComparison.OrdinalIgnoreCase)) return false;
        if (S.Games.Any(g => g.Equals(name, StringComparison.OrdinalIgnoreCase))) return true;
        return S.FullscreenApps && IsFullscreen(hwnd);
    }

    static bool IsFullscreen(IntPtr hwnd)
    {
        var cls = new StringBuilder(64);
        GetClassName(hwnd, cls, cls.Capacity);
        // The desktop and taskbar cover the screen too, but they aren't games.
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (!GetWindowRect(hwnd, out var r)) return false;
        var screen = Screen.FromHandle(hwnd).Bounds;
        return r.Left <= screen.Left && r.Top <= screen.Top && r.Right >= screen.Right && r.Bottom >= screen.Bottom;
    }

    void Lower()
    {
        if (string.IsNullOrWhiteSpace(S.MusicApp)) return;
        var apps = AudioSessions.GetApps(S.MusicApp);
        try
        {
            var music = apps.FirstOrDefault();
            if (music == null) return; // not playing yet; re-checked on the next focus change
            savedPercent = music.Percent;
            loweredPercent = Math.Min(savedPercent, S.Level);
            loweredApp = music.Name;
            if (savedPercent > S.Level) music.SetVolume(S.Level / 100f);
            lowered = true;
        }
        finally { foreach (var a in apps) a.Dispose(); }
    }

    void Restore()
    {
        if (!lowered) return;
        lowered = false;
        var apps = AudioSessions.GetApps(loweredApp);
        try
        {
            var music = apps.FirstOrDefault();
            // If you changed the music volume yourself during the game, keep your change.
            if (music != null && music.Percent == loweredPercent) music.SetVolume(savedPercent / 100f);
        }
        finally { foreach (var a in apps) a.Dispose(); }
    }

    public void Dispose()
    {
        if (hook != IntPtr.Zero) { UnhookWinEvent(hook); hook = IntPtr.Zero; }
        settle.Stop();
        Restore();
        settle.Dispose();
    }

    const uint EVENT_SYSTEM_FOREGROUND = 3;
    const uint WINEVENT_OUTOFCONTEXT = 0;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    delegate void WinEventProc(IntPtr hWinEventHook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmod, WinEventProc proc, uint pid, uint thread, uint flags);

    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
}
