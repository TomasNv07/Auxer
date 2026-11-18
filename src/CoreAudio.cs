using System.Runtime.InteropServices;

namespace Auxer;

/// <summary>One app (grouped by process name) with all of its audio sessions on every output device.</summary>
sealed class AudioApp : IDisposable
{
    readonly List<object> sessions = new();

    public AudioApp(string name) => Name = name;

    public string Name { get; }
    public bool IsActive { get; private set; }

    internal void Add(object session, bool active)
    {
        sessions.Add(session);
        IsActive |= active;
    }

    IEnumerable<ISimpleAudioVolume> Volumes => sessions.OfType<ISimpleAudioVolume>();

    public float Volume
    {
        get
        {
            float best = 0;
            foreach (var v in Volumes)
                try { v.GetMasterVolume(out var f); best = Math.Max(best, f); } catch { }
            return best;
        }
    }

    public int Percent => (int)Math.Round(Volume * 100);

    public bool Muted
    {
        get
        {
            bool any = false;
            foreach (var v in Volumes)
                try { v.GetMute(out var m); if (!m) return false; any = true; } catch { }
            return any;
        }
    }

    public void SetVolume(float level)
    {
        level = Math.Clamp(level, 0f, 1f);
        var ctx = Guid.Empty;
        foreach (var v in Volumes)
            try { v.SetMasterVolume(level, ref ctx); } catch { }
    }

    public void SetMute(bool mute)
    {
        var ctx = Guid.Empty;
        foreach (var v in Volumes)
            try { v.SetMute(mute, ref ctx); } catch { }
    }

    public void Dispose()
    {
        foreach (var s in sessions)
            try { Marshal.ReleaseComObject(s); } catch { }
        sessions.Clear();
    }
}

static class AudioSessions
{
    const uint DEVICE_STATE_ACTIVE = 1;
    const uint CLSCTX_ALL = 23;
    const int AudioSessionStateExpired = 2;
    const int AudioSessionStateActive = 1;

    /// <summary>Snapshot of every app that currently has an audio session. Caller disposes the results.</summary>
    public static List<AudioApp> GetApps(string onlyName = null)
    {
        var byName = new Dictionary<string, AudioApp>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<uint, string>();
        IMMDeviceEnumerator enumerator = null;
        IMMDeviceCollection devices = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            enumerator.EnumAudioEndpoints(EDataFlow.Render, DEVICE_STATE_ACTIVE, out devices);
            devices.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                IMMDevice device = null;
                object manager = null;
                IAudioSessionEnumerator list = null;
                try
                {
                    devices.Item(i, out device);
                    var iid = typeof(IAudioSessionManager2).GUID;
                    device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out manager);
                    ((IAudioSessionManager2)manager).GetSessionEnumerator(out list);
                    list.GetCount(out int n);
                    for (int j = 0; j < n; j++)
                    {
                        list.GetSession(j, out var ctl);
                        bool kept = false;
                        try
                        {
                            if (ctl.IsSystemSoundsSession() == 0) continue;
                            ctl.GetState(out int state);
                            if (state == AudioSessionStateExpired) continue;
                            ctl.GetProcessId(out uint pid);
                            if (pid == 0) continue;
                            if (!names.TryGetValue(pid, out var name))
                                names[pid] = name = ProcessName(pid);
                            if (name == null) continue;
                            if (onlyName != null && !name.Equals(onlyName, StringComparison.OrdinalIgnoreCase)) continue;

                            if (!byName.TryGetValue(name, out var app))
                                byName[name] = app = new AudioApp(name);
                            app.Add(ctl, state == AudioSessionStateActive);
                            kept = true;
                        }
                        catch (COMException) { }
                        finally
                        {
                            if (!kept) Marshal.ReleaseComObject(ctl);
                        }
                    }
                }
                catch (COMException) { }
                finally
                {
                    Release(list);
                    Release(manager);
                    Release(device);
                }
            }
        }
        catch (COMException) { }
        finally
        {
            Release(devices);
            Release(enumerator);
        }
        return byName.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Process name of the window in the foreground (e.g. "Spotify", "cs2").</summary>
    public static string ForegroundProcessName()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == 0 ? null : ProcessName(pid);
    }

    static string ProcessName(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h != IntPtr.Zero)
        {
            try
            {
                var buf = new char[1024];
                uint len = (uint)buf.Length;
                if (QueryFullProcessImageName(h, 0, buf, ref len))
                    return Path.GetFileNameWithoutExtension(new string(buf, 0, (int)len));
            }
            finally { CloseHandle(h); }
        }
        try { using var p = System.Diagnostics.Process.GetProcessById((int)pid); return p.ProcessName; }
        catch { return null; }
    }

    static void Release(object o)
    {
        if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
    }

    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool QueryFullProcessImageName(IntPtr process, uint flags, [Out] char[] exeName, ref uint size);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
}

// ---- Windows Core Audio COM interfaces (only the parts we use; vtable order matters) ----

enum EDataFlow { Render = 0, Capture = 1, All = 2 }

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
class MMDeviceEnumeratorCom { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceCollection
{
    void GetCount(out uint count);
    void Item(uint index, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    void Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
}

[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionManager2
{
    void GetAudioSessionControl(IntPtr sessionGuid, uint streamFlags, out IntPtr sessionControl);
    void GetSimpleAudioVolume(IntPtr sessionGuid, uint streamFlags, out IntPtr audioVolume);
    void GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionEnumerator
{
    void GetCount(out int count);
    void GetSession(int index, out IAudioSessionControl2 session);
}

[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionControl2
{
    // IAudioSessionControl
    [PreserveSig] int GetState(out int state);
    [PreserveSig] int GetDisplayName(out IntPtr name);
    [PreserveSig] int SetDisplayName(IntPtr name, IntPtr ctx);
    [PreserveSig] int GetIconPath(out IntPtr path);
    [PreserveSig] int SetIconPath(IntPtr path, IntPtr ctx);
    [PreserveSig] int GetGroupingParam(out Guid param);
    [PreserveSig] int SetGroupingParam(IntPtr param, IntPtr ctx);
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
    // IAudioSessionControl2
    [PreserveSig] int GetSessionIdentifier(out IntPtr id);
    [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
    [PreserveSig] int GetProcessId(out uint pid);
    [PreserveSig] int IsSystemSoundsSession();
}

[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ISimpleAudioVolume
{
    void SetMasterVolume(float level, ref Guid eventContext);
    void GetMasterVolume(out float level);
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}
