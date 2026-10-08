namespace Example.GameBoy.WpfHost;

using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Threading;

using Example.GameBoy.WpfHost.Rendering;

using GonFox.GameBoy.Core.Video;

// Tests the two screen displays, each on an STA thread with its own dispatcher.
public sealed class FrameDisplayTests
{
    private static readonly VideoFrameInfo Frame = new(VideoOutput.Width, VideoOutput.Height, VideoOutput.StrideBytes, 1, true);

    private static void OnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
#pragma warning disable CA1031
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
#pragma warning restore CA1031
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    // Makes an opaque BGRA image whose bytes differ from pixel to pixel.
    private static byte[] Image(int seed)
    {
        var pixels = new byte[VideoOutput.BufferSize];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = i % 4 == 3 ? (byte)0xFF : (byte)((i * 7) + seed);
        }

        return pixels;
    }

    private static void Lay(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout(); // Places the image.
    }

    [Fact]
    public void EachDrawingMethodHasItsDisplayAndTheSettingsNameChoosesIt()
    {
        OnSta(() =>
        {
            using IFrameDisplay skia = FrameDisplay.Create(FrameRenderer.Skia), bitmap = FrameDisplay.Create(FrameRenderer.WriteableBitmap);
            Assert.IsType<SkiaFrameDisplay>(skia);
            Assert.Same(skia, skia.Element);
            Assert.IsType<BitmapFrameDisplay>(bitmap);
            Assert.Same(bitmap, bitmap.Element);
            Assert.True(skia.Show(Image(1), Frame));
            Assert.Throws<ArgumentException>(() => skia.Show(new byte[100], Frame));

            // UI Automation sees each display as an image named by the window.
            foreach (var display in new[] { skia, bitmap })
            {
                AutomationProperties.SetName(display.Element, "Game Boy screen");
                var peer = UIElementAutomationPeer.CreatePeerForElement(display.Element);
                Assert.Equal(AutomationControlType.Image, peer.GetAutomationControlType());
                Assert.Equal("Game Boy screen", peer.GetName());
                Assert.Null(peer.GetChildren()); // No inner Image.
            }
        });
        Assert.Equal(FrameRenderer.WriteableBitmap, FrameDisplay.Parse(nameof(FrameRenderer.WriteableBitmap)));
        foreach (var other in new[] { nameof(FrameRenderer.Skia), null, string.Empty, "writeablebitmap", "1", "OpenGL" })
        {
            Assert.Equal(FrameRenderer.Skia, FrameDisplay.Parse(other));
        }
    }

    [Theory]
    [InlineData(330, 300, 5, 6, 320, 288)]
    [InlineData(480, 432, 0, 0, 480, 432)] // Whole multiple: fills the display.
    [InlineData(1000, 300, 340, 6, 320, 288)]
    public void TheWriteableBitmapDisplayHoldsTheImageOnTheLayoutRectangle(int width, int height, int left, int top, int imageWidth, int imageHeight)
    {
        OnSta(() =>
        {
            var display = new BitmapFrameDisplay();
            byte[] first = Image(1), second = Image(2);
            Assert.True(display.Show(first, Frame)); // Nothing holds the back buffer.
            Lay(display, width, height);
            Assert.Equal((width, height), display.PixelSize);
            Assert.Equal((left, top, imageWidth, imageHeight), FrameLayout.ImageRect(width, height));
            Assert.True(display.Matches(first));
            Assert.False(display.Matches(second));
            first[1000] ^= 1;
            Assert.False(display.Matches(first)); // One byte differs.
            Assert.True(display.Show(second, Frame));
            Assert.True(display.Matches(second));
            Lay(display, width + 10, height + 10); // A resize moves the image.
            Assert.Equal((width + 10, height + 10), display.PixelSize);
            Assert.True(display.Matches(second));
            Assert.Throws<ArgumentException>(() => display.Show(second, Frame with { StrideBytes = 641 }));
            Assert.Throws<ArgumentException>(() => display.Show(new byte[VideoOutput.BufferSize - 1], Frame));
        });
    }

    [Fact]
    public void TheWriteableBitmapDisplayShowsNothingBeforeItsFirstImage()
    {
        OnSta(() =>
        {
            var display = new BitmapFrameDisplay();
            Lay(display, 320, 288);
            Assert.False(display.Matches(new byte[VideoOutput.BufferSize])); // The empty bitmap is not shown.
        });
    }
}
