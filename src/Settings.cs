using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Auxer;

enum BindAction { VolumeDown, VolumeUp, ToggleMute, SetVolume }

sealed class KeyBinding
{
    public const string FocusedTarget = "@focused";

    /// <summary>Process name (e.g. "Spotify") or "@focused" for whatever window is in front.</summary>
    public string Target { get; set; } = "Spotify";
    public BindAction Action { get; set; } = BindAction.VolumeDown;
    public int Amount { get; set; } = 5;
    public string Hotkey { get; set; } = "";

    [JsonIgnore] public string Status { get; set; } = "";
    [JsonIgnore] public bool IsFocused => Target == FocusedTarget;
    [JsonIgnore] public bool Repeats => Action is BindAction.VolumeDown or BindAction.VolumeUp;
    [JsonIgnore] public string TargetText => IsFocused ? "Focused app" : Target;

    [JsonIgnore]
    public string ActionText => Action switch
    {
        BindAction.VolumeDown => $"Volume down {Amount}%",
        BindAction.VolumeUp => $"Volume up {Amount}%",
        BindAction.ToggleMute => "Toggle mute",
        BindAction.SetVolume => $"Set to {Amount}%",
        _ => Action.ToString(),
    };

    public KeyBinding Clone() => (KeyBinding)MemberwiseClone();
}

enum OverlayPosition { TopCenter, TopLeft, TopRight, BottomCenter, BottomLeft, BottomRight }

/// <summary>Lower one app (usually music) while a game is in front, restore it afterwards.</summary>
sealed class GameModeSettings
{
    public bool Enabled { get; set; }
    public string MusicApp { get; set; } = "Spotify";
    public int Level { get; set; } = 20;
    /// <summary>Treat any fullscreen app as a game (covers most games without listing them).</summary>
    public bool FullscreenApps { get; set; } = true;
    public List<string> Games { get; set; } = new();
}

sealed class AppSettings
{
    public List<KeyBinding> Bindings { get; set; } = new();
    public bool ShowOverlay { get; set; } = true;
    public OverlayPosition OverlayPosition { get; set; } = OverlayPosition.TopCenter;
    public int OverlayDurationMs { get; set; } = 1400;
    /// <summary>Process names left out of the mixer and tray panel.</summary>
    public List<string> HiddenApps { get; set; } = new();
    public GameModeSettings GameMode { get; set; } = new();

    public bool IsHidden(string name) => HiddenApps.Any(h => h.Equals(name, StringComparison.OrdinalIgnoreCase));

    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Auxer");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            // The app used to be called "Audios": carry its settings over once.
            var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Audios", "settings.json");
            if (!File.Exists(FilePath) && File.Exists(old))
            {
                Directory.CreateDirectory(Dir);
                File.Copy(old, FilePath);
            }
        }
        catch { }

        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? Defaults();
                if (loaded.OverlayDurationMs == 900) loaded.OverlayDurationMs = 500; // old "Short" value
                return loaded;
            }
        }
        catch { /* corrupt file: fall back to defaults */ }
        return Defaults();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not save settings:\n" + ex.Message, "Auxer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    static AppSettings Defaults() => new(); // no keybinds until the user adds some
}

static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Auxer";

    static string Command => $"\"{Environment.ProcessPath}\" --tray";

    /// <summary>Replaces the startup entry left by the old "Audios" name, if there is one.</summary>
    public static void MigrateOldName()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue("Audios") != null)
            {
                key.DeleteValue("Audios", false);
                key.SetValue(ValueName, Command);
            }
            // Task Manager keeps its own enabled/disabled record per startup entry; drop the stale one
            // so the old name doesn't linger in the Startup apps list.
            using var approved = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", writable: true);
            approved?.DeleteValue("Audios", false);
        }
        catch { }
    }

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && s.Equals(Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, false);
    }
}
