namespace Example.GameBoy.MauiHost;

using System.Runtime.InteropServices;

using GonFox.GameBoy.Core.Video;

using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

// Draws the newest image, nearest sampled, at the largest whole multiple that fits.
internal sealed class ScreenView : SKCanvasView, IDisposable
{
    private static readonly SKColor Bezel = new(0x20, 0x24, 0x2A);
    private readonly SKBitmap bitmap = new(new SKImageInfo(VideoOutput.Width, VideoOutput.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
    private bool hasFrame;

    internal ScreenView()
    {
        IgnorePixelScaling = false;
        AutomationId = "Screen";
    }

    internal void Show(byte[] pixels)
    {
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), VideoOutput.BufferSize);
        bitmap.NotifyPixelsChanged();
        hasFrame = true;
        InvalidateSurface();
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(Bezel);
        if (!hasFrame)
        {
            return;
        }

        var (left, top, width, height) = FrameLayout.ImageRect(e.Info.Width, e.Info.Height);
        canvas.DrawBitmap(bitmap, SKRect.Create(left, top, width, height), new SKSamplingOptions(SKFilterMode.Nearest));
    }

    public void Dispose() => bitmap.Dispose();
}
