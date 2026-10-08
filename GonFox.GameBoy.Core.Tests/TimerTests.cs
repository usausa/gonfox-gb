namespace GonFox.GameBoy.Core;

[Trait("Category", "Unit")]
public sealed class TimerTests
{
    [Theory]
    [InlineData(4, 1024)]
    [InlineData(5, 16)]
    [InlineData(6, 64)]
    [InlineData(7, 256)]
    public void SelectedDividerFallingEdgesDriveTima(byte control, int period)
    {
        var m = new PeripheralTestMachine();
        m.Timer.WriteRegister(0xFF07, control);
        m.TickTimer(period - 1);
        Assert.Equal(0, m.Timer.Tima);
        m.TickTimer(1);
        Assert.Equal(1, m.Timer.Tima);
        m.TickTimer(period);
        Assert.Equal(2, m.Timer.Tima);
        Assert.Equal((byte)(0xF8 | control), m.Timer.Tac);
    }

    [Fact]
    public void DividerRunsWhileTimerDisabledAndWraps()
    {
        var m = new PeripheralTestMachine();
        m.TickTimer(256);
        Assert.Equal(1, m.Bus.ReadByte(0xFF04));
        Assert.Equal(0, m.Timer.Tima);
        m.TickTimer(65536 - 256);
        Assert.Equal(0, m.Timer.DividerCounter);
        m.TickTimer(9);
        m.Timer.WriteRegister(0xFF04, 0xFF);
        Assert.Equal(0, m.Timer.DividerCounter);
    }

    [Theory]
    [InlineData(0xFF04, 0)] // Divider reset.
    [InlineData(0xFF07, 0)] // Disable with the bit high.
    [InlineData(0xFF07, 4)] // Select a low bit.
    public void WritesGenerateFallingEdgesWithoutResettingTimerPhase(ushort address, byte value)
    {
        var m = new PeripheralTestMachine();
        m.Timer.WriteRegister(0xFF07, 5);
        m.TickTimer(8);
        m.Timer.WriteRegister(address, value);
        Assert.Equal(1, m.Timer.Tima);
        Assert.Equal(address == 0xFF04 ? 0 : 8, m.Timer.DividerCounter);
        m.Timer.WriteRegister(address, value);
        Assert.Equal(1, m.Timer.Tima);
    }

    [Fact]
    public void OverflowWaitsFourTCyclesThenReloadsAndRequestsInterrupt()
    {
        var m = Overflow();
        Assert.Equal(0, m.Timer.Tima);
        Assert.Equal(4, m.Timer.ReloadDelay);
        Assert.Equal(0xE0, m.Interrupts.Flags);
        m.TickTimer(3);
        Assert.Equal(0, m.Timer.Tima);
        Assert.Equal(0xE0, m.Interrupts.Flags);
        m.TickTimer(1);
        Assert.Equal(0x42, m.Timer.Tima);
        Assert.Equal(0xE4, m.Interrupts.Flags);
        m.TickTimer(12);
        Assert.Equal(0x43, m.Timer.Tima); // Next edge is still at 32, not 36.
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TimaWriteInDelayCancelsReloadAndIrq(int offset)
    {
        var m = Overflow();
        m.TickTimer(offset);
        m.Timer.WriteRegister(0xFF05, 0x99);
        m.TickTimer(4 - offset);
        Assert.Equal(0x99, m.Timer.Tima);
        Assert.Equal(0xE0, m.Interrupts.Flags);
    }

    [Fact]
    public void ReloadCycleIgnoresTimaWritesAndCopiesTmaWrites()
    {
        var m = Overflow();
        m.TickTimer(4);
        for (var i = 0; i < 4; i++)
        {
            m.Timer.WriteRegister(0xFF05, 0xFF);
            Assert.NotEqual(0xFF, m.Timer.Tima);
            m.Timer.WriteRegister(0xFF06, (byte)(0x50 + i));
            Assert.Equal(0x50 + i, m.Timer.Tima);
            m.TickTimer(1);
        }
        m.Timer.WriteRegister(0xFF05, 0x99);
        m.Timer.WriteRegister(0xFF06, 0x88);
        Assert.Equal(0x99, m.Timer.Tima);
        Assert.Equal(0x88, m.Timer.Tma);
    }

    [Fact]
    public void TmaDuringDelayChangesReloadButDivAndTacDoNotCancelIt()
    {
        var m = Overflow();
        m.Timer.WriteRegister(0xFF06, 0x77);
        m.Timer.WriteRegister(0xFF04, 0);
        m.Timer.WriteRegister(0xFF07, 0);
        m.TickTimer(4);
        Assert.Equal(0x77, m.Timer.Tima);
        Assert.Equal(0xE4, m.Interrupts.Flags);
    }

    [Fact]
    public void CpuIfWritePrecedesReloadInTheSameMachineCycle()
    {
        var m = new PeripheralTestMachine(0xE0, 0x0F); // LDH (IF),A; boot A=1.
        m.Timer.WriteRegister(0xFF05, 0xFF);
        m.Timer.WriteRegister(0xFF06, 0x42);
        m.Timer.WriteRegister(0xFF07, 5);
        m.TickTimer(8);
        Assert.Equal(12, m.Cpu.StepInstruction());
        Assert.Equal(0x42, m.Timer.Tima);
        Assert.Equal(0xE5, m.Interrupts.Flags); // IF write, then reload ORs in Timer.
    }

    [Fact]
    public void CpuTimaWriteCanCancelReloadBeforeThatMachineCycleAdvances()
    {
        var m = new PeripheralTestMachine(0xE0, 0x05);
        m.Timer.WriteRegister(0xFF05, 0xFF);
        m.Timer.WriteRegister(0xFF07, 5);
        m.TickTimer(8);
        m.Cpu.StepInstruction();
        Assert.Equal(1, m.Timer.Tima);
        Assert.Equal(0xE0, m.Interrupts.Flags);
    }

    [Fact]
    public void WriteInducedOverflowRequestsIrqBeforeTheNextInstruction()
    {
        var m = new PeripheralTestMachine(0xE0, 0x07); // A=1 disables the timer.
        m.Timer.WriteRegister(0xFF05, 0xFF);
        m.Timer.WriteRegister(0xFF07, 5);
        m.Cpu.StepInstruction(); // TAC write at T=8, reload at T=12.
        Assert.Equal(12UL, m.Clock.TotalTCycles);
        Assert.Equal(0xE4, m.Interrupts.Flags);
    }

    private static PeripheralTestMachine Overflow()
    {
        var m = new PeripheralTestMachine();
        m.Timer.WriteRegister(0xFF05, 0xFF);
        m.Timer.WriteRegister(0xFF06, 0x42);
        m.Timer.WriteRegister(0xFF07, 5);
        m.TickTimer(16);
        return m;
    }
}
