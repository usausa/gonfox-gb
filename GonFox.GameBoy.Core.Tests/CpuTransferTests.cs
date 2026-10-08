namespace GonFox.GameBoy.Core;

[Trait("Category", "Unit")]
public sealed class CpuTransferTests
{
    [Theory]
    [InlineData(0x02, 0x0A, 0xC200)]
    [InlineData(0x12, 0x1A, 0xC300)]
    public void BcAndDeIndirectLoadsUseTheSelectedAddress(byte store, byte load, ushort address)
    {
        var m = new CpuTestMachine([store, 0x3E, 0, load], af: 0x42B0, bc: 0xC200, de: 0xC300);
        Assert.Equal(8, m.Cpu.StepInstruction());
        Assert.Equal(0x42, m.Bus.ReadByte(address));
        m.Cpu.StepInstruction();
        Assert.Equal(8, m.Cpu.StepInstruction());
        Assert.Equal(0x42B0, m.State.AF);
        Assert.Equal(0xC200, m.State.BC);
        Assert.Equal(0xC300, m.State.DE);
    }

    [Theory]
    [InlineData(0x22, 0xC101, false)]
    [InlineData(0x32, 0xC0FF, false)]
    [InlineData(0x2A, 0xC101, true)]
    [InlineData(0x3A, 0xC0FF, true)]
    public void HlPostIndexChangesAddressAfterTransfer(byte opcode, ushort expectedHl, bool load)
    {
        var m = new CpuTestMachine([opcode], af: 0x99B0);
        m.Bus.WriteByte(0xC100, 0x42);
        Assert.Equal(8, m.Cpu.StepInstruction());
        Assert.Equal(expectedHl, m.State.HL);
        Assert.Equal(load ? 0x42 : 0x99, m.State.A);
        Assert.Equal(load ? 0x42 : 0x99, m.Bus.ReadByte(0xC100));
        Assert.Equal(0xB0, m.State.F);
    }

    [Theory]
    [InlineData(0x22, 0xFFFF, 0x0000)]
    [InlineData(0x2A, 0xFFFF, 0x0000)]
    [InlineData(0x32, 0x0000, 0xFFFF)]
    [InlineData(0x3A, 0x0000, 0xFFFF)]
    public void HlPostIndexWrapsAt16Bits(byte opcode, ushort start, ushort end)
    {
        var m = new CpuTestMachine([opcode], hl: start);
        m.Cpu.StepInstruction();
        Assert.Equal(end, m.State.HL);
    }

    [Fact]
    public void HighMemoryAddressingUsesFf00PlusImmediateOrC()
    {
        var m = new CpuTestMachine([0xE0, 0x80, 0xE2, 0x3E, 0, 0xF0, 0x80, 0xF2], af: 0x42B0, bc: 0x0081);
        Assert.Equal(12, m.Cpu.StepInstruction());
        Assert.Equal(8, m.Cpu.StepInstruction());
        Assert.Equal(0x42, m.Bus.ReadByte(0xFF80));
        Assert.Equal(0x42, m.Bus.ReadByte(0xFF81));
        m.Cpu.StepInstruction();
        Assert.Equal(12, m.Cpu.StepInstruction());
        Assert.Equal(0x42B0, m.State.AF);
        m.Bus.WriteByte(0xFF81, 0x99);
        Assert.Equal(8, m.Cpu.StepInstruction());
        Assert.Equal(0x99B0, m.State.AF);
    }

    [Fact]
    public void StoreSpWritesLowThenHighAndWrapsAddress()
    {
        var m = new CpuTestMachine([0x08, 0x00, 0xA0], sp: 0x1234);
        Assert.Equal(20, m.Cpu.StepInstruction());
        Assert.Equal(new (bool, ushort, byte, ulong)[]
        {
            (true, 0xA000, 0x34, 12), (true, 0xA001, 0x12, 16)
        }, m.Accesses.Where(access => access.Write));
        m.Reset([0x08, 0xFF, 0xFF], sp: 0x1234);
        Assert.Equal(20, m.Cpu.StepInstruction());
        Assert.Equal((true, (ushort)0, (byte)0x12, 16UL), Assert.Single(m.Accesses, access => access.Write));
        Assert.Equal(0x1234, m.State.SP);
    }

    [Theory]
    [InlineData(0x03, 0x0B, "BC")]
    [InlineData(0x13, 0x1B, "DE")]
    [InlineData(0x23, 0x2B, "HL")]
    [InlineData(0x33, 0x3B, "SP")]
    public void WordIncDecWrapAndLeaveAllFlags(byte increment, byte decrement, string pair)
    {
        var m = new CpuTestMachine([increment, decrement], af: 0x00F0, bc: 0xFFFF, de: 0xFFFF, hl: 0xFFFF, sp: 0xFFFF);
        Assert.Equal(8, m.Cpu.StepInstruction());
        Assert.Equal(0, Pair(m.State, pair));
        Assert.Equal(0xF0, m.State.F);
        Assert.Equal(8, m.Cpu.StepInstruction());
        Assert.Equal(0xFFFF, Pair(m.State, pair));
        Assert.Equal(0xF0, m.State.F);
    }

    [Theory]
    [InlineData(0xC5, 0xC1, "BC", 0x1234)]
    [InlineData(0xD5, 0xD1, "DE", 0x5678)]
    [InlineData(0xE5, 0xE1, "HL", 0x9ABC)]
    [InlineData(0xF5, 0xF1, "AF", 0xDEF0)]
    public void StackPairsAreLittleEndianAndPopReplacesTheTarget(byte push, byte pop, string pair, ushort initial)
    {
        var m = new CpuTestMachine([push, pop], af: 0xDEF0, bc: 0x1234, de: 0x5678, hl: 0x9ABC);
        Assert.Equal(16, m.Cpu.StepInstruction());
        Assert.Equal(0xCFFE, m.State.SP);
        Assert.Equal((byte)initial, m.Bus.ReadByte(0xCFFE));
        Assert.Equal((byte)(initial >> 8), m.Bus.ReadByte(0xCFFF));
        m.Bus.WriteByte(0xCFFE, 0xFF);
        m.Bus.WriteByte(0xCFFF, 0x42);
        Assert.Equal(12, m.Cpu.StepInstruction());
        Assert.Equal(pair == "AF" ? 0x42F0 : 0x42FF, Pair(m.State, pair));
        Assert.Equal(0xD000, m.State.SP);
    }

    [Fact]
    public void StackPointerWrapsOnPushAndPop()
    {
        var m = new CpuTestMachine([0xC5, 0xD1], bc: 0x1234, sp: 0);
        m.Cpu.StepInstruction();
        Assert.Equal(0xFFFE, m.State.SP);
        Assert.Equal(0x34, m.Bus.ReadByte(0xFFFE));
        m.Cpu.StepInstruction();
        Assert.Equal(0, m.State.SP);
        Assert.Equal(0xFF34, m.State.DE); // Unconnected IE reads FF.
    }

    [Fact]
    public void LoadSpFromHlPreservesFlags()
    {
        var m = new CpuTestMachine([0xF9], af: 0x00F0, hl: 0x1234);
        Assert.Equal(8, m.Cpu.StepInstruction());
        Assert.Equal(0x1234, m.State.SP);
        Assert.Equal(0x1234, m.State.HL);
        Assert.Equal(0xF0, m.State.F);
    }

    private static ushort Pair(DebugSnapshot state, string pair) => pair switch
    {
        "AF" => state.AF,
        "BC" => state.BC,
        "DE" => state.DE,
        "HL" => state.HL,
        "SP" => state.SP,
        _ => throw new ArgumentException(pair)
    };
}
