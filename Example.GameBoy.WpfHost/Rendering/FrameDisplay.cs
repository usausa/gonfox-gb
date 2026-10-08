namespace Example.GameBoy.WpfHost.Rendering;

using System.Windows;
using System.Windows.Automation.Peers;

using GonFox.GameBoy.Core.Video;

// The ways the screen can draw the image: Skia scales on the CPU, WriteableBitmap through WPF.
internal enum FrameRenderer
{
    Skia,
    WriteableBitmap
}

// The screen element of one drawing method, used on the UI thread only.
internal interface IFrameDisplay : IDisposable
{
    FrameworkElement Element { get; }

    // Takes the image for the next frame; returns false when the display cannot take it at once.
    bool Show(byte[] pixels, VideoFrameInfo frame);
}

// Exposes the screen to UI Automation as an image with the name the window gives it.
internal sealed class FrameDisplayAutomationPeer(FrameworkElement owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
    protected override string GetClassNameCore() => "FrameDisplay";
    protected override List<AutomationPeer>? GetChildrenCore() => null;
}

internal static class FrameDisplay
{
    internal static IFrameDisplay Create(FrameRenderer renderer) =>
        renderer == FrameRenderer.WriteableBitmap ? new BitmapFrameDisplay() : new SkiaFrameDisplay();

    // Parses the renderer name kept in the settings; anything else gives the default.
    internal static FrameRenderer Parse(string? name) =>
        name == nameof(FrameRenderer.WriteableBitmap) ? FrameRenderer.WriteableBitmap : FrameRenderer.Skia;
}
