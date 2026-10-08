namespace Example.GameBoy.WpfHost.Rendering;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using GonFox.GameBoy.Core.Video;

// Shows the 160 x 144 image in a WriteableBitmap and lets WPF composition scale it.
internal sealed class BitmapFrameDisplay : Border, IFrameDisplay
{
    private readonly WriteableBitmap bitmap = new(VideoOutput.Width, VideoOutput.Height, 96, 96, PixelFormats.Bgr32, null);
    private readonly Image image;

    internal BitmapFrameDisplay()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x24, 0x2A)); // Matches Skia's background.
        image = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Fill,
            Visibility = Visibility.Hidden,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Child = image;
        SizeChanged += (_, _) => Place();
    }

    public FrameworkElement Element => this;

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameDisplayAutomationPeer(this);

    // The display's size in device pixels at its last layout.
    internal (int Width, int Height) PixelSize { get; private set; }

    public bool Show(byte[] pixels, VideoFrameInfo frame)
    {
        if (frame.Width != VideoOutput.Width || frame.Height != VideoOutput.Height || frame.StrideBytes != VideoOutput.StrideBytes ||
            pixels.Length < VideoOutput.BufferSize)
        {
            throw new ArgumentException("Expected a 160 x 144 BGRA32 frame with a 640-byte stride.", nameof(frame));
        }

        // Skips the image while the render thread holds the back buffer, so the UI thread never waits.
        if (!bitmap.TryLock(new Duration(TimeSpan.Zero)))
        {
            return false;
        }

        try
        {
            if (bitmap.BackBufferStride == frame.StrideBytes)
            {
                Marshal.Copy(pixels, 0, bitmap.BackBuffer, VideoOutput.BufferSize);
            }
            else
            {
                for (var y = 0; y < frame.Height; y++)
                {
                    Marshal.Copy(pixels, y * frame.StrideBytes, bitmap.BackBuffer + (y * bitmap.BackBufferStride), frame.StrideBytes);
                }
            }

            bitmap.AddDirtyRect(new Int32Rect(0, 0, frame.Width, frame.Height));
        }
        finally
        {
            bitmap.Unlock();
        }
        image.Visibility = Visibility.Visible;
        return true;
    }

    // Tells whether the bitmap holds these bytes and the image sits on its layout rectangle.
    internal bool Matches(byte[] pixels)
    {
        var copy = new byte[VideoOutput.BufferSize];
        bitmap.CopyPixels(copy, VideoOutput.StrideBytes, 0);
        var dpi = VisualTreeHelper.GetDpi(this);
        var expected = FrameLayout.ImageRect(PixelSize.Width, PixelSize.Height);
        var origin = image.TranslatePoint(default, this);
        return image.Visibility == Visibility.Visible && copy.AsSpan().SequenceEqual(pixels.AsSpan(0, VideoOutput.BufferSize)) &&
            Math.Abs((origin.X * dpi.DpiScaleX) - expected.Left) < 0.01 && Math.Abs((origin.Y * dpi.DpiScaleY) - expected.Top) < 0.01 &&
            Math.Abs((image.ActualWidth * dpi.DpiScaleX) - expected.Width) < 0.01 && Math.Abs((image.ActualHeight * dpi.DpiScaleY) - expected.Height) < 0.01;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Place();
    }

    private void Place()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        PixelSize = ((int)Math.Round(ActualWidth * dpi.DpiScaleX), (int)Math.Round(ActualHeight * dpi.DpiScaleY));
        var rect = FrameLayout.ImageRect(PixelSize.Width, PixelSize.Height);
        image.Margin = new Thickness(rect.Left / dpi.DpiScaleX, rect.Top / dpi.DpiScaleY, 0, 0);
        image.Width = rect.Width / dpi.DpiScaleX;
        image.Height = rect.Height / dpi.DpiScaleY;
    }

    public void Dispose()
    {
        // Nothing to release: WPF owns the bitmap.
    }
}
