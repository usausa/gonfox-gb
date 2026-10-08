namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Memory;
using GonFox.GameBoy.Core.Video;

[Trait("Category", "Unit")]
public sealed class PpuTests
{
    private readonly Interrupts irq = new();
    private readonly VideoOutput video = new();
    private readonly Ppu ppu;

    // Runs from the reset point (line 153, Dot 400) to line 0, Dot 4, where the OAM scan has begun.
    public PpuTests()
    {
        ppu = new Ppu(irq, video);
        ppu.Reset();
        Tick(60);
    }

    private void Tick(int count)
    {
        for (var i = 0; i < count; i++)
        {
            ppu.Tick();
        }
    }

    private void Write(ushort address, byte value) => ppu.WriteRegister(address, value);

    private byte[] Pixels()
    {
        var result = new byte[VideoOutput.BufferSize];
        video.CopyLatestFrame(result);
        return result;
    }

    private void Off() => Write(0xFF40, 0);

    private void On(byte lcdc = 0x91) => Write(0xFF40, lcdc);

    // Turns the LCD on and skips its first frame, which is not shown.
    private void Show(byte lcdc = 0x91)
    {
        On(lcdc);
        Tick(70224);
    }

    private void Tile(ushort address, byte low, byte high)
    {
        for (ushort row = 0; row < 16; row += 2)
        {
            ppu.WriteMemory((ushort)(address + row), low);
            ppu.WriteMemory((ushort)(address + row + 1), high);
        }
    }

    // Samples at CPU M-cycle boundaries from line 0, Dot 4.
    [Fact]
    public void ModeBoundariesAndVblankHaveExactFramePeriod()
    {
        Tick(76);
        Assert.Equal(2, ppu.Mode);
        Tick(4);
        Assert.Equal(3, ppu.Mode);
        Tick(168);
        Assert.Equal(3, ppu.Mode); // Drawing lasts 172 dots here.
        Tick(4);
        Assert.Equal(0, ppu.Mode);
        Tick(196);
        Assert.Equal(0, ppu.Ly);
        Tick(4);
        Assert.Equal(1, ppu.Ly);
        Assert.Equal(0, ppu.Dot);
        Assert.Equal(0, ppu.Mode);
        Tick(4);
        Assert.Equal(2, ppu.Mode);
        Tick((456 * 143) - 4);
        Assert.Equal(144, ppu.Ly);
        Assert.Equal(0, ppu.Mode);
        Assert.Equal(0, irq.Flags & 1);
        Assert.Equal(0UL, video.CompletedFrameCount);
        Tick(4);
        Assert.Equal(1, ppu.Mode);
        Assert.Equal(1, irq.Flags & 1);
        Assert.Equal(1UL, video.CompletedFrameCount);
        irq.WriteFlags(0);
        Tick((456 * 10) - 4);
        Assert.Equal(0, ppu.Ly);
        Assert.Equal(0, ppu.Mode);
        Tick(4);
        Assert.Equal(2, ppu.Mode);
        Assert.Equal(0, irq.Flags & 1);
        Tick(456 * 144);
        Assert.Equal(2UL, video.CompletedFrameCount);
    }

    [Fact]
    public void StatSourcesShareOneEdgeRatherThanRequestingAtEveryModeChange()
    {
        Write(0xFF41, 0x28); // OAM and HBlank.
        Assert.Equal(2, irq.Flags & 2);
        irq.WriteFlags(0);
        Tick(252);
        Assert.Equal(2, irq.Flags & 2);
        irq.WriteFlags(0);
        Tick(204);
        Assert.Equal(2, ppu.Mode);
        Assert.Equal(0, irq.Flags & 2);
        Tick(252);
        Assert.Equal(2, irq.Flags & 2);
    }

    [Fact]
    public void LycWritesAndLineTransitionsUpdateCoincidenceAndStatEdge()
    {
        Write(0xFF41, 0x40);
        Assert.Equal(2, irq.Flags & 2);
        irq.WriteFlags(0);
        Write(0xFF45, 0);
        Tick(4);
        Assert.Equal(0, irq.Flags & 2); // Checked an M-cycle later.
        Write(0xFF45, 1);
        Tick(4);
        Assert.Equal(0, ppu.ReadRegister(0xFF41) & 4);
        Tick(448);
        Assert.Equal(2, irq.Flags & 2);
        Assert.Equal(4, ppu.ReadRegister(0xFF41) & 4);
        Write(0xFF44, 99);
        Assert.Equal(1, ppu.Ly);
        Assert.Equal(0xC6, ppu.ReadRegister(0xFF41));
    }

    [Fact]
    public void VblankStatIsOneEdgeAcrossAllTenLines()
    {
        Write(0xFF41, 0x10);
        Tick(456 * 144);
        Assert.Equal(3, irq.Flags & 3);
        irq.WriteFlags(0);
        Tick(456 * 10);
        Assert.Equal(0, irq.Flags & 3);
        Tick(456 * 144);
        Assert.Equal(3, irq.Flags & 3);
    }

    [Fact]
    public void CpuVramAndOamRestrictionsDoNotApplyToDebugReads()
    {
        Off();
        ppu.WriteMemory(0x8000, 0x12);
        ppu.WriteMemory(0xFE00, 0x34);
        On();
        var bus = new MemoryBus();
        bus.ConnectPpu(ppu);
        Tick(456); // Line 1's OAM scan.
        Assert.Equal(0x12, bus.ReadByte(0x8000));
        Assert.Equal(0xFF, bus.ReadByte(0xFE00));
        bus.WriteByte(0xFE00, 0x55);
        Assert.Equal(0x34, bus.PeekByte(0xFE00));
        Tick(80);
        Assert.Equal(0xFF, bus.ReadByte(0x8000));
        Assert.Equal(0x12, bus.PeekByte(0x8000));
        bus.WriteByte(0x8000, 0x55);
        Assert.Equal(0x12, bus.PeekByte(0x8000));
        Tick(172);
        bus.WriteByte(0x8000, 0x56);
        bus.WriteByte(0xFE00, 0x78);
        Assert.Equal(0x56, bus.ReadByte(0x8000));
        Assert.Equal(0x78, bus.ReadByte(0xFE00));

        // FEA0-FEFF reads 00 while OAM is open, and FF46 reads FF without DMA.
        Assert.Equal(0x00, bus.ReadByte(0xFEA0));
        Assert.Equal(0xFF, bus.ReadByte(0xFF46));
    }

    [Fact]
    public void UnsignedTilesDecodeBothPlanesAndPaletteInPixelOrder()
    {
        Off();
        Tile(0x8000, 0x55, 0x33);
        Write(0xFF47, 0xE4);
        Show();
        Tick(456 * 144);
        var pixels = Pixels();
        byte[] shades = [255, 170, 85, 0];
        for (var y = 0; y < 144; y++)
        {
            for (var x = 0; x < 160; x++)
            {
                var offset = (y * 640) + (x * 4);
                Assert.Equal(shades[x % 4], pixels[offset]);
                Assert.Equal(pixels[offset], pixels[offset + 1]);
                Assert.Equal(pixels[offset], pixels[offset + 2]);
                Assert.Equal(255, pixels[offset + 3]);
            }
        }
    }

    [Fact]
    public void SignedTileIndicesUse9000OriginAndAlternateMap()
    {
        Off();
        Tile(0x8FF0, 0xFF, 0);
        Tile(0x9000, 0, 0xFF);
        ppu.WriteMemory(0x9C00, 0xFF);
        Write(0xFF47, 0xE4);
        Show(0x89);
        Tick(456 * 144);
        var pixels = Pixels();
        Assert.Equal(170, pixels[0]);
        Assert.Equal(170, pixels[7 * 4]);
        Assert.Equal(85, pixels[8 * 4]);
        Assert.Equal(85, pixels[143 * 640]);
    }

    [Fact]
    public void ScrollWrapsBothCoordinatesAcrossMapEdges()
    {
        Off();
        Tile(0x8010, 0xFF, 0);
        Tile(0x8020, 0, 0xFF);
        Tile(0x8030, 0xFF, 0xFF);
        ppu.WriteMemory(0x9BFF, 1);
        ppu.WriteMemory(0x9BE0, 2);
        ppu.WriteMemory(0x981F, 3);

        // Keeps WY out of reach to avoid the disabled window's pixel insertion.
        Write(0xFF4A, 144);
        Write(0xFF42, 255);
        Write(0xFF43, 255);
        Write(0xFF47, 0xE4);
        Show();
        Tick(456 * 144);
        var pixels = Pixels();
        Assert.Equal(170, pixels[0]);
        Assert.Equal(85, pixels[4]);
        Assert.Equal(0, pixels[640]);
        Assert.Equal(255, pixels[644]);
    }

    [Fact]
    public void PaletteChangesAffectSubsequentLinesNotCompletedLines()
    {
        Off();
        Tile(0x8000, 0xFF, 0);
        Write(0xFF47, 0xE4);
        Show();
        Tick(252);
        Write(0xFF47, 0x1B);
        Tick((456 * 144) - 252);
        var pixels = Pixels();
        Assert.Equal(170, pixels[0]);
        Assert.Equal(85, pixels[640]);
    }

    [Fact]
    public void DisabledBackgroundUsesShadeZeroIndependentlyOfBgp()
    {
        Write(0xFF47, 0xFF);
        Write(0xFF40, 0x90);
        Tick(456 * 144);
        Assert.All(Pixels(), value => Assert.Equal(255, value));
    }

    [Fact]
    public void LcdOffFreezesScanAndKeepsTheScreenBlankUntilTheSecondFrame()
    {
        Off();
        Tile(0x8000, 0xFF, 0xFF);
        Write(0xFF47, 0xE4);
        Show();
        Tick(456 * 145);
        Assert.Equal(0, Pixels()[0]);
        var completed = video.CompletedFrameCount;
        Off();
        var sequence = video.Sequence;
        Tick(70224);
        Assert.Equal(0, ppu.Ly);
        Assert.Equal(0, ppu.Dot);
        Assert.Equal(0, ppu.Mode);
        Assert.Equal(sequence, video.Sequence);
        Assert.Equal(completed, video.CompletedFrameCount);
        Assert.All(Pixels(), value => Assert.Equal(255, value));

        // Enabled again, the first frame completes but is not shown.
        On();
        Assert.Equal(sequence + 1, video.Sequence); // The blank, with the LCD now on.
        Tick((456 * 144) - 4);
        Assert.All(Pixels(), value => Assert.Equal(255, value));
        Tick(4);
        Assert.Equal(completed + 1, video.CompletedFrameCount);
        Assert.Equal(sequence + 1, video.Sequence);
        Assert.All(Pixels(), value => Assert.Equal(255, value));
        Tick(70224);
        Assert.Equal(0, Pixels()[0]);
        Assert.Equal(completed + 2, video.CompletedFrameCount);
        Assert.Equal(sequence + 2, video.Sequence);
    }

    [Fact]
    public void ResetClearsMemoryAndReturnsToThePostBootPhase()
    {
        Off();
        ppu.WriteMemory(0x9FFF, 0x42);
        ppu.WriteMemory(0xFE9F, 0x55);
        On();
        Tick(1000);
        video.Reset();
        ppu.Reset();
        Assert.Equal(0, ppu.PeekMemory(0x9FFF));
        Assert.Equal(0, ppu.PeekMemory(0xFE9F));
        Assert.All(Pixels(), value => Assert.Equal(255, value));

        // Reset is line 153 in mode 1 with LY reading 0 and LY=LYC; line 0 begins 56 dots later.
        Assert.Equal((0, DmgBootProfile.PpuDot, (byte)1), (ppu.Ly, ppu.Dot, ppu.Mode));
        Assert.Equal(0x91, ppu.ReadRegister(0xFF40));
        Assert.Equal(0x85, ppu.ReadRegister(0xFF41));
        Tick(52);
        Assert.Equal(0x85, ppu.ReadRegister(0xFF41));
        Tick(4);
        Assert.Equal(0x84, ppu.ReadRegister(0xFF41));
        Assert.Equal(0, ppu.Dot);
        Tick(4);
        Assert.Equal(0x86, ppu.ReadRegister(0xFF41));
    }
}
