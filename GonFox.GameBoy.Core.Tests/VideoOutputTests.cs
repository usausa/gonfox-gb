namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Video;

[Trait("Category", "Unit")]
public sealed class VideoOutputTests
{
    [Fact]
    public void InitialFrameHasFixedLayoutAndOpaqueBlankPixels()
    {
        var video = new VideoOutput();
        var pixels = new byte[92_160];
        var frame = video.CopyLatestFrame(pixels);
        Assert.Equal(new VideoFrameInfo(160, 144, 640, 0, false), frame);
        Assert.All(pixels, value => Assert.Equal(255, value));
    }

    [Fact]
    public void GrayscaleStripesUseTopLeftOriginAndIndependentRows()
    {
        var video = new VideoOutput();
        video.SetLcdEnabled(true, showFirstFrame: true); // Shown, as at power-on.
        for (var y = 0; y < 144; y++)
        {
            video.WriteLine(y, Enumerable.Range(0, 160).Select(x => (byte)((x + y) % 4)).ToArray());
        }

        video.CompleteFrame();

        var pixels = Copy(video);
        byte[] intensities = [255, 170, 85, 0];
        for (var y = 0; y < 144; y++)
        {
            for (var x = 0; x < 160; x++)
            {
                var offset = (y * 640) + (x * 4);
                var expected = intensities[(x + y) % 4];
                Assert.Equal(new byte[] { expected, expected, expected, 255 }, pixels[offset..(offset + 4)]);
            }
        }
    }

    [Fact]
    public void PaletteColorsHaveExplicitBgraOrderAtAllFourCorners()
    {
        uint[] palette = [0xFFFFFF, 0xFF0000, 0x00FF00, 0x123456];
        var video = new VideoOutput(palette);
        palette[1] = 0; // The output keeps its own copy.
        video.SetLcdEnabled(true, showFirstFrame: true); // Shown, as at power-on.
        byte[] top = new byte[160], bottom = new byte[160];
        top[0] = 1;
        top[159] = 2;
        bottom[0] = 3;
        video.WriteLine(0, top);
        video.WriteLine(143, bottom);
        video.CompleteFrame();
        var pixels = Copy(video);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, pixels[..4]);
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, pixels[636..640]);
        Assert.Equal(new byte[] { 0x56, 0x34, 0x12, 255 }, pixels[91_520..91_524]);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, pixels[92_156..92_160]);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, pixels[640..644]);
    }

    [Fact]
    public void OnlyCompleteFramesArePublishedAndCopiesRemainIndependent()
    {
        var video = new VideoOutput();
        video.SetLcdEnabled(true, showFirstFrame: true); // Shown, as at power-on.
        var initial = video.Sequence;
        Fill(video, 3);
        Assert.All(Copy(video), value => Assert.Equal(255, value));
        Assert.Equal(initial, video.Sequence);
        video.CompleteFrame();
        var first = Copy(video);
        AssertSolid(first, 0);

        Fill(video, 1);
        Assert.Equal(first, Copy(video));
        video.CompleteFrame();
        Assert.Equal(initial + 2, video.Sequence);
        AssertSolid(Copy(video), 170);
        AssertSolid(first, 0);
        first[0] = 123;
        AssertSolid(Copy(video), 170);

        // Reuse both buffers again; a retained external copy is still isolated.
        Fill(video, 2);
        video.CompleteFrame();
        AssertSolid(Copy(video), 85);
        Assert.Equal(123, first[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(92_159)]
    public void ShortDestinationIsRejectedBeforeAnyWrite(int length)
    {
        var video = new VideoOutput();
        var destination = Enumerable.Repeat((byte)0x42, length).ToArray();
        Assert.Throws<ArgumentException>(() => video.CopyLatestFrame(destination));
        Assert.All(destination, value => Assert.Equal(0x42, value));
        Assert.Equal(0UL, video.Sequence);
    }

    [Fact]
    public void CopyLeavesTailAloneAndDoesNotAdvanceTheSystem()
    {
        var system = new GameBoySystem();
        var before = system.GetDebugSnapshot();
        var destination = Enumerable.Repeat((byte)0x42, 92_164).ToArray();
        var frame = system.Video.CopyLatestFrame(destination);
        Assert.Equal(system.Video.Sequence, frame.Sequence);
        Assert.All(destination[..92_160], value => Assert.Equal(255, value));
        Assert.All(destination[92_160..], value => Assert.Equal(0x42, value));
        Assert.Equal(before, system.GetDebugSnapshot());
        Assert.Equal(frame, system.Video.CopyLatestFrame(destination));
    }

    // Enabling the LCD shows blank until its second frame; the first still counts as completed.
    [Fact]
    public void TheFirstFrameAfterEnablingStaysBlankButCounts()
    {
        var video = new VideoOutput();
        video.SetLcdEnabled(true);
        var enabled = video.Sequence;
        Fill(video, 3);
        video.CompleteFrame();
        AssertSolid(Copy(video), 255);
        Assert.Equal(enabled, video.Sequence);
        Assert.Equal(1UL, video.CompletedFrameCount);
        Fill(video, 1);
        video.CompleteFrame();
        AssertSolid(Copy(video), 170);
        Assert.Equal(enabled + 1, video.Sequence);
        Assert.Equal(2UL, video.CompletedFrameCount);

        video.SetLcdEnabled(false);
        video.SetLcdEnabled(true); // Each enabling hides its own first frame.
        Fill(video, 3);
        video.CompleteFrame();
        AssertSolid(Copy(video), 255);
        Fill(video, 3);
        video.CompleteFrame();
        AssertSolid(Copy(video), 0);
        video.Reset();
        video.SetLcdEnabled(true, showFirstFrame: true);
        Fill(video, 3);
        video.CompleteFrame();
        AssertSolid(Copy(video), 0);
    }

    // The post-boot state continues the boot ROM's picture, so its first frame is shown.
    [Fact]
    public void PowerOnAndResetShowTheirFirstFrame()
    {
        var system = TestRom.Start(0x18, 0xFE); // JR -2
        for (var pass = 0; pass < 2; pass++)
        {
            ulong shown = system.Video.Sequence, completed = system.Video.CompletedFrameCount;
            system.RunForTCycles(70_224);
            Assert.Equal(completed + 1, system.Video.CompletedFrameCount);
            Assert.Equal(shown + 1, system.Video.Sequence);
            system.Reset();
        }
    }

    [Fact]
    public void LcdTransitionsDiscardPartialFrameAndPublishConsistentMetadata()
    {
        var video = new VideoOutput();
        video.SetLcdEnabled(true);
        Fill(video, 3);
        video.CompleteFrame();
        Fill(video, 2); // Unfinished next frame.
        var completed = video.Sequence;
        video.SetLcdEnabled(false);
        var pixels = new byte[VideoOutput.BufferSize];
        var disabled = video.CopyLatestFrame(pixels);
        Assert.Equal(completed + 1, disabled.Sequence);
        Assert.False(disabled.LcdEnabled);
        AssertSolid(pixels, 255);
        video.SetLcdEnabled(false);
        Fill(video, 3);
        video.CompleteFrame();
        Assert.Equal(disabled.Sequence, video.Sequence);
        AssertSolid(Copy(video), 255);

        video.SetLcdEnabled(true);
        var enabled = video.CopyLatestFrame(pixels);
        Assert.True(enabled.LcdEnabled);
        Assert.Equal(disabled.Sequence + 1, enabled.Sequence);
        AssertSolid(pixels, 255);
        video.SetLcdEnabled(true);
        Assert.Equal(enabled.Sequence, video.Sequence);

        // Checks the cleared drawing buffer as well.
        video.CompleteFrame();
        AssertSolid(Copy(video), 255);
    }

    [Fact]
    public void ResetAndCartridgeReplacementClearImagesWithoutRewindingSequence()
    {
        var system = new GameBoySystem();
        var video = system.Video;
        video.SetLcdEnabled(true);
        Fill(video, 3);
        video.CompleteFrame();
        var before = video.Sequence;
        system.Reset();
        Assert.Equal(before + 2, video.Sequence); // Clear, then enable.
        AssertSolid(Copy(video), 255);
        Assert.True(video.CopyLatestFrame(new byte[VideoOutput.BufferSize]).LcdEnabled);
        video.SetLcdEnabled(true);
        Fill(video, 2);
        video.CompleteFrame();
        before = video.Sequence;
        system.InsertCartridge(CartridgeLoader.Load(TestRom.Create(0)).Cartridge);
        Assert.Same(video, system.Video);
        Assert.Equal(before + 2, video.Sequence);
        AssertSolid(Copy(video), 255);
        video.SetLcdEnabled(true);
        video.CompleteFrame();
        AssertSolid(Copy(video), 255);
    }

    [Fact]
    public void BlankUsesConfiguredShadeZeroAndPaletteIsValidated()
    {
        var video = new VideoOutput([0x123456, 0, 0, 0]);
        Assert.Equal(new byte[] { 0x56, 0x34, 0x12, 255 }, Copy(video)[..4]);
        Assert.Throws<ArgumentException>(() => new VideoOutput([]));
        Assert.Throws<ArgumentException>(() => new VideoOutput([0u, 0u, 0u]));
        Assert.Throws<ArgumentException>(() => new VideoOutput([0u, 0u, 0u, 0x1000000u]));
    }

    [Theory]
    [InlineData(-1, 160, 159, 0)]
    [InlineData(144, 160, 159, 0)]
    [InlineData(0, 160, 0, 4)]
    [InlineData(0, 160, 159, 4)]
    [InlineData(143, 160, 80, 255)]
    [InlineData(0, 159, 0, 0)]
    [InlineData(0, 161, 0, 0)]
    public void InvalidLineDoesNotModifyEitherImage(int y, int length, int x, byte shade)
    {
        var video = new VideoOutput();
        video.SetLcdEnabled(true);
        var shades = Enumerable.Repeat((byte)3, length).ToArray();
        shades[x] = shade;
        Assert.ThrowsAny<ArgumentException>(() => video.WriteLine(y, shades));
        video.CompleteFrame();
        AssertSolid(Copy(video), 255);
    }

    private static byte[] Copy(VideoOutput video)
    {
        var result = new byte[VideoOutput.BufferSize];
        video.CopyLatestFrame(result);
        return result;
    }

    private static void Fill(VideoOutput video, byte shade)
    {
        var line = Enumerable.Repeat(shade, VideoOutput.Width).ToArray();
        for (var y = 0; y < VideoOutput.Height; y++)
        {
            video.WriteLine(y, line);
        }
    }

    private static void AssertSolid(byte[] pixels, byte intensity)
    {
        for (var offset = 0; offset < pixels.Length; offset++)
        {
            Assert.Equal(offset % 4 == 3 ? (byte)255 : intensity, pixels[offset]);
        }
    }
}
