namespace GonFox.GameBoy.Platform.Windows;

using System.IO;

// Where a Windows host keeps its files, each overridable by an environment variable.
public static class WindowsStorage
{
    public const string SavesVariable = "GONFOX_GAMEBOY_SAVES";
    public const string StatesVariable = "GONFOX_GAMEBOY_STATES";
    public const string SettingsVariable = "GONFOX_GAMEBOY_SETTINGS";

    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GonFox.GameBoy");
    public static string SavesDirectory => Resolve(Environment.GetEnvironmentVariable(SavesVariable), "Saves");
    public static string StatesDirectory => Resolve(Environment.GetEnvironmentVariable(StatesVariable), "States");
    public static string SettingsPath => Resolve(Environment.GetEnvironmentVariable(SettingsVariable), "settings.json");

    // A non-empty override wins; otherwise the name under Root.
    public static string Resolve(string? custom, string name) => custom is { Length: > 0 } ? custom : Path.Combine(Root, name);
}
