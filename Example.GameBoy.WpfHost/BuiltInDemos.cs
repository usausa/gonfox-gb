namespace Example.GameBoy.WpfHost;

// Lists the demo ROMs built into the host and loads their images.
internal static class BuiltInDemos
{
    internal static readonly (string Resource, string Name)[] All =
    [
        ("BackgroundDemo.gb", "Background demo"),
        ("MegaDemo.gb", "Mega demo"),
        ("SoundCheck.gb", "Sound check"),
        ("RpgDemo.gb", "RPG demo")
    ];

    internal static byte[] Load(string resource)
    {
        using var stream = typeof(BuiltInDemos).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The built-in demo is missing: {resource}");
        var image = new byte[stream.Length];
        stream.ReadExactly(image);
        return image;
    }
}
