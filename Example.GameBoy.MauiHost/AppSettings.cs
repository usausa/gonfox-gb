namespace Example.GameBoy.MauiHost;

// Keeps the app's settings between runs in Preferences; values out of range give the defaults.
internal static class AppSettings
{
    internal const int DefaultVolume = 70; // As the WPF host.
    private static IPreferences Store => Preferences.Default;

    internal static int Volume
    {
        get => Store.Get("volume", DefaultVolume) is var volume and >= 0 and <= 100 ? volume : DefaultVolume;
        set => Store.Set("volume", Math.Clamp(value, 0, 100));
    }

    internal static bool Muted { get => Store.Get("muted", false); set => Store.Set("muted", value); }

    internal static bool Haptics { get => Store.Get("haptics", true); set => Store.Set("haptics", value); }

    internal static bool Rumble { get => Store.Get("rumble", true); set => Store.Set("rumble", value); }

    internal static string? BootRom { get => Text("bootRom"); set => Set("bootRom", value); }

    internal static string? Last { get => Text("last"); set => Set("last", value); }

    internal static bool ResumeRunning { get => Store.Get("resumeRunning", false); set => Store.Set("resumeRunning", value); }

    private static string? Text(string key) => Store.Get<string?>(key, null) is { Length: > 0 } text ? text : null;

    private static void Set(string key, string? value)
    {
        if (value is null)
        {
            Store.Remove(key);
        }
        else
        {
            Store.Set(key, value);
        }
    }
}
