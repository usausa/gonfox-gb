namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Video;

// Window quirks of the pixel FIFO that the per-pixel reference renderer leaves out.
[Trait("Category", "Unit")]
public sealed class PpuWindowQuirkTests
{
    private readonly VideoOutput video = new();
    private readonly Ppu ppu;

    public PpuWindowQuirkTests()
    {
        ppu = new(new Interrupts(), video);
        ppu.Reset();
        Write(0xFF40, 0);
        Write(0xFF47, 0xE4);
        Write(0xFF4A, 0);
    }

    private void Write(ushort address, byte value) => ppu.WriteRegister(address, value);

    private void Tile(int tile, byte low, byte high)
    {
        for (var row = 0; row < 8; row++)
        {
            ppu.WriteMemory((ushort)(0x8000 + (tile * 16) + (row * 2)), low);
            ppu.WriteMemory((ushort)(0x8001 + (tile * 16) + (row * 2)), high);
        }
    }

    private byte[] Frame(byte lcdc)
    {
        Write(0xFF40, lcdc);
        while (video.CompletedFrameCount < 2)
        {
            ppu.Tick(); // The first frame is not shown.
        }

        var pixels = new byte[VideoOutput.BufferSize];
        video.CopyLatestFrame(pixels);
        return pixels;
    }

    private static byte Pixel(byte[] pixels, int x, int y) => pixels[(y * 640) + (x * 4)];

    [Fact]
    public void Wx166ShowsNoColumnButCoversTheLinesBelow()
    {
        Tile(1, 0xFF, 0); // Window colour 1, background colour 0.
        for (var i = 0; i < 1024; i++)
        {
            ppu.WriteMemory((ushort)(0x9C00 + i), 1);
        }

        Write(0xFF4B, 166);

        // Turns the window on in the VBlank before the second frame, so WY first matches in that frame.
        Write(0xFF40, 0xD1);
        while (video.CompletedFrameCount == 0)
        {
            ppu.Tick();
        }

        var pixels = Frame(0xF1);

        // The window covers the screen one line down.
        for (var x = 0; x < 160; x++)
        {
            Assert.Equal(255, Pixel(pixels, x, 0));
        }

        for (var y = 1; y < 144; y++)
        {
            for (var x = 0; x < 160; x++)
            {
                Assert.Equal(170, Pixel(pixels, x, y));
            }
        }

        // Line 143 also carries the window into line 0 of the next frame.
        while (video.CompletedFrameCount < 3)
        {
            ppu.Tick();
        }

        video.CopyLatestFrame(pixels);
        for (var x = 0; x < 160; x++)
        {
            Assert.Equal(170, Pixel(pixels, x, 0));
        }
    }

    // With WX=0 the window starts before the fine scroll, which shifts it left.
    [Theory]
    [InlineData(0, 8)]
    [InlineData(1, 6)]
    [InlineData(3, 4)]
    [InlineData(7, 1)]
    public void Wx0StartsTheWindowBeforeTheFineScroll(byte scx, int markerX)
    {
        Tile(1, 0, 0);
        Tile(2, 0x01, 0x01); // Window column 15 is the only colour 3.
        ppu.WriteMemory(0x9C00, 1);
        ppu.WriteMemory(0x9C01, 2);
        for (var i = 2; i < 32; i++)
        {
            ppu.WriteMemory((ushort)(0x9C00 + i), 1);
        }

        Write(0xFF43, scx);
        Write(0xFF4B, 0);
        var pixels = Frame(0xF1);
        for (var x = 0; x < 160; x++)
        {
            Assert.Equal(x == markerX ? 0 : 255, Pixel(pixels, x, 0));
        }
    }

    // A disabled window whose start falls on a tile boundary inserts one colour-0 pixel there.
    [Theory]
    [InlineData(23, true, 16)]  // x=16: a tile boundary.
    [InlineData(26, true, -1)]  // x=19 is not.
    [InlineData(23, false, -1)] // Enabled: the window draws.
    public void DisabledWindowOnATileBoundaryInsertsOnePixel(byte wx, bool disable, int inserted)
    {
        Tile(0, 0xFE, 0x81); // Colours 3,1,1,1,1,1,1,2.
        Write(0xFF4B, wx);
        Write(0xFF40, 0xB1);
        for (var i = 0; i < 70224; i++)
        {
            ppu.Tick(); // The first frame is not shown.
        }

        while (ppu.Dot != 300)
        {
            ppu.Tick(); // Line 0's HBlank.
        }

        var pixels = Frame(disable ? (byte)0x91 : (byte)0xB1);
        byte[] shades = [255, 170, 85, 0];
        int[] row = [3, 1, 1, 1, 1, 1, 1, 2];
        for (var y = 1; y < 144; y += 47)
        {
            for (var x = 0; x < 160; x++)
            {
                var expected = inserted < 0 || x < inserted ? row[x & 7] : x == inserted ? 0 : row[(x - 1) & 7];
                Assert.Equal(shades[expected], Pixel(pixels, x, y));
            }
        }
    }
}
