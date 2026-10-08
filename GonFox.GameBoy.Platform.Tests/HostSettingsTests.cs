namespace GonFox.GameBoy.Platform;

using System.IO;

public sealed class HostSettingsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GonFox.GameBoySettingsTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(directory, "nested", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SavedSettingsComeBackAndReplaceTheFileWithoutLeftovers()
    {
        var store = new HostSettingsStore(FilePath);
        var first = new HostSettings(35, true, new WindowBounds(120.5, 80, 900, 1000, false), "WriteableBitmap");
        Assert.True(store.Save(first));
        Assert.Equal(first, store.Load());
        var second = new HostSettings(0, false, new WindowBounds(-1800, 40, 700, 800, true));
        Assert.True(store.Save(second));
        Assert.Equal(second, new HostSettingsStore(FilePath).Load());
        Assert.Equal(["settings.json"], Directory.GetFiles(Path.GetDirectoryName(FilePath)!).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData(null)] // No file.
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[1, 2]")]
    [InlineData("{\"Volume\": \"loud\"}")]
    [InlineData("{\"Volume\": 50, \"Window\": {\"Left\": NaN}}")]
    public void MissingOrUnreadableFilesGiveTheDefaults(string? content)
    {
        if (content is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, content);
        }
        Assert.Equal(new HostSettings(), new HostSettingsStore(FilePath).Load());
        Assert.Equal((70, false, null), (new HostSettings().Volume, new HostSettings().Muted, new HostSettings().Window));
    }

    [Theory]
    [InlineData("{\"Volume\": -1, \"Muted\": true}", 70, true, false)]
    [InlineData("{\"Volume\": 101}", 70, false, false)]
    [InlineData("{\"Volume\": 100, \"Window\": {\"Left\": 0, \"Top\": 0, \"Width\": 0, \"Height\": 500}}", 100, false, false)]
    [InlineData("{\"Volume\": 5, \"Window\": {\"Left\": 10, \"Top\": 20, \"Width\": 900, \"Height\": 950}}", 5, false, true)]
    public void ValuesOutOfRangeFallBackOneByOne(string content, int volume, bool muted, bool hasWindow)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content);
        var loaded = new HostSettingsStore(FilePath).Load();
        Assert.Equal((volume, muted, hasWindow), (loaded.Volume, loaded.Muted, loaded.Window is not null));
    }

    // Keeps paths up to MAX_PATH characters; a longer one is dropped while the rest is saved.
    [Fact]
    public void TheBootRomIsKeptByItsPath()
    {
        var store = new HostSettingsStore(FilePath);
        const string path = @"D:\ブートROM\dmg_boot.bin";
        Assert.True(store.Save(new HostSettings(BootRom: path)));
        Assert.Equal(path, store.Load().BootRom);
        var longest = @"C:\" + new string('あ', HostSettings.MaxBootRomPathLength - 3); // Every character escaped.
        Assert.True(store.Save(new HostSettings(40, BootRom: longest)));
        Assert.Equal(new HostSettings(40, BootRom: longest), store.Load());
        Assert.True(store.Save(new HostSettings(40, BootRom: longest + "x")));
        Assert.Equal(new HostSettings(40), store.Load());
        File.WriteAllText(FilePath, "{\"Volume\": 40, \"BootRom\": \"\"}");
        Assert.Equal(new HostSettings(40), store.Load());
    }

    [Fact]
    public void OversizedFilesAreNotRead()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{\"Volume\": 10" + new string(' ', 5000) + "}");
        Assert.Equal(new HostSettings(), new HostSettingsStore(FilePath).Load());
    }

    [Fact]
    public void AFailedSaveReturnsFalseAndKeepsNoTemporaryFile()
    {
        Directory.CreateDirectory(FilePath); // Blocks the save.
        var store = new HostSettingsStore(FilePath);
        Assert.False(store.Save(new HostSettings(20)));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
    }
}
