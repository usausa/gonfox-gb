namespace Example.GameBoy.WpfHost.Rendering;

using System.Windows;
using System.Windows.Automation.Peers;

using GonFox.GameBoy.Core.Video;

using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

// Shows the image through SkiaFrameRenderer, scaled on the CPU into a device-pixel surface.
internal sealed class SkiaFrameDisplay : SKElement, IFrameDisplay
{
    private readonly SkiaFrameRenderer renderer = new();

    internal SkiaFrameDisplay()
    {
        IgnorePixelScaling = false;
        PaintSurface += Paint;
    }

    public FrameworkElement Element => this;

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameDisplayAutomationPeer(this);

    public bool Show(byte[] pixels, VideoFrameInfo frame)
    {
        renderer.UpdateFrame(pixels, frame);
        InvalidateVisual();
        return true;
    }

    // Repaints on a DPI change, which SKElement does not do by itself.
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateVisual();
    }

    private void Paint(object? sender, SKPaintSurfaceEventArgs e)
    {
        renderer.Draw(e.Surface.Canvas, e.Info.Width, e.Info.Height);
    }

    public void Dispose()
    {
        PaintSurface -= Paint;
        renderer.Dispose();
    }
}
