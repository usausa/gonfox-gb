namespace GonFox.GameBoy.Core;

[Trait("Category", "Unit")]
public sealed class CpuStateAndBitTests
{
    [Fact]
    public void EiEnablesAfterTheFollowingInstructionAndDiCancelsIt()
    {
        var m = new CpuTestMachine([0xFB, 0x00, 0xF3]);
        Assert.Equal(4, m.Cpu.StepInstruction());
        Assert.False(m.State.InterruptMasterEnable);
        Assert.True(m.State.InterruptEnablePending);
        Assert.Equal(4, m.Cpu.StepInstruction());
        Assert.True(m.State.InterruptMasterEnable);
        Assert.False(m.State.InterruptEnablePending);
        Assert.Equal(4, m.Cpu.StepInstruction());
        Assert.False(m.State.InterruptMasterEnable);

        m.Reset([0xFB, 0xF3, 0x00]);
        m.Cpu.StepInstruction();
        m.Cpu.StepInstruction();
        m.Cpu.StepInstruction();
        Assert.False(m.State.InterruptMasterEnable);
        Assert.False(m.State.InterruptEnablePending);
    }

    [Fact]
    public void RepeatedEiDoesNotPostponeTheFirstEnable()
    {
        var m = new CpuTestMachine([0xFB, 0xFB, 0xF3, 0x00]);
        m.Cpu.StepInstruction();
        m.Cpu.StepInstruction();
        Assert.True(m.State.InterruptMasterEnable);
        Assert.False(m.State.InterruptEnablePending);
        m.Cpu.StepInstruction();
        m.Cpu.StepInstruction();
        Assert.False(m.State.InterruptMasterEnable);
    }

    [Fact]
    public void RetiPopsPcAndImmediatelyEnablesInterrupts()
    {
        var m = new CpuTestMachine([0xD9]);
        m.Bus.WriteByte(0xD000, 0x34);
        m.Bus.WriteByte(0xD001, 0x12);
        Assert.Equal(16, m.Cpu.StepInstruction());
        Assert.Equal(0x1234, m.State.PC);
        Assert.Equal(0xD002, m.State.SP);
        Assert.True(m.State.InterruptMasterEnable);
        Assert.False(m.State.InterruptEnablePending);
    }

    [Fact]
    public void HaltWaitsInFourCycleUnitsWithoutFetchingAnotherInstruction()
    {
        var system = TestRom.Start(0xFB, 0x76, 0x3E, 0x42);
        system.StepInstruction();
        Assert.Equal(new RunResult(4, false), system.StepInstruction());
        Assert.True(system.IsHalted);
        var halted = system.GetDebugSnapshot();
        Assert.True(halted.InterruptMasterEnable);
        Assert.False(halted.InterruptEnablePending);
        Assert.Equal(new RunResult(12, false), system.RunForTCycles(9));
        Assert.Equal(halted with
        {
            TotalTCycles = halted.TotalTCycles + 12,
            DividerCounter = (ushort)(halted.DividerCounter + 12),
            PpuDot = 436
        }, system.GetDebugSnapshot()); // Line 153 (post-boot phase).
        system.Reset();
        Assert.False(system.IsHalted);
        Assert.False(system.GetDebugSnapshot().InterruptMasterEnable);
    }

    [Fact]
    public void StopConsumesPaddingAndReturnsEvenForAnUnfulfilledBudget()
    {
        var system = TestRom.Start(0x10, 0, 0x3E, 0x42);
        Assert.Equal(new RunResult(4, true), system.RunForTCycles(100));
        Assert.True(system.IsStopped);
        Assert.False(system.IsHalted);
        var stopped = system.GetDebugSnapshot();
        Assert.Equal(0x152, stopped.PC);
        Assert.True(stopped.IsStopped);
        Assert.Equal(new RunResult(0, true), system.StepInstruction());
        Assert.Equal(new RunResult(0, true), system.RunForTCycles(int.MaxValue));
        Assert.Equal(new RunResult(0, true), system.RunForTCycles(0));
        Assert.Equal(stopped, system.GetDebugSnapshot());
        system.Reset();
        Assert.False(system.IsStopped);
        Assert.False(system.GetDebugSnapshot().IsStopped);
    }

    [Theory]
    [InlineData(0x07, 0x8000, 0x0110)]
    [InlineData(0x0F, 0x0100, 0x8010)]
    [InlineData(0x17, 0x8000, 0x0010)] // Z clear even for zero.
    [InlineData(0x17, 0x0010, 0x0100)]
    [InlineData(0x1F, 0x0100, 0x0010)]
    [InlineData(0x1F, 0x0010, 0x8000)]
    [InlineData(0x2F, 0xAA90, 0x55F0)]
    [InlineData(0x37, 0x42E0, 0x4290)]
    [InlineData(0x3F, 0x42F0, 0x4280)]
    [InlineData(0x3F, 0x4260, 0x4210)]
    public void AccumulatorAndFlagOperationsRespectPreservedBits(byte opcode, ushort af, ushort expected)
    {
        var m = new CpuTestMachine([opcode], af);
        Assert.Equal(4, m.Cpu.StepInstruction());
        Assert.Equal(expected, m.State.AF);
    }

    [Theory]
    [InlineData(0x07, 0x0000, 0x0080)]
    [InlineData(0x0F, 0x0000, 0x0080)]
    [InlineData(0x17, 0x8000, 0x0090)]
    [InlineData(0x1F, 0x0100, 0x0090)]
    [InlineData(0x27, 0x8000, 0x0090)]
    [InlineData(0x2F, 0x8000, 0xC000)] // SRA keeps the sign bit.
    [InlineData(0x37, 0x0010, 0x0080)]
    [InlineData(0x3F, 0x0100, 0x0090)]
    public void CbZeroAndCarryBoundariesDifferFromAccumulatorRotations(byte opcode, ushort af, ushort expected)
    {
        var m = new CpuTestMachine([0xCB, opcode], af);
        Assert.Equal(8, m.Cpu.StepInstruction());
        Assert.Equal(expected, m.State.AF);
    }

    [Fact]
    public void BitOnMemoryDoesNotWriteButRotateHasASeparateWriteCycle()
    {
        var m = new CpuTestMachine([0xCB, 0x7E, 0xCB, 0x06], af: 0, hl: 0xA100);
        m.Bus.WriteByte(0xA100, 0x81);
        m.Accesses.Clear();
        Assert.Equal(12, m.Cpu.StepInstruction());
        Assert.Equal(0x20, m.State.F);
        Assert.DoesNotContain(m.Accesses, access => access.Write);
        Assert.Equal((false, (ushort)0xA100, (byte)0x81, 8UL), m.Accesses.Last());
        Assert.Equal(16, m.Cpu.StepInstruction());
        Assert.Equal((true, (ushort)0xA100, (byte)0x03, 24UL), Assert.Single(m.Accesses, access => access.Write));
    }
}
