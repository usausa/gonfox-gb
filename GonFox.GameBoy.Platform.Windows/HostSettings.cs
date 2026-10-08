namespace GonFox.GameBoy.Platform.Windows;

using System.IO;
using System.Text.Json;

// What a Windows host keeps between runs: volume, mute, window bounds, renderer and boot ROM path.
public sealed record HostSettings(int Volume = HostSettings.DefaultVolume, bool Muted = false, WindowBounds? Window = null,
    string? Renderer = null, string? BootRom = null)
{
    public const int DefaultVolume = 70;
    public const int MaxRendererLength = 32;

    // Windows MAX_PATH.
    public const int MaxBootRomPathLength = 260;

    public static string? ValidBootRom(string? path) => path is { Length: > 0 and <= MaxBootRomPathLength } ? path : null;
}

public readonly record struct WindowBounds(double Left, double Top, double Width, double Height, bool Maximized);

// Loads and saves the settings as a small JSON file; a bad file or value gives the defaults.
public sealed class HostSettingsStore(string path)
{
    private const int MaxFileBytes = 4096;
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    public string FilePath { get; } = path;

    public HostSettings Load()
    {
        try
        {
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxFileBytes)
            {
                return new();
            }

            var file = JsonSerializer.Deserialize<SettingsFile>(stream);
            if (file is null)
            {
                return new();
            }

            var volume = file.Volume is >= 0 and <= 100 ? file.Volume : HostSettings.DefaultVolume;
            WindowBounds? window = file.Window is { } w && double.IsFinite(w.Left) && double.IsFinite(w.Top) &&
                double.IsFinite(w.Width) && double.IsFinite(w.Height) && w.Width > 0 && w.Height > 0
                ? new WindowBounds(w.Left, w.Top, w.Width, w.Height, w.Maximized) : null;
            var renderer = file.Renderer is { Length: > 0 and <= HostSettings.MaxRendererLength } name ? name : null;
            return new HostSettings(volume, file.Muted, window, renderer, HostSettings.ValidBootRom(file.BootRom));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    // Saves the settings, returning false on failure rather than stopping an exit.
    public bool Save(HostSettings settings)
    {
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
            var file = new SettingsFile
            {
                Volume = settings.Volume,
                Muted = settings.Muted,
                Window = settings.Window is { } w ? new BoundsFile { Left = w.Left, Top = w.Top, Width = w.Width, Height = w.Height, Maximized = w.Maximized } : null,
                Renderer = settings.Renderer,
                BootRom = HostSettings.ValidBootRom(settings.BootRom)
            };
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, file, WriteOptions);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(FilePath))
            {
                File.Replace(temporary, FilePath, null);
            }
            else
            {
                File.Move(temporary, FilePath);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class SettingsFile
    {
        public int Volume { get; set; } = HostSettings.DefaultVolume;
        public bool Muted { get; set; }
        public BoundsFile? Window { get; set; }
        public string? Renderer { get; set; }
        public string? BootRom { get; set; }
    }

    private sealed class BoundsFile
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }
}
