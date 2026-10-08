namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Video;

// Dots at which PPU writes take effect; cases write at multiples of four dots, as the CPU does.
[Trait("Category", "Unit")]
public sealed class PpuWriteTimingTests
{
    private readonly Interrupts irq = new();
    private readonly VideoOutput video = new();
    private readonly Ppu ppu;

    public PpuWriteTimingTests()
    {
        ppu = new(irq, video);
        ppu.Reset();
        Write(0xFF40, 0);
        Write(0xFF47, 0xE4);
    }

    private void Write(ushort address, byte value) => ppu.WriteRegister(address, value);

    // Ticks to a line and dot; the mode tells line 0 from line 153, whose LY also reads 0.
    private void TickTo(int line, int dot)
    {
        while (ppu.Ly != line || ppu.Dot != dot || (line < 144 && ppu.Mode == 1))
        {
            ppu.Tick();
        }
    }

    private byte Stat => ppu.ReadRegister(0xFF41);

    private void Tile(int tile, Func<int, (byte Low, byte High)> row)
    {
        for (var y = 0; y < 8; y++)
        {
            var (low, high) = row(y);
            ppu.WriteMemory((ushort)(0x8000 + (tile * 16) + (y * 2)), low);
            ppu.WriteMemory((ushort)(0x8001 + (tile * 16) + (y * 2)), high);
        }
    }

    private void WindowMap(byte tile)
    {
        for (var i = 0; i < 1024; i++)
        {
            ppu.WriteMemory((ushort)(0x9C00 + i), tile);
        }
    }

    // Turns the LCD on and runs past its first frame, which is not shown.
    private void EnableAndSkipFirstFrame(byte lcdc)
    {
        Write(0xFF40, lcdc);
        while (video.CompletedFrameCount < 1)
        {
            ppu.Tick();
        }
    }

    private byte[] FinishFrame()
    {
        var target = video.CompletedFrameCount + 1;
        while (video.CompletedFrameCount < target)
        {
            ppu.Tick();
        }

        var pixels = new byte[VideoOutput.BufferSize];
        video.CopyLatestFrame(pixels);
        return pixels;
    }

    private static int Shade(byte[] pixels, int x, int y) => 3 - (pixels[(y * 640) + (x * 4)] / 85);

    private int Mode0Dot()
    {
        while (ppu.Mode != 0)
        {
            ppu.Tick();
        }

        return ppu.Dot;
    }

    // SCX lowered from 7 to 1 after the fine scroll passed 1: the scroll wraps round, 8 dots more.
    [Fact]
    public void AnScxWriteThatTheFineScrollMissesLengthensMode3ByEightDots()
    {
        Write(0xFF43, 7);
        EnableAndSkipFirstFrame(0x91);
        TickTo(5, 88);
        Write(0xFF43, 1);
        Assert.Equal(253 + 1 + 8, Mode0Dot());
        TickTo(6, 88);
        Assert.Equal(253 + 1, Mode0Dot()); // SCX 1 from the start.
    }

    // OAM entry i is checked at Dot 2 + 2i with the OBJ size of that moment.
    [Fact]
    public void TheOamScanUsesTheObjSizeOfEachEntrysDot()
    {
        Tile(1, _ => (0xFF, 0xFF)); // Colour 3.
        void Object(int entry, byte x) // On line 10 only as 8x16.
        {
            ppu.WriteMemory((ushort)(0xFE00 + (entry * 4)), 10 + 16 - 12);
            ppu.WriteMemory((ushort)(0xFE01 + (entry * 4)), x);
            ppu.WriteMemory((ushort)(0xFE02 + (entry * 4)), 0);
            ppu.WriteMemory((ushort)(0xFE03 + (entry * 4)), 0);
        }
        Object(0, 8);
        Object(39, 40);
        Write(0xFF48, 0xE4);
        EnableAndSkipFirstFrame(0x93);
        TickTo(10, 40);
        Write(0xFF40, 0x97);
        TickTo(10, 300);
        Write(0xFF40, 0x93);
        var pixels = FinishFrame();
        Assert.Equal(0, Shade(pixels, 0, 10)); // Entry 0: checked as 8x8.
        Assert.Equal(3, Shade(pixels, 32, 10)); // Entry 39: checked as 8x16.
    }

    // OAM written at Dot 80 of mode 2 reaches entry 39 but not entry 0, which the scan has passed.
    [Fact]
    public void AnOamWriteInMode2MissesTheEntriesTheScanHasPassed()
    {
        Tile(1, _ => (0xFF, 0xFF));
        foreach (var (entry, x) in new[] { (0, (byte)8), (39, (byte)40) })
        {
            ppu.WriteMemory((ushort)(0xFE01 + (entry * 4)), x);
            ppu.WriteMemory((ushort)(0xFE02 + (entry * 4)), 1);
        }
        Write(0xFF48, 0xE4);
        EnableAndSkipFirstFrame(0x93);
        TickTo(10, 80);
        ppu.WriteMemory(0xFE00, 10 + 16);
        ppu.WriteMemory(0xFE00 + (39 * 4), 10 + 16);
        var pixels = FinishFrame();
        Assert.Equal(0, Shade(pixels, 0, 10));
        Assert.Equal(3, Shade(pixels, 32, 10));
    }

    // Restoring a state also restores the SCX that the fine scroll uses.
    [Fact]
    public void ARestoredStateBringsItsScxToTheFineScroll()
    {
        Write(0xFF43, 5);
        EnableAndSkipFirstFrame(0x91);
        TickTo(3, 40);
        var saved = ppu.CaptureState();
        var other = new Ppu(new Interrupts(), new VideoOutput()) { WholeLines = false };
        other.Reset();
        other.RestoreState(saved);
        while (other.Mode != 0 || other.Dot <= 84)
        {
            other.Tick();
        }

        Assert.Equal(253 + 5, other.Dot);
    }

    // A WY change at Dot 0 decides its line; one at Dot 88 is compared too late for that line.
    [Fact]
    public void WyIsComparedWhenMode2BeginsAndThreeDotsAfterAWriteWhileDrawing()
    {
        Tile(1, _ => (0xFF, 0xFF));
        WindowMap(1);
        Write(0xFF4B, 2);
        Write(0xFF4A, 0xFF);
        EnableAndSkipFirstFrame(0xF1);
        TickTo(4, 300);
        Write(0xFF4A, 5);
        TickTo(5, 0);
        Write(0xFF4A, 0xFF);
        TickTo(8, 88);
        Write(0xFF4A, 8);
        var pixels = FinishFrame();
        for (var y = 0; y <= 8; y++)
        {
            Assert.Equal(0, Shade(pixels, 0, y));
        }

        Assert.Equal(3, Shade(pixels, 0, 9));
        Assert.Equal(3, Shade(pixels, 159, 143));
    }

    // Clearing the window enable a dot after the window starts draws background but adds 6 dots.
    [Fact]
    public void AWindowStartCaughtByClearingTheEnableShowsNoWindowButLengthensMode3()
    {
        Tile(0, _ => (0xFF, 0));
        Tile(1, _ => (0xFF, 0xFF));
        WindowMap(1); // Background colour 1, window colour 3.
        Write(0xFF43, 3);
        Write(0xFF4B, 0x11);
        Write(0xFF4A, 0);
        EnableAndSkipFirstFrame(0xF1);
        TickTo(1, 108);
        Write(0xFF40, 0xD1);
        Assert.Equal(253 + 3 + 6, Mode0Dot());
        TickTo(2, 0);
        Write(0xFF40, 0xF1);
        TickTo(2, 112);
        Write(0xFF40, 0xD1); // Later: the first window tile shows.
        var pixels = FinishFrame();
        for (var x = 0; x < 160; x++)
        {
            Assert.Equal(1, Shade(pixels, x, 1));
        }

        Assert.Equal(3, Shade(pixels, 10, 2));
    }

    // A written LYC is compared from the next dot; line 153 compares LY=153 only through Dot 5.
    [Fact]
    public void LyLycTakesAWrittenLycFromTheNextDot()
    {
        Write(0xFF41, 0x40);
        Write(0xFF45, 0xFF);
        EnableAndSkipFirstFrame(0x91);
        TickTo(5, 100);
        irq.WriteFlags(0);
        Write(0xFF45, 5);
        Assert.Equal(0, Stat & 4);
        Assert.Equal(0, irq.Flags & 2);
        ppu.Tick();
        Assert.Equal(4, Stat & 4);
        Assert.Equal(2, irq.Flags & 2);
        foreach (var (dot, expected) in new[] { (4, 2), (8, 0) })
        {
            Write(0xFF45, 152);
            TickTo(153, 0);
            irq.WriteFlags(0); // LY reads 0 here: count dots.
            for (var i = 0; i < dot; i++)
            {
                ppu.Tick();
            }

            Write(0xFF45, 153);
            for (var i = dot; i < 12; i++)
            {
                ppu.Tick();
            }

            Assert.Equal(expected, irq.Flags & 2);
        }
    }

    // LYC and LCDC written in the same dot: the LYC comparison is made before the LCD stops.
    [Fact]
    public void AnLycWriteRightBeforeTheLcdStopsIsComparedFirst()
    {
        Write(0xFF41, 0x40);
        Write(0xFF45, 0xFF);
        EnableAndSkipFirstFrame(0x91);
        TickTo(5, 100);
        irq.WriteFlags(0);
        Write(0xFF45, 5);
        Write(0xFF40, 0x11);
        Assert.Equal(4, Stat & 4);
        Assert.Equal(2, irq.Flags & 2);
        Write(0xFF40, 0x91);
        for (var i = 0; i < 456 * 6; i++)
        {
            ppu.Tick();
        }

        Assert.Equal(6, ppu.Ly); // Counted from LCD-on line 0.
    }

    // Restoring a state drops a pending LYC write; the restored LYC is compared and lines go on.
    [Fact]
    public void ARestoreDropsAnLycWriteWaitingForItsComparison()
    {
        Write(0xFF45, 0xFF);
        EnableAndSkipFirstFrame(0x91);
        TickTo(5, 100);
        var saved = ppu.CaptureState();
        TickTo(6, 0);
        Write(0xFF45, 6); // Compared at Dot 1.
        ppu.RestoreState(saved);
        for (var i = 0; i < 456 * 2; i++)
        {
            ppu.Tick();
        }

        Assert.Equal(7, ppu.Ly);
        Assert.Equal(0xFF, ppu.ReadRegister(0xFF45));
        Assert.Equal(0, Stat & 4);
    }

    // A WX=166 match held at line end starts the window on the next line if enabled before mode 3.
    [Theory]
    [InlineData(0, 300, true)]
    [InlineData(1, 76, true)]
    [InlineData(1, 88, false)]
    public void EnablingTheWindowWhileWx166IsHeldStartsItOnTheNextLine(int line, int dot, bool shown)
    {
        Tile(1, _ => (0xFF, 0xFF));
        WindowMap(1);
        Write(0xFF4B, 166);
        Write(0xFF4A, 0);
        EnableAndSkipFirstFrame(0xF1);
        TickTo(0, 200);
        Write(0xFF40, 0xD1); // Window off; the match is held.
        TickTo(line, dot);
        Write(0xFF40, 0xF1);
        var pixels = FinishFrame();
        Assert.Equal(shown ? 3 : 0, Shade(pixels, 0, 1));
    }

    // Restarted once mode 3 has begun (Dot 84), the window counts the extra row only after the line.
    [Theory]
    [InlineData(76, new[] { 2, 4, 6, 8 })]
    [InlineData(84, new[] { 1, 3, 5, 7 })]
    public void AWindowStoppedAndStartedAgainInMode2CountsTwoRowsPerLine(int restartDot, int[] rows)
    {
        // Window row r marks pixel r mod 8: colour 1 for rows 0-7, colour 3 for rows 8-15.
        Tile(1, y => ((byte)(0x80 >> y), 0));
        Tile(2, y => ((byte)(0x80 >> y), (byte)(0x80 >> y)));
        for (var i = 0; i < 32; i++)
        {
            ppu.WriteMemory((ushort)(0x9C00 + i), 1);
            ppu.WriteMemory((ushort)(0x9C20 + i), 2);
        }
        Write(0xFF4B, 166);
        Write(0xFF4A, 0xFF);
        EnableAndSkipFirstFrame(0xF1);
        TickTo(9, 300);
        Write(0xFF4A, 10);
        for (var line = 11; line <= 14; line++)
        {
            TickTo(line, 40);
            Write(0xFF40, 0xD1);
            TickTo(line, restartDot);
            Write(0xFF40, 0xF1);
        }
        var pixels = FinishFrame();
        Assert.Equal(rows, Enumerable.Range(11, 4).Select(y => Row(pixels, y)).ToArray());
        static int Row(byte[] pixels, int y)
        {
            for (var x = 0; x < 8; x++)
            {
                if (Shade(pixels, x, y) is var shade and not 0)
                {
                    return x + (shade == 3 ? 8 : 0);
                }
            }

            return -1;
        }
    }

    // A BGP write shows old OR new on one pixel, but the line's first pixel takes the new value whole.
    [Theory]
    [InlineData(0, 1, 3)]
    [InlineData(1, 0, 2)]
    public void APaletteWriteOnTheFirstPixelShowsTheNewValueWhole(byte scx, int x, int shade)
    {
        Write(0xFF43, scx);
        Write(0xFF47, 0x01);
        EnableAndSkipFirstFrame(0x91);
        TickTo(3, 96);
        Write(0xFF47, 0x02); // Shade 1 to 2; their OR is 3.
        TickTo(3, 300);
        Write(0xFF47, 0x01);
        var pixels = FinishFrame();
        Assert.Equal(shade, Shade(pixels, x, 3));
        Assert.Equal(2, Shade(pixels, x + 1, 3));
    }
}
