namespace Example.GameBoy.WpfHost;

using System.IO;
using System.Windows;

using GonFox.GameBoy.Platform.Windows;

public sealed class WindowSettingsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GonFox.GameBoy-WindowSettingsTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(directory, "nested", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // The renderer name is kept (up to 32 characters); unknown names give Skia.
    [Theory]
    [InlineData("{\"Renderer\": \"WriteableBitmap\"}", "WriteableBitmap", "WriteableBitmap")]
    [InlineData("{\"Renderer\": \"Skia\"}", "Skia", "Skia")]
    [InlineData("{\"Renderer\": \"OpenGL\"}", "OpenGL", "Skia")] // Another host's name.
    [InlineData("{\"Volume\": 40, \"Renderer\": \"\"}", null, "Skia")]
    [InlineData("{\"Renderer\": \"012345678901234567890123456789012\"}", null, "Skia")] // 33 characters.
    [InlineData("{\"Renderer\": \"01234567890123456789012345678901\"}", "01234567890123456789012345678901", "Skia")]
    [InlineData("{\"Volume\": 40}", null, "Skia")]
    public void TheDrawingMethodIsKeptByName(string content, string? kept, string renderer)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content);
        var loaded = new HostSettingsStore(FilePath).Load();
        Assert.Equal(kept, loaded.Renderer);
        Assert.Equal(renderer, Rendering.FrameDisplay.Parse(loaded.Renderer).ToString());
    }

    // The virtual screen of two 1920 x 1080 monitors side by side.
    private static readonly Rect Screen = new(0, 0, 3840, 1080);

    private static readonly Size Minimum = new(640, 780);

    [Theory]
    [InlineData(100, 50, 820, 950, true)] // Inside.
    [InlineData(3500, 100, 820, 950, true)] // 340 px visible.
    [InlineData(3700, 100, 820, 950, false)] // 140 px visible.
    [InlineData(-600, 100, 820, 950, true)] // 220 px visible.
    [InlineData(-700, 100, 820, 950, false)] // 120 px visible.
    [InlineData(-2000, 100, 820, 950, false)] // On a missing monitor.
    [InlineData(100, 1070, 820, 950, false)] // Under 16 px high.
    [InlineData(100, -20, 820, 950, false)] // 12 px high.
    public void TheWindowComesBackOnlyWhereItsTitleBarCanBeReached(double left, double top, double width, double height, bool placed)
    {
        var fitted = WindowPlacement.Fit(new WindowBounds(left, top, width, height, false), Screen, Minimum);
        Assert.Equal(placed, fitted is not null);
        if (fitted is { } bounds)
        {
            Assert.Equal((left, top), (bounds.Left, bounds.Top));
        }
    }

    [Theory]
    [InlineData(300, 300, 640, 780)] // Below the minimum.
    [InlineData(5000, 3000, 3840, 1080)] // Beyond the screen.
    [InlineData(900, 1000, 900, 1000)]
    public void TheSizeStaysWithinTheMinimumAndTheScreen(double width, double height, double expectedWidth, double expectedHeight)
    {
        var bounds = WindowPlacement.Fit(new WindowBounds(0, 0, width, height, true), Screen, Minimum)!.Value;
        Assert.Equal((expectedWidth, expectedHeight, true), (bounds.Width, bounds.Height, bounds.Maximized));
    }
}
