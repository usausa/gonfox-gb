namespace Example.GameBoy.WpfHost;

using Example.GameBoy.WpfHost.Rendering;

using GonFox.GameBoy.Core.Video;

using SkiaSharp;

public sealed class SkiaFrameRendererTests
{
    [Theory]
    [InlineData(640)]
    [InlineData(672)]
    public void BgraTransferAndNearestScalingPreservePixelsWithEitherStride(int rowBytes)
    {
        using var renderer = new SkiaFrameRenderer(rowBytes);
        using var target = new SKBitmap(500, 450, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(target);
        var pixels = new byte[VideoOutput.BufferSize];
        for (var y = 0; y < 144; y++)
        {
            for (var x = 0; x < 160; x++)
            {
                var i = (y * 640) + (x * 4);
                pixels[i] = (byte)x;
                pixels[i + 1] = (byte)y;
                pixels[i + 2] = (byte)(x ^ y);
                pixels[i + 3] = 255;
            }
        }

        var frame = new VideoFrameInfo(160, 144, 640, 1, true);
        renderer.UpdateFrame(pixels, frame);
        Array.Clear(pixels); // The renderer keeps a copy.
        renderer.Draw(canvas, 500, 450);
        Assert.Equal(new SKColor(0x20, 0x24, 0x2A), target.GetPixel(0, 0));
        for (var y = 0; y < 144; y++)
        {
            for (var x = 0; x < 160; x++)
            {
                var expected = new SKColor((byte)(x ^ y), (byte)y, (byte)x, 255);
                Assert.Equal(expected, target.GetPixel(10 + (x * 3), 9 + (y * 3)));
                Assert.Equal(expected, target.GetPixel(12 + (x * 3), 11 + (y * 3)));
            }
        }

        Array.Fill(pixels, (byte)255);
        renderer.UpdateFrame(pixels, frame with { Sequence = 2 });
        renderer.Draw(canvas, 500, 450);
        Assert.Equal(SKColors.White, target.GetPixel(100, 100));
    }

    [Theory]
    [InlineData(500, 450, 10, 9, 480, 432)]     // 3x, centred on whole pixels.
    [InlineData(1000, 300, 340, 6, 320, 288)]   // Wide: 2x, height-limited.
    [InlineData(300, 1000, 70, 428, 160, 144)]  // Tall: 1x.
    [InlineData(2520, 946, 780, 41, 960, 864)]  // Maximized window: 6x.
    [InlineData(100, 90, 0, 0, 100, 90)]        // Below 1x: fractional.
    public void ImageRectKeepsTenToNineAndPrefersWholeMultiples(int width, int height, float left, float top, float imageWidth, float imageHeight)
    {
        Assert.Equal(SKRect.Create(left, top, imageWidth, imageHeight), SkiaFrameRenderer.ImageRect(width, height));
    }

    [Fact]
    public void PausedImageIsRedrawnForNewSurfaceSizesWithoutAnotherUpload()
    {
        using var renderer = new SkiaFrameRenderer();
        var pixels = Pattern();
        renderer.UpdateFrame(pixels, new VideoFrameInfo(160, 144, 640, 7, true));
        foreach (var (width, height) in new[] { (1000, 300), (100, 90), (320, 288) })
        {
            using var target = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using (var canvas = new SKCanvas(target))
            {
                renderer.Draw(canvas, width, height);
            }

            var rect = SkiaFrameRenderer.ImageRect(width, height);
            var scale = rect.Width / 160;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var actual = target.GetPixel(x, y);
                    if (x < rect.Left || x >= rect.Right || y < rect.Top || y >= rect.Bottom)
                    {
                        Assert.True(new SKColor(0x20, 0x24, 0x2A) == actual, $"{width}x{height} background at ({x},{y})");
                        continue;
                    }

                    // Expects nearest sampling at the pixel centre; a centre on a source edge may take either side.
                    float sourceX = (x - rect.Left + 0.5f) / scale, sourceY = (y - rect.Top + 0.5f) / scale;
                    int[] xs = sourceX % 1 == 0 && sourceX > 0 ? [(int)sourceX, (int)sourceX - 1] : [(int)sourceX];
                    int[] ys = sourceY % 1 == 0 && sourceY > 0 ? [(int)sourceY, (int)sourceY - 1] : [(int)sourceY];
                    Assert.True(xs.Any(sx => ys.Any(sy => Color(pixels, Math.Min(sx, 159), Math.Min(sy, 143)) == actual)),
                        $"{width}x{height} image at ({x},{y})");
                }
            }
        }
    }

    [Fact]
    public async Task RestoredStateImageReachesTheCanvasEvenAfterNewerFrames()
    {
        using var runner = new EmulationRunner();
        using var renderer = new SkiaFrameRenderer();
        await runner.LoadAsync(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "background-demo.gb"), TestContext.Current.CancellationToken), "demo");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (runner.Status.Registers.TotalTCycles < 300_000 && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        await runner.CaptureStateAsync();
        byte[] saved = new byte[VideoOutput.BufferSize], pixels = new byte[VideoOutput.BufferSize];
        Assert.True(runner.TryCopyFrame(ulong.MaxValue, saved, out var shown));
        await runner.ResumeAsync();
        while (runner.Status.Registers.TotalTCycles < 1_000_000 && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        await runner.PauseAsync();
        Assert.True(runner.TryCopyFrame(shown.Sequence, pixels, out shown)); // A newer, scrolled frame.
        Assert.NotEqual(saved, pixels);
        renderer.UpdateFrame(pixels, shown);
        Assert.False(runner.TryCopyFrame(shown.Sequence, pixels, out _)); // Paused: nothing new to transfer.
        await runner.RestoreStateAsync();
        Assert.True(runner.TryCopyFrame(shown.Sequence, pixels, out var restored)); // A new Sequence, older content.
        Assert.Equal(saved, pixels);
        renderer.UpdateFrame(pixels, restored);
        using var target = new SKBitmap(160, 144, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(target))
        {
            renderer.Draw(canvas, 160, 144);
        }

        for (var y = 0; y < 144; y++)
        {
            for (var x = 0; x < 160; x++)
            {
                Assert.Equal(Color(saved, x, y), target.GetPixel(x, y));
            }
        }
    }

    private static byte[] Pattern()
    {
        var pixels = new byte[VideoOutput.BufferSize];
        for (var y = 0; y < 144; y++)
        {
            for (var x = 0; x < 160; x++)
            {
                var i = (y * 640) + (x * 4);
                pixels[i] = (byte)(x * 7);
                pixels[i + 1] = (byte)(y * 5);
                pixels[i + 2] = (byte)((x + y) * 3);
                pixels[i + 3] = 255;
            }
        }

        return pixels;
    }

    private static SKColor Color(byte[] pixels, int x, int y)
    {
        var i = (y * 640) + (x * 4);
        return new SKColor(pixels[i + 2], pixels[i + 1], pixels[i], pixels[i + 3]);
    }

    [Fact]
    public void DisposalIsRepeatableAndRejectsFurtherNativeUse()
    {
        var renderer = new SkiaFrameRenderer();
        using var target = new SKBitmap(160, 144);
        using var canvas = new SKCanvas(target);
        renderer.Dispose();
        renderer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => renderer.Draw(canvas, 160, 144));
        Assert.Throws<ObjectDisposedException>(() => renderer.UpdateFrame(new byte[VideoOutput.BufferSize], new(160, 144, 640, 1, true)));
    }

    [Fact]
    public void TheLayoutForOtherDisplaysIsTheSkiaRectangle()
    {
        for (var width = 1; width < 2000; width += 7)
        {
            for (var height = 1; height < 1500; height += 11)
            {
                var skia = SkiaFrameRenderer.ImageRect(width, height); // SKRect stores edges.
                var (left, top, width1, height1) = FrameLayout.ImageRect(width, height);
                Assert.Equal((skia.Left, skia.Top, skia.Right, skia.Bottom),
                    (left, top, left + width1, top + height1));
            }
        }
    }
}
