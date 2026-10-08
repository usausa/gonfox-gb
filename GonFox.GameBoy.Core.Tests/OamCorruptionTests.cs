namespace GonFox.GameBoy.Core;

using System.Globalization;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Cpu;
using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Memory;
using GonFox.GameBoy.Core.Video;

// OAM corruption: during the scan, FE00-FEFF accesses and IDU values damage the row being read.
[Trait("Category", "Unit")]
public sealed class OamCorruptionTests
{
    private readonly MemoryBus bus = new();
    private readonly Clock clock = new();
    private readonly Ppu ppu = new(new Interrupts(), new VideoOutput());

    public OamCorruptionTests()
    {
        bus.ConnectPpu(ppu);
        clock.ConnectPpu(ppu);
        ppu.Reset();
        ppu.WriteRegister(0xFF40, 0);
        Fill();
        ppu.WriteRegister(0xFF40, 0x91); // Line 0 has no scan; line 1 has.
    }

    private static byte Pattern(int index) => (byte)((index * 37) + 11);

    private void Fill()
    {
        for (var i = 0; i < 160; i++)
        {
            ppu.WriteOamDma(i, Pattern(i));
        }
    }

    private void TickTo(int line, int dot)
    {
        while (ppu.Ly != line || ppu.Dot != dot || (line < 144 && ppu.Mode == 1))
        {
            clock.AdvanceTCycles(1);
        }
    }

    // A pseudo-random OAM from a linear congruential generator, each byte its bits 16-23.
    private static byte[] Shuffled(int seed)
    {
        var bytes = new byte[160];
        long x = seed;
        for (var i = 0; i < 160; i++)
        {
            x = ((x * 1103515245) + 12345) & 0x7FFFFFFF;
            bytes[i] = (byte)(x >> 16);
        }
        return bytes;
    }

    private void AssertRows(string changes, byte[]? initial = null)
    {
        var expected = new byte[160];
        for (var i = 0; i < 160; i++)
        {
            expected[i] = initial?[i] ?? Pattern(i);
        }

        foreach (var change in changes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = change.Split(':');
            Convert.FromHexString(parts[1]).CopyTo(expected, int.Parse(parts[0], CultureInfo.InvariantCulture) * 8);
        }
        for (var i = 0; i < 160; i++)
        {
            Assert.True(expected[i] == bus.PeekByte((ushort)(0xFE00 + i)), $"OAM {i:X2}");
        }
    }

    // A write corrupts row k, read at Dots 4k to 4k + 3, from its own and the previous row's words.
    [Theory]
    [InlineData(4, "1:1B50557A9FC4E90E")]
    [InlineData(7, "1:1B50557A9FC4E90E")]
    [InlineData(8, "2:53C87DA2C7EC1136")]
    [InlineData(40, "10:1388BDE2072C5176")]
    [InlineData(64, "16:E398ADD2F71C4166")]
    [InlineData(76, "19:4B00254A6F94B9DE")]
    [InlineData(79, "19:4B00254A6F94B9DE")]
    public void WriteCorruptsTheRowOfItsMCycle(int dot, string rows)
    {
        TickTo(1, dot);
        bus.WriteByte(0xFE42, 0x99); // The address and value play no part.
        AssertRows(rows);
    }

    // A read corrupts the row before (and further back for even rows), then copies it into the row.
    [Theory]
    [InlineData(1, "0:1B70557A9FC4E90E 1:1B70557A9FC4E90E")]
    [InlineData(2, "0:13587DA2C7EC1136 1:13587DA2C7EC1136 2:13587DA2C7EC1136")]
    [InlineData(3, "2:DB80A5CAEF14395E 3:DB80A5CAEF14395E")]
    [InlineData(4, "0:83A8CDF2173C6186 2:83A8CDF2173C6186 4:83A8CDF2173C6186")]
    [InlineData(5, "4:BBF0F51A3F6489AE 5:BBF0F51A3F6489AE")]
    [InlineData(6, "4:F3F81D42678CB1D6 5:F3F81D42678CB1D6 6:F3F81D42678CB1D6")]
    [InlineData(7, "7:FB20456A8FB4D9FE")]
    [InlineData(8, "4:A3486D92B7DC0126 6:A3486D92B7DC0126 7:A3486D92B7DC0126 8:A3486D92B7DC0126")]
    [InlineData(9, "8:5B7095BADF04294E 9:5B7095BADF04294E")]
    [InlineData(10, "8:5398BDE2072C5176 9:5398BDE2072C5176 10:5398BDE2072C5176")]
    [InlineData(11, "11:9BC0E50A2F54799E")]
    [InlineData(12, "8:C3E80D32577CA1C6 10:C3E80D32577CA1C6 12:C3E80D32577CA1C6")]
    [InlineData(13, "12:FB30355A7FA4C9EE 13:FB30355A7FA4C9EE")]
    [InlineData(14, "12:33385D82A7CCF116 13:33385D82A7CCF116 14:33385D82A7CCF116")]
    [InlineData(15, "14:7BE085AACFF4193E 15:7BE085AACFF4193E")]
    [InlineData(16, "0:6388ADD2F71C4166 12:6388ADD2F71C4166 14:6388ADD2F71C4166 16:6388ADD2F71C4166")]
    [InlineData(17, "16:9BF0D5FA1F44698E 17:9BF0D5FA1F44698E")]
    [InlineData(18, "16:93D8FD22476C91B6 17:93D8FD22476C91B6 18:93D8FD22476C91B6")]
    [InlineData(19, "19:DB00254A6F94B9DE")]
    public void ReadCorruptsWithThePatternOfTheRow(int row, string rows)
    {
        TickTo(1, row * 4);
        Assert.Equal(0xFF, bus.ReadByte(0xFE10));
        AssertRows(rows);
    }

    // Reads in rows 4, 8, 12 and 16 each use their own formula, told apart on a pseudo-random OAM.
    [Theory]
    [InlineData(4, 10, "0:A97C20664A4641F8 2:A97C20664A4641F8 3:A97C20664A4641F8 4:A97C20664A4641F8")]
    [InlineData(8, 56, "4:EF9B73684FCD5C4C 6:EF9B73684FCD5C4C 7:EF9B73684FCD5C4C 8:EF9B73684FCD5C4C")]
    [InlineData(12, 57, "8:29B6A09F31726BD4 10:29B6A09F31726BD4 11:29B6A09F31726BD4 12:29B6A09F31726BD4")]
    [InlineData(16, 1, "0:9C72469571368F57 12:9C72469571368F57 14:9C72469571368F57 15:9C72469571368F57 16:9C72469571368F57")]
    public void QuarterRowReadsUseTheirOwnFormula(int row, int seed, string rows)
    {
        var oam = Shuffled(seed);
        for (var i = 0; i < 160; i++)
        {
            ppu.WriteOamDma(i, oam[i]);
        }

        TickTo(1, row * 4);
        Assert.Equal(0xFF, bus.ReadByte(0xFE10));
        AssertRows(rows, oam);
    }

    // Accesses while row 0 is read, in mode 3 or in VBlank corrupt nothing.
    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 2)]
    [InlineData(1, 84)]
    [InlineData(1, 200)]
    [InlineData(145, 40)]
    public void NothingIsCorruptedOutsideTheScanRows(int line, int dot)
    {
        TickTo(line, dot);
        bus.ReadByte(0xFE10);
        bus.ReadByte(0xFEA0);
        bus.IduAddress(0xFE00);
        if (ppu.Mode != 0 && ppu.Mode != 1)
        {
            bus.WriteByte(0xFE42, 0x99); // Open modes would take the write itself.
        }

        AssertRows(string.Empty);
    }

    [Fact]
    public void AtDot80TheWriteGoesThroughWithoutCorruption()
    {
        TickTo(1, 80);
        bus.WriteByte(0xFE42, 0x99);
        Assert.Equal(0x99, bus.PeekByte(0xFE42));
        ppu.WriteOamDma(0x42, Pattern(0x42));
        AssertRows(string.Empty);
    }

    [Fact]
    public void TheLineThatEnablesTheLcdAndOamDmaCorruptNothing()
    {
        TickTo(0, 40); // The LCD-on line: no scan.
        Assert.Equal(0, ppu.Mode);
        bus.IduAddress(0xFE00);
        bus.ReadByte(0xFE10);
        AssertRows(string.Empty);
        var dma = new OamDma(bus, ppu);
        bus.ConnectDma(dma);
        clock.ConnectDma(dma);
        for (var i = 0; i < 160; i++)
        {
            bus.WriteByte((ushort)(0xC000 + i), Pattern(i));
        }

        TickTo(1, 0);
        bus.WriteByte(0xFF46, 0xC0);
        TickTo(1, 40);
        Assert.True(dma.Active);
        bus.IduAddress(0xFE00);
        Assert.Equal(0xFF, bus.ReadByte(0xFE10));
        bus.WriteByte(0xFE42, 0x99);
        AssertRows(string.Empty);
    }

    // FEA0-FEFF reads 00 when OAM is open and FF when blocked, and corrupts like OAM during the scan.
    [Fact]
    public void UnusableAreaReadsZeroWhenOpenAndCorruptsDuringTheScan()
    {
        TickTo(1, 200);
        Assert.Equal(0xFF, bus.ReadByte(0xFEA0));
        TickTo(1, 300);
        Assert.Equal(0x00, bus.ReadByte(0xFEFF));
        bus.WriteByte(0xFEA0, 0x12);
        Assert.Equal(0x00, bus.ReadByte(0xFEA0));
        AssertRows(string.Empty);
        TickTo(2, 8);
        Assert.Equal(0xFF, bus.ReadByte(0xFEC3));
        AssertRows("0:13587DA2C7EC1136 1:13587DA2C7EC1136 2:13587DA2C7EC1136");
        Fill();
        TickTo(3, 40);
        bus.WriteByte(0xFEFF, 0x12);
        AssertRows("10:1388BDE2072C5176");
    }

    // The increment/decrement unit corrupts like a write for any value in FE00-FEFF, and only there.
    [Theory]
    [InlineData(0xFE00, true)]
    [InlineData(0xFEFF, true)]
    [InlineData(0xFDFF, false)]
    [InlineData(0xFF00, false)]
    public void IduValuesOnlyInFE00ToFEFFCorrupt(int value, bool corrupts)
    {
        TickTo(1, 40);
        bus.IduAddress((ushort)value);
        AssertRows(corrupts ? "10:1388BDE2072C5176" : string.Empty);
    }

    // Instructions passing FE00 through the IDU corrupt row 10 at Dot 40; 16-bit additions do not.
    [Theory]
    [InlineData("INC BC", new byte[] { 0x01, 0x00, 0xFE, 0x03 }, 1, true)]
    [InlineData("DEC BC", new byte[] { 0x01, 0x00, 0xFE, 0x0B }, 1, true)]
    [InlineData("INC DE", new byte[] { 0x11, 0x00, 0xFE, 0x13 }, 1, true)]
    [InlineData("DEC DE", new byte[] { 0x11, 0x00, 0xFE, 0x1B }, 1, true)]
    [InlineData("INC HL", new byte[] { 0x21, 0x00, 0xFE, 0x23 }, 1, true)]
    [InlineData("DEC HL", new byte[] { 0x21, 0x00, 0xFE, 0x2B }, 1, true)]
    [InlineData("INC SP", new byte[] { 0x31, 0x00, 0xFE, 0x33 }, 1, true)]
    [InlineData("DEC SP", new byte[] { 0x31, 0x00, 0xFE, 0x3B }, 1, true)]
    [InlineData("PUSH BC", new byte[] { 0x31, 0x00, 0xFE, 0xC5 }, 1, true)]
    [InlineData("CALL", new byte[] { 0x31, 0x00, 0xFE, 0xCD, 0x00, 0x02 }, 3, true)]
    [InlineData("RST 08", new byte[] { 0x31, 0x00, 0xFE, 0xCF }, 1, true)]
    [InlineData("LD SP,HL", new byte[] { 0x21, 0x00, 0xFE, 0xF9 }, 1, true)]
    [InlineData("ADD HL,BC", new byte[] { 0x21, 0x00, 0xFE, 0x01, 0x00, 0x00, 0x09 }, 1, false)]
    [InlineData("LD HL,SP+0", new byte[] { 0x31, 0x00, 0xFE, 0xF8, 0x00 }, 2, false)]
    [InlineData("ADD SP,0", new byte[] { 0x31, 0x00, 0xFE, 0xE8, 0x00 }, 2, false)]
    public void InstructionsMovingAnOamAddressThroughTheIduCorrupt(string name, byte[] program, int iduCycle, bool corrupts)
    {
        var cpu = new Sm83Cpu(bus, clock);
        bus.ConnectCartridge(CartridgeLoader.Load(TestRom.Create(program)).Cartridge);
        cpu.Reset();
        cpu.StepInstruction(); // JP 0150.
        var setups = name == "ADD HL,BC" ? 2 : 1;
        for (var i = 0; i < setups; i++)
        {
            cpu.StepInstruction();
        }

        TickTo(1, 40 - (4 * iduCycle));
        cpu.StepInstruction();
        AssertRows(corrupts ? "10:1388BDE2072C5176" : string.Empty);
    }

    // Interrupt dispatch decrements SP through the IDU in its second M-cycle.
    [Fact]
    public void InterruptDispatchWithTheStackInOamCorrupts()
    {
        var interrupts = new Interrupts();
        var cpu = new Sm83Cpu(bus, clock, interrupts);
        bus.ConnectCartridge(CartridgeLoader.Load(TestRom.Create(0x31, 0x00, 0xFE, 0xFB, 0x00, 0x00)).Cartridge);
        cpu.Reset();
        for (var i = 0; i < 4; i++)
        {
            cpu.StepInstruction(); // JP 0150, LD SP, EI, NOP.
        }

        interrupts.WriteEnable(0x01);
        interrupts.Request(InterruptSource.VBlank);
        TickTo(1, 40 - 4);
        cpu.StepInstruction();
        AssertRows("10:1388BDE2072C5176");
    }
}
