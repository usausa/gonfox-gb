namespace GonFox.GameBoy.Platform;

using System.IO;

// Tests where the Windows host keeps its files and their environment overrides.
public sealed class WindowsStorageTests
{
    [Theory]
    [InlineData(WindowsStorage.SavesVariable, "Saves")]
    [InlineData(WindowsStorage.StatesVariable, "States")]
    [InlineData(WindowsStorage.SettingsVariable, "settings.json")]
    public void EachPlaceCanBeRedirectedForChecks(string variable, string name)
    {
        string Place() => variable switch
        {
            WindowsStorage.SavesVariable => WindowsStorage.SavesDirectory,
            WindowsStorage.StatesVariable => WindowsStorage.StatesDirectory,
            _ => WindowsStorage.SettingsPath
        };
        var original = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, @"C:\checks\place");
            Assert.Equal(@"C:\checks\place", Place());
            Environment.SetEnvironmentVariable(variable, null);
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GonFox.GameBoy", name), Place());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
        }
    }

    [Fact]
    public void AnEmptyOverrideGivesTheStandardPlace()
    {
        Assert.Equal(@"C:\checks\settings.json", WindowsStorage.Resolve(@"C:\checks\settings.json", "settings.json"));
        var standard = WindowsStorage.Resolve(null, "settings.json");
        Assert.Equal(standard, WindowsStorage.Resolve(string.Empty, "settings.json"));
        Assert.Equal(Path.Combine(WindowsStorage.Root, "settings.json"), standard);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GonFox.GameBoy"), WindowsStorage.Root);
    }
}
