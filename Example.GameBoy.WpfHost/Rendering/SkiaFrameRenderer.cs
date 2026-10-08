namespace Example.GameBoy.WpfHost.Rendering;

using System.Runtime.InteropServices;

using GonFox.GameBoy.Core.Video;

using SkiaSharp;

// Copies frames into a Skia bitmap and draws it scaled; used on the UI thread only.
internal sealed class SkiaFrameRenderer : IDisposable
{
    private readonly SKBitmap bitmap = new();
    private bool hasFrame;
    private bool disposed;

    internal SkiaFrameRenderer(int rowBytes = 640)
    {
        var info = new SKImageInfo(160, 144, SKColorType.Bgra8888, SKAlphaType.Opaque);
        if (!bitmap.TryAllocPixels(info, rowBytes))
        {
            bitmap.Dispose();
            throw new InvalidOperationException("Unable to allocate the display bitmap.");
        }
    }

    internal void UpdateFrame(byte[] pixels, VideoFrameInfo frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(pixels);
        if (frame.Width != 160 || frame.Height != 144 || frame.StrideBytes != 640)
        {
            throw new ArgumentException("Expected a 160 x 144 BGRA32 frame with a 640-byte stride.", nameof(frame));
        }

        if (pixels.Length < 92_160)
        {
            throw new ArgumentException("The frame buffer is too short.", nameof(pixels));
        }

        var target = bitmap.GetPixels();
        if (target == IntPtr.Zero || bitmap.ColorType != SKColorType.Bgra8888 ||
            bitmap.AlphaType != SKAlphaType.Opaque || bitmap.RowBytes < frame.StrideBytes)
        {
            throw new InvalidOperationException("The display bitmap is not writable BGRA32.");
        }

        if (bitmap.RowBytes == frame.StrideBytes)
        {
            Marshal.Copy(pixels, 0, target, frame.StrideBytes * frame.Height);
        }
        else
        {
            for (var y = 0; y < frame.Height; y++)
            {
                Marshal.Copy(pixels, y * frame.StrideBytes,
                    IntPtr.Add(target, y * bitmap.RowBytes), frame.Width * 4);
            }
        }

        bitmap.NotifyPixelsChanged();
        hasFrame = true;
    }

    internal void Draw(SKCanvas canvas, int pixelWidth, int pixelHeight)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        canvas.Clear(new SKColor(0x20, 0x24, 0x2A));
        if (!hasFrame || pixelWidth <= 0 || pixelHeight <= 0)
        {
            return;
        }

        canvas.DrawBitmap(bitmap, ImageRect(pixelWidth, pixelHeight), new SKSamplingOptions(SKFilterMode.Nearest));
    }

    // Fits the largest whole multiple of 160 x 144 (fractional below 1x), centred on whole pixels.
    internal static SKRect ImageRect(int pixelWidth, int pixelHeight)
    {
        var scale = Math.Min(pixelWidth / 160f, pixelHeight / 144f);
        if (scale >= 1)
        {
            scale = MathF.Floor(scale);
        }

        float width = 160 * scale, height = 144 * scale;
        return SKRect.Create(MathF.Floor((pixelWidth - width) / 2), MathF.Floor((pixelHeight - height) / 2), width, height);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        bitmap.Dispose();
        disposed = true;
    }
}
