using System.Runtime.InteropServices;

namespace Auxer;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\Auxer.SingleInstance", out bool first);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Auxer.Show");
        if (!first)
        {
            // Already running: ask that instance to open its window, then quit.
            AllowSetForegroundWindow(-1);
            showSignal.Set();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.AddMessageFilter(new WheelUnderMouse());
        bool startInTray = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        Application.Run(new TrayApp(!startInTray, showSignal));
    }

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);
}

/// <summary>
/// Sends mouse-wheel messages to the control under the mouse instead of the focused one.
/// Windows may route the wheel to the focused control (depending on a mouse setting), which would make a
/// focused slider change volume instead of the list scrolling.
/// </summary>
sealed class WheelUnderMouse : IMessageFilter
{
    static readonly uint ownPid = (uint)Environment.ProcessId;

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != 0x020A /*WM_MOUSEWHEEL*/ && m.Msg != 0x020E /*WM_MOUSEHWHEEL*/) return false;
        long lp = m.LParam.ToInt64();
        var target = WindowFromPoint(new POINT { X = (short)(lp & 0xFFFF), Y = (short)((lp >> 16) & 0xFFFF) });
        if (target == IntPtr.Zero || target == m.HWnd) return false;
        GetWindowThreadProcessId(target, out uint pid);
        if (pid != ownPid) return false; // only redirect within Auxer's own windows
        SendMessage(target, m.Msg, m.WParam, m.LParam);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
}

static class AppIcon
{
    static Icon window;

    static Stream Data() => typeof(AppIcon).Assembly.GetManifestResourceStream("app.ico");

    public static Icon Window => window ??= new Icon(Data());

    public static Icon Tray()
    {
        using var s = Data();
        return new Icon(s, SystemInformation.SmallIconSize);
    }
}
