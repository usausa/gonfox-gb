namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Devices;

[Trait("Category", "Unit")]
public sealed class InterruptTests
{
    [Fact]
    public void IfMasksUnusedBitsWhileIeKeepsThem()
    {
        var m = new PeripheralTestMachine();
        m.Bus.WriteByte(0xFFFF, 0xE0);
        m.Bus.WriteByte(0xFF0F, 0xFF);
        Assert.Equal(0xE0, m.Bus.ReadByte(0xFFFF));
        Assert.Equal(0xFF, m.Bus.ReadByte(0xFF0F));
        Assert.Equal(0, m.Interrupts.Pending);
        m.Bus.WriteByte(0xFFFF, 0xFF);
        Assert.Equal(0x1F, m.Interrupts.Pending);
    }

    [Theory]
    [InlineData(0x1F, 0x40, 0x1E)]
    [InlineData(0x1E, 0x48, 0x1C)]
    [InlineData(0x1C, 0x50, 0x18)]
    [InlineData(0x18, 0x58, 0x10)]
    [InlineData(0x10, 0x60, 0)]
    public void HighestPriorityInterruptConsumes20CyclesAndPushesPc(byte pending, ushort vector, byte remaining)
    {
        var m = new PeripheralTestMachine(0xFB, 0x00);
        m.Interrupts.WriteEnable(0x1F);
        m.Interrupts.WriteFlags(pending);
        m.Cpu.StepInstruction();
        Assert.False(m.Cpu.GetDebugSnapshot().InterruptMasterEnable);
        m.Cpu.StepInstruction();
        Assert.Equal(20, m.Cpu.StepInstruction());
        var state = m.Cpu.GetDebugSnapshot();
        Assert.Equal(vector, state.PC);
        Assert.False(state.InterruptMasterEnable);
        Assert.Equal(0xFFFC, state.SP);
        Assert.Equal(0x52, m.Bus.ReadByte(0xFFFC));
        Assert.Equal(0x01, m.Bus.ReadByte(0xFFFD));
        Assert.Equal(remaining, m.Interrupts.Pending);
        Assert.Equal(28, m.Timer.DividerCounter);
    }

    [Fact]
    public void PendingInterruptCannotEscapeEiThenDi()
    {
        var m = new PeripheralTestMachine(0xFB, 0xF3, 0x00);
        m.Interrupts.WriteEnable(1);
        m.Interrupts.WriteFlags(1);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(4, m.Cpu.StepInstruction());
        }

        Assert.Equal(0x153, m.Cpu.GetDebugSnapshot().PC);
        Assert.Equal(1, m.Interrupts.Pending);
    }

    [Fact]
    public void HaltBugSuppressesExactlyOneFetchIncrement()
    {
        var m = new PeripheralTestMachine("v>B"u8.ToArray());
        m.Interrupts.WriteEnable(1);
        m.Interrupts.WriteFlags(1);
        m.Cpu.StepInstruction();
        Assert.False(m.Cpu.IsHalted);
        Assert.Equal(8, m.Cpu.StepInstruction());
        Assert.Equal(0x3E, m.Cpu.GetDebugSnapshot().A);
        Assert.Equal(0x152, m.Cpu.GetDebugSnapshot().PC);
        Assert.Equal(4, m.Cpu.StepInstruction());
        Assert.Equal(0x153, m.Cpu.GetDebugSnapshot().PC);
    }

    [Fact]
    public void HaltWithImeOffWakesWithoutServicingInterrupt()
    {
        var m = new PeripheralTestMachine("v>B"u8.ToArray());
        m.Interrupts.WriteEnable(4);
        m.Cpu.StepInstruction();
        Assert.True(m.Cpu.IsHalted);
        m.Interrupts.Request(InterruptSource.Timer);
        Assert.Equal(4, m.Cpu.StepInstruction()); // HALT samples the request.
        Assert.False(m.Cpu.IsHalted);
        Assert.Equal(8, m.Cpu.StepInstruction());
        Assert.Equal(0x42, m.Cpu.GetDebugSnapshot().A);
        Assert.Equal(4, m.Interrupts.Pending);
    }

    [Fact]
    public void TimerRunsThroughHaltAndInterruptEntry()
    {
        var m = new PeripheralTestMachine(0xFB, 0x00, 0x76, 0x3E, 0x99);
        m.Interrupts.WriteEnable(4);
        m.Timer.WriteRegister(0xFF05, 0xFF);
        m.Timer.WriteRegister(0xFF06, 0x42);
        m.Timer.WriteRegister(0xFF07, 5);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(4, m.Cpu.StepInstruction());
        }

        Assert.True(m.Cpu.IsHalted); // Request came after the sample.
        Assert.Equal(4, m.Interrupts.Pending);
        Assert.Equal(4, m.Cpu.StepInstruction());
        Assert.False(m.Cpu.IsHalted);
        Assert.Equal(20, m.Cpu.StepInstruction());
        Assert.Equal(0x50, m.Cpu.GetDebugSnapshot().PC);
        Assert.Equal(44, m.Timer.DividerCounter);
        Assert.Equal(0x43, m.Timer.Tima);
    }

    [Fact]
    public void EiHaltWithPendingInterruptDoesNotDuplicateIsrFetch()
    {
        var m = new PeripheralTestMachine(0xFB, 0x76);
        m.Interrupts.WriteEnable(4);
        m.Interrupts.WriteFlags(4);
        m.Cpu.StepInstruction();
        m.Cpu.StepInstruction();
        Assert.Equal(20, m.Cpu.StepInstruction());
        Assert.Equal(4, m.Cpu.StepInstruction()); // NOP at vector 0050.
        Assert.Equal(0x51, m.Cpu.GetDebugSnapshot().PC);
    }

    [Fact]
    public void EiHaltWithPendingInterruptReturnsToTheHalt()
    {
        // The handler returns to the HALT, which then waits again.
        var m = new PeripheralTestMachine(0xFB, 0x76, 0x3C);
        m.Interrupts.WriteEnable(4);
        m.Interrupts.WriteFlags(4);
        m.Cpu.StepInstruction();
        m.Cpu.StepInstruction();
        Assert.False(m.Cpu.IsHalted);
        Assert.Equal(20, m.Cpu.StepInstruction());
        var state = m.Cpu.GetDebugSnapshot();
        Assert.Equal((0x50, 0xFFFC), (state.PC, state.SP));
        Assert.Equal((0x51, 0x01), (m.Bus.ReadByte(0xFFFC), m.Bus.ReadByte(0xFFFD)));
        Assert.Equal(0, m.Interrupts.Pending);
    }

    // IE is sampled after the upper PC byte (02) is pushed.
    [Theory]
    [InlineData(0x0000, 0x04, 0x04, 0x0000, 0x04, 0x02)] // Timer cancelled.
    [InlineData(0x0001, 0x08, 0x08, 0x0058, 0x00, 0x05)] // Low byte too late.
    [InlineData(0x0000, 0x03, 0x03, 0x0048, 0x01, 0x02)] // STAT instead of VBlank.
    public void PushedPcCanRewriteIeBeforeTheVectorIsChosen(ushort sp, byte enable, byte flags, ushort vector,
        byte remaining, byte enabledAfter)
    {
        var program = new byte[0xB5];
        new byte[] { 0xC3, 0x00, 0x02 }.CopyTo(program, 0);
        new byte[] { 0x31, (byte)sp, (byte)(sp >> 8), 0xFB, 0x00 }.CopyTo(program, 0xB0);
        var m = new PeripheralTestMachine(program);
        m.Interrupts.WriteEnable(enable);
        m.Interrupts.WriteFlags(flags);
        for (var i = 0; i < 4; i++)
        {
            m.Cpu.StepInstruction();
        }

        Assert.Equal(20, m.Cpu.StepInstruction()); // Cancellation keeps the full dispatch length.
        var state = m.Cpu.GetDebugSnapshot();
        Assert.Equal(vector, state.PC);
        Assert.False(state.InterruptMasterEnable);
        Assert.Equal(remaining, m.Interrupts.Flags & 0x1F);
        Assert.Equal(unchecked((ushort)(sp - 2)), state.SP);
        Assert.Equal(enabledAfter, m.Interrupts.Enable);
    }

    [Fact]
    public void VblankWakesHaltWithoutAnExtraMachineCycle()
    {
        // VBlank is raised early in its M-cycle, so HALT adds no delay.
        var system = TestRom.Start(0x3E, 0x01, 0xE0, 0xFF, 0xAF, 0xE0, 0x0F, 0xFB, 0x76);
        while (system.GetDebugSnapshot().PC != 0x40)
        {
            system.StepInstruction();
        }

        Assert.Equal((456UL * 144) + 60 + 20, system.TotalTCycles); // Line 144 dot 4 + dispatch.
    }
}
