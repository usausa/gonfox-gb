namespace GonFox.GameBoy.Platform;

// Fits the 160 x 144 image into a display at the largest whole scale, centred on whole pixels.
public static class FrameLayout
{
    public static (float Left, float Top, float Width, float Height) ImageRect(int pixelWidth, int pixelHeight)
    {
        var scale = Math.Min(pixelWidth / 160f, pixelHeight / 144f);
        if (scale >= 1)
        {
            scale = MathF.Floor(scale);
        }

        float width = 160 * scale, height = 144 * scale;
        return (MathF.Floor((pixelWidth - width) / 2), MathF.Floor((pixelHeight - height) / 2), width, height);
    }
}
