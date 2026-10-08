namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Memory;
using GonFox.GameBoy.Core.Video;

// PPU state at line and mode boundaries, sampled every M-cycle (four ticks) as the CPU sees it.
[Trait("Category", "Unit")]
public sealed class PpuBoundaryTests
{
    private readonly Interrupts irq = new();
    private readonly VideoOutput video = new();
    private readonly Ppu ppu;
    private readonly MemoryBus bus = new();

    public PpuBoundaryTests()
    {
        ppu = new Ppu(irq, video);
        ppu.Reset();
        bus.ConnectPpu(ppu);
        ppu.WriteRegister(0xFF40, 0);
        irq.WriteFlags(0);
    }

    private void Tick(int count)
    {
        for (var i = 0; i < count; i++)
        {
            ppu.Tick();
        }
    }

    private void LcdOn() => ppu.WriteRegister(0xFF40, 0x81);
    private byte Stat => ppu.ReadRegister(0xFF41);

    // Reads 4 * nops + 8 ticks after LCD on: LY, STAT with LYC 0 and 1, OAM and VRAM access.
    [Theory]
    [InlineData(0, 0x00, 0x84, 0x80, true, true)]
    [InlineData(17, 0x00, 0x84, 0x80, true, true)]
    [InlineData(60, 0x00, 0x87, 0x83, false, false)]
    [InlineData(110, 0x00, 0x84, 0x80, true, true)]
    [InlineData(130, 0x01, 0x82, 0x86, false, true)]
    [InlineData(174, 0x01, 0x83, 0x87, false, false)]
    [InlineData(224, 0x01, 0x80, 0x84, true, true)]
    [InlineData(244, 0x02, 0x82, 0x82, false, true)]
    [InlineData(1, 0x00, 0x84, 0x80, true, true)]
    [InlineData(18, 0x00, 0x87, 0x83, false, false)]
    [InlineData(61, 0x00, 0x84, 0x80, true, true)]
    [InlineData(111, 0x01, 0x80, 0x80, false, true)]
    [InlineData(131, 0x01, 0x82, 0x86, false, false)]
    [InlineData(175, 0x01, 0x80, 0x84, true, true)]
    [InlineData(225, 0x02, 0x80, 0x80, false, true)]
    [InlineData(245, 0x02, 0x82, 0x82, false, false)]
    [InlineData(2, 0x00, 0x84, 0x80, true, true)]
    [InlineData(19, 0x00, 0x87, 0x83, false, false)]
    [InlineData(62, 0x00, 0x84, 0x80, true, true)]
    [InlineData(112, 0x01, 0x82, 0x86, false, true)]
    [InlineData(132, 0x01, 0x83, 0x87, false, false)]
    [InlineData(176, 0x01, 0x80, 0x84, true, true)]
    [InlineData(226, 0x02, 0x82, 0x82, false, true)]
    [InlineData(246, 0x02, 0x83, 0x83, false, false)]
    public void LcdOnFirstLinesMatchDmgReadSamples(int nops, byte ly, byte statLyc0, byte statLyc1, bool oam, bool vram)
    {
        ppu.WriteMemory(0xFE00, 0);
        ppu.WriteMemory(0x8000, 0);
        foreach (var lyc in new byte[] { 0, 1 })
        {
            ppu.WriteRegister(0xFF40, 0);
            ppu.WriteRegister(0xFF45, lyc);
            LcdOn();
            Tick((4 * nops) + 8);
            Assert.Equal(ly, ppu.ReadRegister(0xFF44));
            Assert.Equal(lyc == 0 ? statLyc0 : statLyc1, Stat);
            Assert.Equal(oam ? 0 : 0xFF, bus.ReadByte(0xFE00));
            Assert.Equal(vram ? 0 : 0xFF, bus.ReadByte(0x8000));
        }
    }

    // OAM and VRAM writes 4 * nops + 8 ticks after LCD on, blocked or passed by the mode.
    [Theory]
    [InlineData(0, true, true)]
    [InlineData(17, true, true)]
    [InlineData(18, false, false)]
    [InlineData(60, false, false)]
    [InlineData(61, true, true)]
    [InlineData(110, true, true)]
    [InlineData(111, true, true)]
    [InlineData(112, false, true)]
    [InlineData(130, false, true)]
    [InlineData(131, true, true)]
    [InlineData(132, false, false)]
    [InlineData(174, false, false)]
    [InlineData(175, true, true)]
    [InlineData(224, true, true)]
    [InlineData(225, true, true)]
    [InlineData(226, false, true)]
    [InlineData(244, false, true)]
    [InlineData(245, true, true)]
    [InlineData(246, false, false)]
    public void LcdOnWritesMatchDmgWriteSamples(int nops, bool oam, bool vram)
    {
        ppu.WriteMemory(0xFE00, 0);
        ppu.WriteMemory(0x8000, 0);
        LcdOn();
        Tick((4 * nops) + 8);
        bus.WriteByte(0xFE00, 0x81);
        bus.WriteByte(0x8000, 0x81);
        Assert.Equal(oam ? 0x81 : 0, ppu.PeekMemory(0xFE00));
        Assert.Equal(vram ? 0x81 : 0, ppu.PeekMemory(0x8000));
    }

    [Fact]
    public void LineStartChangesLyAndStartsOamScanOneMachineCycleLater()
    {
        ppu.WriteRegister(0xFF41, 0x28);
        ppu.WriteRegister(0xFF45, 1);
        LcdOn();
        Tick(448);
        irq.WriteFlags(0); // Line 0, last HBlank sample.
        Assert.Equal((0, 0xA8), (ppu.ReadRegister(0xFF44), Stat));
        Tick(4); // LY changes first.
        Assert.Equal((1, 0xA8), (ppu.ReadRegister(0xFF44), Stat));
        Assert.Equal(0, irq.Flags & 2); // The STAT line is already high.
        Tick(4);
        Assert.Equal(0xAE, Stat);
    }

    [Fact]
    public void OamInterruptFiresAtLineStartAndNotWhenEnabledDuringOamScan()
    {
        LcdOn();
        Tick(456 + 20); // Line 1, OAM scan.
        ppu.WriteRegister(0xFF41, 0x20);
        irq.WriteFlags(0);
        Tick(428);
        Assert.Equal(0, irq.Flags & 2); // Still clear.
        Tick(4);
        Assert.Equal(2, irq.Flags & 2); // Requested with the LY change.
        Assert.Equal(2, ppu.ReadRegister(0xFF44));
        Assert.Equal(0, Stat & 3);
    }

    [Fact]
    public void VblankStartAlsoRaisesTheOamSourceOnDmg()
    {
        ppu.WriteRegister(0xFF41, 0x20);
        LcdOn();
        Tick((456 * 143) + 260);
        irq.WriteFlags(0);
        Tick(188);
        Assert.Equal(0, irq.Flags & 3); // Line 143, last sample.
        Tick(4);
        Assert.Equal(2, irq.Flags & 3); // Line 144: OAM source, not VBlank.
        Tick(4);
        Assert.Equal(3, irq.Flags & 3);
        Assert.Equal(1, Stat & 3);
    }

    // LY and STAT through line 153 into line 0, for LYC 0 and 153 with the LYC source enabled.
    [Theory]
    [InlineData(0, 0, 0x99, 0xC1)]
    [InlineData(0, 4, 0x00, 0xC1)]
    [InlineData(0, 8, 0x00, 0xC1)]
    [InlineData(0, 12, 0x00, 0xC5)]
    [InlineData(0, 452, 0x00, 0xC5)]
    [InlineData(0, 456, 0x00, 0xC4)]
    [InlineData(0, 460, 0x00, 0xC6)]
    [InlineData(0, 536, 0x00, 0xC6)]
    [InlineData(0, 540, 0x00, 0xC7)]
    [InlineData(0, 712, 0x00, 0xC4)]
    [InlineData(0, 912, 0x01, 0xC0)]
    [InlineData(0, 916, 0x01, 0xC2)]
    [InlineData(153, 0, 0x99, 0xC1)]
    [InlineData(153, 4, 0x00, 0xC5)]
    [InlineData(153, 8, 0x00, 0xC1)]
    [InlineData(153, 452, 0x00, 0xC1)]
    [InlineData(153, 456, 0x00, 0xC0)]
    [InlineData(153, 460, 0x00, 0xC2)]
    public void Line153ReadsZeroEarlyAndComparesItsHiddenValues(byte lyc, int afterLineStart, byte ly, byte stat)
    {
        ppu.WriteRegister(0xFF41, 0x40);
        ppu.WriteRegister(0xFF45, lyc);
        LcdOn();
        Tick((456 * 153) - 4 + afterLineStart); // LCD on starts at Dot 4.
        Assert.Equal(ly, ppu.ReadRegister(0xFF44));
        Assert.Equal(stat, Stat);
    }

    [Theory]
    [InlineData(153, 456 * 153)] // The short LY=153 comparison.
    [InlineData(0, (456 * 153) + 8)] // Matched in line 153.
    public void LycInterruptForLine153AndLine0FollowsTheHiddenComparison(byte lyc, int requestedBy)
    {
        ppu.WriteRegister(0xFF41, 0x40);
        ppu.WriteRegister(0xFF45, lyc);
        LcdOn();
        Tick(requestedBy - 4 - 4);
        irq.WriteFlags(0);
        Tick(4);
        Assert.Equal(0, irq.Flags & 2);
        Tick(4);
        Assert.Equal(2, irq.Flags & 2);
        irq.WriteFlags(0);
        Tick(456);
        Assert.Equal(0, irq.Flags & 2); // No second edge at line 0.
    }

    // A STAT write briefly enables every source, so an active one requests the interrupt (LYC=0).
    [Theory]
    [InlineData(16, 0x00, true)] // LY=LYC=0.
    [InlineData(452, 0x00, true)] // Line 1 starts: OAM source.
    [InlineData(456, 0x00, false)] // Later in OAM scan: none.
    [InlineData(704, 0x00, false)] // Drawing.
    [InlineData(708, 0x00, true)] // HBlank.
    [InlineData(908, 0x00, true)] // Line 2 starts.
    [InlineData(912, 0x00, false)] // Past line 2's start.
    [InlineData(65456, 0x00, false)] // Line 143, drawing.
    [InlineData(65460, 0x00, true)] // Line 143, HBlank.
    [InlineData(456 * 150, 0x00, true)] // VBlank.
    [InlineData(708, 0x08, false)] // HBlank source already high.
    public void StatWriteGlitchRequestsOnlyFromActiveSources(int afterLcdOn, byte enabledBefore, bool requested)
    {
        ppu.WriteRegister(0xFF41, enabledBefore);
        LcdOn();
        Tick(afterLcdOn);
        irq.WriteFlags(0);
        ppu.WriteRegister(0xFF41, 0x00);
        Assert.Equal(requested ? 2 : 0, irq.Flags & 2);
        Assert.Equal(0x80 | (ppu.ReadRegister(0xFF41) & 7), ppu.ReadRegister(0xFF41)); // Real value stored.
    }

    // With the LCD off, STAT writes still use the frozen LY=LYC result, the write quirk included.
    [Fact]
    public void StatWritesWithTheLcdOffUseTheFrozenComparison()
    {
        ppu.WriteRegister(0xFF41, 0x00);
        Assert.Equal(2, irq.Flags & 2); // Quirk: every source.
        irq.WriteFlags(0);
        ppu.WriteRegister(0xFF41, 0x40);
        Assert.Equal(2, irq.Flags & 2); // A new edge.
        irq.WriteFlags(0);
        ppu.WriteRegister(0xFF45, 1);
        Assert.Equal(0, irq.Flags & 2); // LYC writes keep the frozen result.
        ppu.WriteRegister(0xFF41, 0x40);
        Assert.Equal(0, irq.Flags & 2); // Already high.
        ppu.WriteRegister(0xFF41, 0x00);
        Assert.Equal(0, irq.Flags & 2);
        ppu.WriteRegister(0xFF41, 0x00);
        Assert.Equal(2, irq.Flags & 2);
    }

    [Fact]
    public void LcdOffFreezesComparisonAndLcdOnComparesLineZero()
    {
        ppu.WriteRegister(0xFF41, 0x40);
        LcdOn();
        Tick(456 * 144);
        ppu.WriteRegister(0xFF45, 144);
        Tick(4);
        irq.WriteFlags(0); // Compared a dot after the write.
        ppu.WriteRegister(0xFF40, 0);
        Assert.Equal(0xC4, Stat);
        ppu.WriteRegister(0xFF45, 1);
        Assert.Equal(0xC4, Stat);
        LcdOn();
        Assert.Equal(0xC0, Stat);
        Assert.Equal(0, irq.Flags & 2);
        ppu.WriteRegister(0xFF40, 0);
        ppu.WriteRegister(0xFF45, 0);
        LcdOn();
        Assert.Equal(0xC4, Stat);
        Assert.Equal(2, irq.Flags & 2); // Rising edge from the off state.
    }
}
