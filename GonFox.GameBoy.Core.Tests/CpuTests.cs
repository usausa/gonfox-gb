namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Cpu;
using GonFox.GameBoy.Core.Memory;

[Trait("Category", "Unit")]
public sealed class CpuTests
{
    [Fact]
    public void NopOnlyAdvancesPcAndOneMachineCycle()
    {
        var system = TestRom.Start(0x00);
        var before = system.GetDebugSnapshot();
        Assert.Equal(new RunResult(4, false), system.StepInstruction());
        Assert.Equal(before with { PC = 0x151, TotalTCycles = 20, DividerCounter = 0xABE0, PpuDot = 420 }, system.GetDebugSnapshot());
    }

    [Theory]
    [InlineData(0x06, "B")]
    [InlineData(0x0E, "C")]
    [InlineData(0x16, "D")]
    [InlineData(0x1E, "E")]
    [InlineData(0x26, "H")]
    [InlineData(0x2E, "L")]
    [InlineData(0x3E, "A")]
    public void ImmediateByteLoadsChangeOnlyTheSelectedRegister(byte opcode, string register)
    {
        var system = TestRom.Start(opcode, 0x42);
        var before = system.GetDebugSnapshot();
        var expected = register switch
        {
            "B" => before with { BC = 0x4213 },
            "C" => before with { BC = 0x0042 },
            "D" => before with { DE = 0x42D8 },
            "E" => before with { DE = 0x0042 },
            "H" => before with { HL = 0x424D },
            "L" => before with { HL = 0x0142 },
            "A" => before with { AF = 0x42B0 },
            _ => throw new ArgumentException(register)
        };

        Assert.Equal(8, system.StepInstruction().ExecutedTCycles);
        Assert.Equal(expected with { PC = 0x152, TotalTCycles = 24, DividerCounter = 0xABE4, PpuDot = 424 }, system.GetDebugSnapshot());
    }

    [Theory]
    [InlineData(0x01, "BC")]
    [InlineData(0x11, "DE")]
    [InlineData(0x21, "HL")]
    [InlineData(0x31, "SP")]
    public void ImmediateWordLoadsAreLittleEndian(byte opcode, string register)
    {
        var system = TestRom.Start(opcode, 0x34, 0x12);
        var before = system.GetDebugSnapshot();
        var expected = register switch
        {
            "BC" => before with { BC = 0x1234 },
            "DE" => before with { DE = 0x1234 },
            "HL" => before with { HL = 0x1234 },
            "SP" => before with { SP = 0x1234 },
            _ => throw new ArgumentException(register)
        };

        Assert.Equal(12, system.StepInstruction().ExecutedTCycles);
        Assert.Equal(expected with { PC = 0x153, TotalTCycles = 28, DividerCounter = 0xABE8, PpuDot = 428 }, system.GetDebugSnapshot());
    }

    [Fact]
    public void RegisterTransfersPreserveFlagsAndIncludeSelfLoads()
    {
        var system = TestRom.Start(
">BGHQZcl}@"u8.ToArray());
        system.StepInstruction();
        for (var index = 0; index < 8; index++)
        {
            Assert.Equal(4, system.StepInstruction().ExecutedTCycles);
        }

        var snapshot = system.GetDebugSnapshot();
        Assert.Equal(0x42B0, snapshot.AF);
        Assert.Equal(0x4242, snapshot.BC);
        Assert.Equal(0x4242, snapshot.DE);
        Assert.Equal(0x4242, snapshot.HL);
        Assert.Equal(0x15A, snapshot.PC);
    }

    [Fact]
    public void IndirectLoadsUseOldHlBeforeChangingH()
    {
        var system = TestRom.Start(
            0x21, 0x00, 0xC0, // LD HL,C000
            0x36, 0x12,       // LD (HL),12
            0x46,             // LD B,(HL)
            0x70,             // LD (HL),B
            0x66);            // LD H,(HL)
        Assert.Equal(12, system.StepInstruction().ExecutedTCycles);
        Assert.Equal(12, system.StepInstruction().ExecutedTCycles);
        Assert.Equal(8, system.StepInstruction().ExecutedTCycles);
        Assert.Equal(8, system.StepInstruction().ExecutedTCycles);
        Assert.Equal(8, system.StepInstruction().ExecutedTCycles);
        Assert.Equal(0x12, system.GetDebugSnapshot().B);
        Assert.Equal(0x1200, system.GetDebugSnapshot().HL);
        Assert.Equal(0xB0, system.GetDebugSnapshot().F);
        var memory = new byte[1];
        system.CopyMemory(0xC000, memory);
        Assert.Equal(0x12, memory[0]);
    }

    [Theory]
    [InlineData(0x00, 0x00, 0x00, 0x80)] // Zero; old carry is ignored.
    [InlineData(0x0F, 0x01, 0x10, 0x20)] // Half carry only.
    [InlineData(0xFF, 0x01, 0x00, 0xB0)] // Zero, half carry, carry.
    [InlineData(0x80, 0x80, 0x00, 0x90)] // Zero and carry, no half carry.
    [InlineData(0x01, 0x01, 0x02, 0x00)] // Clear all flags.
    [InlineData(0xFF, 0xFF, 0xFE, 0x30)] // Carry without zero.
    public void AddImmediateSetsFlagsAtArithmeticBoundaries(byte left, byte right, byte expected, byte flags)
    {
        var system = TestRom.Start(0x3E, left, 0xC6, right);
        system.StepInstruction();
        Assert.Equal(8, system.StepInstruction().ExecutedTCycles);
        Assert.Equal(expected, system.GetDebugSnapshot().A);
        Assert.Equal(flags, system.GetDebugSnapshot().F);
    }

    [Theory]
    [InlineData(0x06, 0x80)] // B
    [InlineData(0x0E, 0x81)] // C
    [InlineData(0x16, 0x82)] // D
    [InlineData(0x1E, 0x83)] // E
    [InlineData(0x26, 0x84)] // H
    [InlineData(0x2E, 0x85)] // L
    public void AddRegisterReadsTheSelectedOperand(byte loadOpcode, byte addOpcode)
    {
        var system = TestRom.Start(0x3E, 0x0F, loadOpcode, 0x01, addOpcode);
        system.RunForTCycles(16);
        Assert.Equal(4, system.StepInstruction().ExecutedTCycles);
        Assert.Equal(0x1020, system.GetDebugSnapshot().AF);
    }

    [Fact]
    public void AddAccumulatorToItselfReadsTheOriginalValue()
    {
        var system = TestRom.Start(0x3E, 0x88, 0x87);
        system.StepInstruction();
        Assert.Equal(4, system.StepInstruction().ExecutedTCycles);
        Assert.Equal(0x1030, system.GetDebugSnapshot().AF);
    }

    [Fact]
    public void AddIndirectUsesOneExtraReadCycleAndDoesNotChangeMemory()
    {
        var system = TestRom.Start(0x21, 0x00, 0xC0, 0x36, 0xFF, 0x86);
        system.RunForTCycles(24); // A = 01, (C000) = FF.
        Assert.Equal(8, system.StepInstruction().ExecutedTCycles);
        Assert.Equal(0x00B0, system.GetDebugSnapshot().AF);
        var memory = new byte[1];
        system.CopyMemory(0xC000, memory);
        Assert.Equal(0xFF, memory[0]);
    }

    [Fact]
    public void BusAccessesPrecedeEachMachineCycleAndJumpIncludesIdleTime()
    {
        var clock = new Clock();
        var bus = new MemoryBus();
        var cartridge = new TimedCartridge(clock);
        bus.ConnectCartridge(cartridge);
        var cpu = new Sm83Cpu(bus, clock);
        cpu.Reset();

        Assert.Equal(16, cpu.StepInstruction()); // JP 0150
        Assert.Equal(16, cpu.StepInstruction()); // LD (A000),A
        Assert.Equal(16, cpu.StepInstruction()); // LD A,(A000)
        Assert.Equal(new (bool Write, ushort Address, byte Value, ulong Time)[]
        {
            (false, 0x100, 0xC3, 0), (false, 0x101, 0x50, 4), (false, 0x102, 0x01, 8),
            (false, 0x150, 0xEA, 16), (false, 0x151, 0x00, 20), (false, 0x152, 0xA0, 24),
            (true, 0xA000, 0x01, 28),
            (false, 0x153, 0xFA, 32), (false, 0x154, 0x00, 36), (false, 0x155, 0xA0, 40),
            (false, 0xA000, 0x42, 44)
        }, cartridge.Accesses);
        Assert.Equal(48UL, clock.TotalTCycles);
        Assert.Equal(0x42, cpu.GetDebugSnapshot().A);
    }

    [Fact]
    public void FetchWrapsPcFromFfffToZeroBeforeRstPushesReturnAddress()
    {
        var system = TestRom.Start(0x3E, 0xFF, 0xEA, 0xFF, 0xFF, 0xC3, 0xFF, 0xFF);
        system.RunForTCycles(24); // IE = FF, fetched as RST 38.
        Assert.Equal(16, system.StepInstruction().ExecutedTCycles);
        Assert.Equal(16, system.StepInstruction().ExecutedTCycles);
        Assert.Equal(0x38, system.GetDebugSnapshot().PC);
        Assert.Equal(0xFFFC, system.GetDebugSnapshot().SP);
        var returnAddress = new byte[2];
        system.CopyMemory(0xFFFC, returnAddress);
        // ReSharper disable once UseUtf8StringLiteral
        Assert.Equal(new byte[] { 0, 0 }, returnAddress);
        Assert.Equal(72UL, system.TotalTCycles);
    }

    private sealed class TimedCartridge(Clock clock) : ICartridge
    {
        private readonly byte[] rom = TestRom.Create(0xEA, 0x00, 0xA0, 0xFA, 0x00, 0xA0);
        internal List<(bool Write, ushort Address, byte Value, ulong Time)> Accesses { get; } = [];

        public byte Read(ushort address)
        {
            var value = address < 0x8000 ? rom[address] : (byte)0x42;
            Accesses.Add((false, address, value, clock.TotalTCycles));
            return value;
        }

        public void Write(ushort address, byte value) => Accesses.Add((true, address, value, clock.TotalTCycles));

        public void ResetController()
        {
        }
    }
}
