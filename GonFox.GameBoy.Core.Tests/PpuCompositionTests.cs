namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Video;

[Trait("Category", "Unit")]
public sealed class PpuCompositionTests
{
    private readonly VideoOutput video = new();
    private readonly Ppu ppu;

    public PpuCompositionTests()
    {
        ppu = new(new Interrupts(), video);
        ppu.Reset();
        Write(0xFF40, 0);
        Write(0xFF47, 0xE4);
        Write(0xFF48, 0xE4);
        Write(0xFF49, 0x1B);
    }

    private void Write(ushort address, byte value) => ppu.WriteRegister(address, value);

    private void Tick(int count)
    {
        for (var i = 0; i < count; i++)
        {
            ppu.Tick();
        }
    }

    // Writes LCDC, skipping the hidden first frame when this turns the LCD on.
    private void On(byte lcdc)
    {
        var enabling = (ppu.ReadRegister(0xFF40) & 0x80) == 0 && (lcdc & 0x80) != 0;
        Write(0xFF40, lcdc);
        if (enabling)
        {
            Tick(70224);
        }
    }

    private void Tile(int tile, byte low, byte high)
    {
        for (var row = 0; row < 8; row++)
        {
            Row(tile, row, low, high);
        }
    }

    private void Row(int tile, int row, byte low, byte high)
    {
        ppu.WriteMemory((ushort)(0x8000 + (tile * 16) + (row * 2)), low);
        ppu.WriteMemory((ushort)(0x8001 + (tile * 16) + (row * 2)), high);
    }

    private void Sprite(int index, byte y, byte x, byte tile, byte flags = 0)
    {
        byte[] bytes = [y, x, tile, flags];
        for (var i = 0; i < 4; i++)
        {
            ppu.WriteMemory((ushort)(0xFE00 + (index * 4) + i), bytes[i]);
        }
    }

    private byte[] Finish()
    {
        Tick(((144 - ppu.Ly) * 456) - ppu.Dot + 4); // First sample after the frame.
        var pixels = new byte[VideoOutput.BufferSize];
        video.CopyLatestFrame(pixels);
        return pixels;
    }

    private static byte Pixel(byte[] pixels, int x, int y = 0) => pixels[(y * 640) + (x * 4)];

    private void WindowPattern()
    {
        Tile(1, 0xFF, 0);
        Row(1, 1, 0, 0xFF);
        Row(1, 2, 0xFF, 0xFF);
        ppu.WriteMemory(0x9C00, 1);
        Write(0xFF4B, 7);
    }

    [Fact]
    public void WindowStartsAtWyAndWxMinusSevenWithoutUsingScroll()
    {
        WindowPattern();
        Write(0xFF4A, 2);
        Write(0xFF4B, 15);
        Write(0xFF42, 80);
        Write(0xFF43, 50);
        On(0xF1);
        var pixels = Finish();
        Assert.Equal(255, Pixel(pixels, 8, 1));
        Assert.Equal(255, Pixel(pixels, 7, 2));
        Assert.Equal(170, Pixel(pixels, 8, 2));
        Assert.Equal(85, Pixel(pixels, 8, 3));
    }

    [Fact]
    public void HiddenWindowPausesRowsAndCanChangeMapBeforeResuming()
    {
        WindowPattern();
        ppu.WriteMemory(0x9800, 1);
        On(0xF1);
        Tick(456);
        Write(0xFF4B, 167);
        Tick(456 * 2);
        Write(0xFF4B, 7);
        On(0xB1);
        Tick(456);
        On(0x91);
        Tick(456);
        On(0xB1);
        var pixels = Finish();
        Assert.Equal(170, Pixel(pixels, 0));
        Assert.Equal(85, Pixel(pixels, 0, 3));
        Assert.Equal(0, Pixel(pixels, 0, 5));
    }

    // WY matching while the window is disabled does not set the Y condition.
    [Fact]
    public void WyMatchNeedsTheWindowEnabled()
    {
        WindowPattern();
        Write(0xFF4A, 1);
        On(0xD1);
        Tick(456 * 3);
        Write(0xFF4A, 120);
        On(0xF1);
        var pixels = Finish();
        Assert.Equal(255, Pixel(pixels, 0, 3));
        Assert.Equal(170, Pixel(pixels, 0, 120));
    }

    [Fact]
    public void EnablingTheWindowOnTheWyLineLatchesIt()
    {
        WindowPattern();
        Write(0xFF4A, 2);
        On(0xD1);
        Tick((456 * 2) + 100);
        On(0xF1); // Late in line 2: starts on line 3.
        var pixels = Finish();
        Assert.Equal(255, Pixel(pixels, 0, 2));
        Assert.Equal(170, Pixel(pixels, 0, 3));
        Assert.Equal(85, Pixel(pixels, 0, 4));
    }

    [Fact]
    public void ChangingWyToAnAlreadyPassedLineDoesNotTriggerWindow()
    {
        WindowPattern();
        Write(0xFF4A, 100);
        On(0xF1);
        Tick(456 * 2);
        Write(0xFF4A, 1);
        Assert.All(Finish(), value => Assert.Equal(255, value));
    }

    [Theory]
    [InlineData(0, 0, 0)] // x=0 is window column 7.
    [InlineData(7, 0, 170)]
    [InlineData(165, 158, 170)] // WX=166 is a window quirk.
    [InlineData(167, 159, 255)]
    public void WindowClipsAtScreenEdges(int wx, int x, int expected)
    {
        WindowPattern();
        Row(1, 0, 0x81, 0x01);
        Write(0xFF4B, (byte)wx);
        On(0xF1);
        Assert.Equal(expected, Pixel(Finish(), x));
    }

    [Fact]
    public void WindowCounterResetsEachFrameAndWhenLcdIsDisabled()
    {
        WindowPattern();
        On(0xF1);
        Assert.Equal(170, Pixel(Finish(), 0));
        Tick(456 * 10);
        Assert.Equal(170, Pixel(Finish(), 0));
        Write(0xFF40, 0);
        On(0xF1);
        Assert.Equal(170, Pixel(Finish(), 0));
    }

    [Fact]
    public void BgDisableAlsoHidesWindowButKeepsSpritesAndRawPriorityZero()
    {
        WindowPattern();
        Tile(2, 0, 0xFF);
        Sprite(0, 16, 8, 2, 0x80);
        Write(0xFF47, 0xFF);
        On(0xF2);
        var pixels = Finish();
        Assert.Equal(85, Pixel(pixels, 0));
        Assert.Equal(255, Pixel(pixels, 8));
    }

    [Fact]
    public void SpriteFlipAndPaletteApplyAfterTransparency()
    {
        Row(1, 0, 0x80, 0);
        Row(1, 7, 0, 0x01);
        Sprite(0, 16, 8, 1);
        Sprite(1, 16, 16, 1, 0x20);
        Sprite(2, 16, 24, 1, 0x40);
        Sprite(3, 16, 32, 1, 0x70);
        On(0x93);
        var pixels = Finish();
        Assert.Equal(170, Pixel(pixels, 0));
        Assert.Equal(170, Pixel(pixels, 15));
        Assert.Equal(85, Pixel(pixels, 23));
        Assert.Equal(170, Pixel(pixels, 24));
        Assert.Equal(255, Pixel(pixels, 25)); // OBJ color 0 stays transparent.
    }

    [Fact]
    public void LargeSpritesIgnoreOddTileBitAndFlipBothTilesTogether()
    {
        Tile(2, 0xFF, 0);
        Tile(3, 0, 0xFF);
        Sprite(0, 16, 8, 3);
        Sprite(1, 16, 16, 3, 0x40);
        On(0x97);
        var pixels = Finish();
        Assert.Equal(170, Pixel(pixels, 0));
        Assert.Equal(85, Pixel(pixels, 0, 15));
        Assert.Equal(85, Pixel(pixels, 8));
        Assert.Equal(170, Pixel(pixels, 8, 15));
        Assert.Equal(255, Pixel(pixels, 0, 16));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(168)]
    public void FirstTenYMatchesIncludeSpritesWithInvisibleX(int hiddenX)
    {
        Tile(1, 0xFF, 0);
        for (var i = 0; i < 10; i++)
        {
            Sprite(i, 16, (byte)hiddenX, 1);
        }

        Sprite(10, 16, 8, 1);
        Sprite(11, 24, 8, 1);
        On(0x93);
        var pixels = Finish();
        Assert.Equal(255, Pixel(pixels, 0));
        Assert.Equal(170, Pixel(pixels, 0, 8));
    }

    [Fact]
    public void LowerXWinsThenLowerOamIndexWithTransparentHoles()
    {
        Tile(1, 0xFF, 0);
        Tile(2, 0, 0xFE);
        Tile(3, 0xFF, 0xFF);
        Sprite(0, 16, 10, 1);
        Sprite(1, 16, 8, 2);
        Sprite(2, 16, 8, 3);
        On(0x93);
        var pixels = Finish();
        Assert.Equal(85, Pixel(pixels, 2)); // Later OAM wins through smaller X.
        Assert.Equal(0, Pixel(pixels, 7)); // Hole in OBJ1 exposes OBJ2, ahead of OBJ0.
        Assert.Equal(170, Pixel(pixels, 8));
    }

    [Fact]
    public void BgPriorityUsesColorIndexBeforePaletteAndWinningObjectMasksOthers()
    {
        Tile(0, 0x80, 0);
        Tile(1, 0, 0xFF);
        Tile(2, 0xFF, 0xFF);
        Write(0xFF47, 0xE3); // BG index 1 white, index 0 black.
        Sprite(0, 16, 8, 1, 0x80);
        Sprite(1, 16, 8, 2);
        On(0x93);
        var pixels = Finish();
        Assert.Equal(255, Pixel(pixels, 0));
        Assert.Equal(85, Pixel(pixels, 1));
    }

    [Fact]
    public void WindowColorAlsoParticipatesInObjectPriority()
    {
        WindowPattern();
        Tile(2, 0, 0xFF);
        Sprite(0, 16, 8, 2, 0x80);
        On(0xF3);
        Assert.Equal(170, Pixel(Finish(), 0));
    }

    [Fact]
    public void SpritesClipNegativeCoordinatesAndCanBeDisabled()
    {
        Tile(1, 0xFF, 0);
        Sprite(0, 15, 7, 1);
        Sprite(1, 159, 167, 1);
        On(0x93);
        var pixels = Finish();
        Assert.Equal(170, Pixel(pixels, 0));
        Assert.Equal(255, Pixel(pixels, 7));
        Assert.Equal(170, Pixel(pixels, 159, 143));
        Assert.Equal(255, Pixel(pixels, 158, 143));
        Tick(456 * 10);
        On(0x91);
        Assert.All(Finish(), value => Assert.Equal(255, value));
    }
}
