namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Cpu;

[Trait("Category", "Unit")]
public sealed class GameBoySystemTests
{
    [Fact]
    public void NewSystemHasFixedDmgProfileAndNoCartridge()
    {
        var system = new GameBoySystem();
        Assert.False(system.IsRomLoaded);
        Assert.False(system.IsFaulted);
        Assert.Equal(4_194_304, GameBoySystem.CyclesPerSecond);
        Assert.Equal(new DebugSnapshot(0x01B0, 0x0013, 0x00D8, 0x014D, 0xFFFE, 0x0100, 0)
        { DividerCounter = 0xABCC, InterruptFlags = 0xE1, TimerControl = 0xF8, LcdControl = 0x91, PpuMode = 1, PpuDot = 400, RomBank1 = 1 },
            system.GetDebugSnapshot());
    }

    [Fact]
    public void ExecutionWithoutCartridgeIsRejectedWithoutAdvancing()
    {
        var system = new GameBoySystem();
        var before = system.GetDebugSnapshot();
        Assert.Throws<InvalidOperationException>(() => system.StepInstruction());
        Assert.Throws<InvalidOperationException>(() => system.RunForTCycles(4));
        Assert.Throws<InvalidOperationException>(() => system.RunForTCycles(0));
        Assert.Equal(before, system.GetDebugSnapshot());
    }

    [Fact]
    public void ZeroBudgetDoesNothingAndNegativeBudgetIsRejected()
    {
        var system = TestRom.Start(0x00);
        var before = system.GetDebugSnapshot();
        Assert.Equal(new RunResult(0, false), system.RunForTCycles(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => system.RunForTCycles(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => system.RunForTCycles(int.MinValue));
        Assert.Equal(before, system.GetDebugSnapshot());
    }

    [Fact]
    public void BudgetFinishesCurrentInstructionAndDoesNotCarryDebtInsideCore()
    {
        // ReSharper disable once UseUtf8StringLiteral
        var system = TestRom.Start(0x3E, 0x42, 0x00);
        Assert.Equal(new RunResult(8, false), system.RunForTCycles(1));
        Assert.Equal(0x152, system.GetDebugSnapshot().PC);
        Assert.Equal(new RunResult(4, false), system.RunForTCycles(1));
        Assert.Equal(28UL, system.TotalTCycles); // Entry 16 + LD 8 + NOP 4.
    }

    [Fact]
    public void SyntheticProgramAgreesForSteppingAndBudgetExecution()
    {
        var image = TestRom.Create(
            0x31, 0xFE, 0xDF, // LD SP,DFFE  12 T
            0x21, 0x00, 0xC0, // LD HL,C000  12 T
            0x3E, 0x0F,       // LD A,0F      8 T
            0x06, 0x01,       // LD B,01      8 T
            0x80,             // ADD A,B      4 T
            0x77,             // LD (HL),A    8 T
            0x3E, 0x00,       // LD A,00      8 T
            0xFA, 0x00, 0xE0, // LD A,(E000) 16 T (Echo RAM)
            0xEA, 0x80, 0xFF, // LD (FF80),A 16 T (HRAM)
            0xC6, 0xF0,       // ADD A,F0     8 T
            0xC3, 0x50, 0x01); // JP 0150     16 T
        var stepped = new GameBoySystem();
        var budgeted = new GameBoySystem();
        stepped.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        budgeted.InsertCartridge(CartridgeLoader.Load(image).Cartridge);

        long executed = 0;
        for (var instruction = 0; instruction < 12; instruction++)
        {
            executed += stepped.StepInstruction().ExecutedTCycles;
        }

        Assert.Equal(132, executed); // Includes entry JP, 16 T.
        Assert.Equal(new RunResult(132, false), budgeted.RunForTCycles(131));
        var expected = new DebugSnapshot(0x0090, 0x0113, 0x00D8, 0xC000, 0xDFFE, 0x0150, 132)
        { DividerCounter = 0xAC50, InterruptFlags = 0xE1, TimerControl = 0xF8, LcdControl = 0x91, PpuMode = 2, PpuDot = 76, RomBank1 = 1 }; // Line 0 began at T=56.
        Assert.Equal(expected, stepped.GetDebugSnapshot());
        Assert.Equal(expected, budgeted.GetDebugSnapshot());

        var stepMemory = new byte[0x10000];
        var budgetMemory = new byte[0x10000];
        stepped.CopyMemory(0, stepMemory);
        budgeted.CopyMemory(0, budgetMemory);
        Assert.Equal(stepMemory, budgetMemory);
        Assert.Equal(0x10, stepMemory[0xC000]);
        Assert.Equal(0x10, stepMemory[0xE000]);
        Assert.Equal(0x10, stepMemory[0xFF80]);
        Assert.Equal(expected, stepped.GetDebugSnapshot()); // Inspection advances no time.
    }

    [Theory]
    [InlineData(0xD3)]
    [InlineData(0xDB)]
    [InlineData(0xDD)]
    [InlineData(0xE3)]
    [InlineData(0xE4)]
    [InlineData(0xEB)]
    [InlineData(0xEC)]
    [InlineData(0xED)]
    [InlineData(0xF4)]
    [InlineData(0xFC)]
    [InlineData(0xFD)]
    public void UndefinedOpcodeLocksTheCpuWhileTimeRunsOnUntilReset(byte opcode)
    {
        var system = TestRom.Start(opcode, 0x00);
        Assert.Equal(4, system.StepInstruction().ExecutedTCycles); // Only the opcode fetch.
        Assert.True(system.IsFaulted);
        Assert.Equal(new CpuFault(0x150, opcode), system.Fault);
        Assert.Contains("0x0150", system.Fault!.Value.Message, StringComparison.Ordinal);
        Assert.Contains($"0x{opcode:X2}", system.Fault.Value.Message, StringComparison.Ordinal);
        var locked = system.GetDebugSnapshot();
        Assert.Equal(0x151, locked.PC);
        Assert.Equal(20UL, locked.TotalTCycles); // Includes the undefined opcode's fetch.

        // From now on only time passes: one M-cycle per step, frames go on, registers stay.
        Assert.Equal(4, system.StepInstruction().ExecutedTCycles);
        var frames = system.Video.CompletedFrameCount;
        Assert.True(system.RunForTCycles(3 * 70_224).ExecutedTCycles >= 3 * 70_224);
        Assert.True(system.Video.CompletedFrameCount >= frames + 2);
        var later = system.GetDebugSnapshot();
        Assert.Equal((locked.AF, locked.BC, locked.DE, locked.HL, locked.SP, locked.PC), (later.AF, later.BC, later.DE, later.HL, later.SP, later.PC));
        Assert.True(system.IsFaulted);

        system.Reset();
        Assert.False(system.IsFaulted);
        Assert.Null(system.Fault);
        Assert.Equal(0UL, system.TotalTCycles);
        Assert.Equal(16, system.StepInstruction().ExecutedTCycles);
    }

    [Fact]
    public void ALockedCpuTakesNoInterrupt()
    {
        // Enables VBlank interrupts and locks the CPU; the request must stay pending.
        var system = TestRom.Start(0x3E, 0x01, 0xE0, 0xFF, 0xFB, 0xD3);
        for (var i = 0; i < 4; i++)
        {
            system.StepInstruction();
        }

        Assert.True(system.IsFaulted);
        var locked = system.GetDebugSnapshot();
        system.RunForTCycles(2 * 70_224);
        var later = system.GetDebugSnapshot();
        Assert.Equal(locked.PC, later.PC);
        Assert.Equal(locked.SP, later.SP);
        Assert.True(later.InterruptMasterEnable);
        Assert.Equal(1, later.InterruptFlags & 1);
    }

    [Fact]
    public void ResetRestoresRegistersAndInternalRamButPreservesCartridgeRam()
    {
        var cartridge = new RamCartridge();
        var system = new GameBoySystem();
        system.InsertCartridge(cartridge);
        var initial = system.GetDebugSnapshot();
        system.RunForTCycles(72); // Entry, LD A, three stores.
        Assert.Equal(0x42, cartridge.RamValue);
        var memory = new byte[1];
        system.CopyMemory(0xC000, memory);
        Assert.Equal(0x42, memory[0]);
        system.CopyMemory(0xFF80, memory);
        Assert.Equal(0x42, memory[0]);

        system.Reset();

        Assert.True(system.IsRomLoaded);
        Assert.Equal(initial, system.GetDebugSnapshot());
        Assert.Equal(2, cartridge.ResetCount); // Insert and explicit Reset.
        Assert.Equal(0x42, cartridge.RamValue);
        system.CopyMemory(0xC000, memory);
        Assert.Equal(0, memory[0]);
        system.CopyMemory(0xFF80, memory);
        Assert.Equal(0, memory[0]);
        Assert.Equal(16, system.StepInstruction().ExecutedTCycles);
    }

    [Fact]
    public void InsertingAnotherCartridgeResetsAndSwitchesProgram()
    {
        var system = TestRom.Start(0x3E, 0x42, 0xEA, 0x00, 0xC0);
        system.RunForTCycles(24);
        system.InsertCartridge(CartridgeLoader.Load(TestRom.Create(0x3E, 0x99)).Cartridge);
        Assert.Equal(0UL, system.TotalTCycles);
        Assert.Equal(0x100, system.GetDebugSnapshot().PC);
        var memory = new byte[1];
        system.CopyMemory(0xC000, memory);
        Assert.Equal(0, memory[0]);
        system.RunForTCycles(24);
        Assert.Equal(0x99, system.GetDebugSnapshot().A);
    }

    [Fact]
    public void BadLoadAndNullInsertLeaveRunningCartridgeUntouched()
    {
        var system = TestRom.Start(">B"u8.ToArray());
        var before = system.GetDebugSnapshot();
        Assert.Throws<ArgumentNullException>(() => system.InsertCartridge(null!));
        Assert.Throws<CartridgeLoadException>(() =>
            system.InsertCartridge(CartridgeLoader.Load([]).Cartridge));
        Assert.Equal(before, system.GetDebugSnapshot());
        system.StepInstruction();
        Assert.Equal(0x42, system.GetDebugSnapshot().A);
    }

    [Fact]
    public void DebugCopiesRemainIndependentOfLaterExecution()
    {
        var system = TestRom.Start(0x3E, 0x42, 0xEA, 0x00, 0xC0);
        var before = system.GetDebugSnapshot();
        var memory = new byte[1];
        system.CopyMemory(0xC000, memory);
        Assert.Equal(before, system.GetDebugSnapshot());
        system.RunForTCycles(24);
        Assert.Equal(0x01, before.A);
        Assert.Equal(0x150, before.PC);
        Assert.Equal(16UL, before.TotalTCycles);
        Assert.Equal(0, memory[0]);
        system.CopyMemory(0xC000, memory);
        Assert.Equal(0x42, memory[0]);
        memory[0] = 0x99;
        system.CopyMemory(0xC000, memory);
        Assert.Equal(0x42, memory[0]);
    }

    [Fact]
    public void MemoryCopyRejectsOverflowBeforeWritingAnyDestinationByte()
    {
        var system = new GameBoySystem();
        byte[] destination = [0x12, 0x34];
        Assert.Throws<ArgumentOutOfRangeException>(() => system.CopyMemory(0xFFFF, destination));
        Assert.Equal(new byte[] { 0x12, 0x34 }, destination);
        system.CopyMemory(0xFFFF, destination.AsSpan(0, 1));
        // ReSharper disable once UseUtf8StringLiteral
        Assert.Equal(new byte[] { 0x00, 0x34 }, destination); // IE resets to 0.
        system.CopyMemory(0xFFFF, []);
        Assert.Equal(0UL, system.TotalTCycles);
    }

    private sealed class RamCartridge : ICartridge
    {
        private readonly byte[] rom = TestRom.Create(
            0x3E, 0x42, 0xEA, 0x00, 0xC0, 0xEA, 0x80, 0xFF, 0xEA, 0x00, 0xA0);
        internal byte RamValue { get; private set; }
        internal int ResetCount { get; private set; }
        public byte Read(ushort address) => address < 0x8000 ? rom[address] : RamValue;

        public void Write(ushort address, byte value)
        {
            if (address == 0xA000)
            {
                RamValue = value;
            }
        }

        public void ResetController() => ResetCount++;
    }
}
