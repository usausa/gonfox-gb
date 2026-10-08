namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Devices;

[Trait("Category", "Unit")]
public sealed class JoypadTests
{
    [Theory]
    [InlineData(JoypadButton.Right, 0x20, 0xEE)]
    [InlineData(JoypadButton.Left, 0x20, 0xED)]
    [InlineData(JoypadButton.Up, 0x20, 0xEB)]
    [InlineData(JoypadButton.Down, 0x20, 0xE7)]
    [InlineData(JoypadButton.A, 0x10, 0xDE)]
    [InlineData(JoypadButton.B, 0x10, 0xDD)]
    [InlineData(JoypadButton.Select, 0x10, 0xDB)]
    [InlineData(JoypadButton.Start, 0x10, 0xD7)]
    public void ButtonsAreActiveLowAndRequestIrqOnPressOnly(JoypadButton button, byte selection, byte expected)
    {
        var m = new PeripheralTestMachine();
        m.Bus.WriteByte(0xFF00, selection);
        m.Joypad.SetButtonState(button, true);
        Assert.Equal(expected, m.Bus.ReadByte(0xFF00));
        Assert.Equal(0xF0, m.Interrupts.Flags);
        m.Interrupts.WriteFlags(0);
        m.Joypad.SetButtonState(button, true);
        m.Joypad.SetButtonState(button, false);
        Assert.Equal(0xE0, m.Interrupts.Flags);
        Assert.Equal(selection | 0xCF, m.Bus.ReadByte(0xFF00));
    }

    [Fact]
    public void SelectionEdgesAndCombinedRowsMatchTheButtonMatrix()
    {
        var m = new PeripheralTestMachine();
        m.Joypad.WriteRegister(0x30); // No row selected.
        m.Joypad.SetButtonState(JoypadButton.Right, true);
        m.Joypad.SetButtonState(JoypadButton.B, true);
        Assert.Equal(0xFF, m.Joypad.ReadRegister());
        Assert.Equal(0xE0, m.Interrupts.Flags);
        m.Joypad.WriteRegister(0); // Both rows: Right + B.
        Assert.Equal(0xCC, m.Joypad.ReadRegister());
        Assert.Equal(0xF0, m.Interrupts.Flags);
        m.Interrupts.WriteFlags(0);
        m.Joypad.WriteRegister(0xCF); // Low/unused bits are not writable.
        Assert.Equal(0xCC, m.Joypad.ReadRegister());
        Assert.Equal(0xE0, m.Interrupts.Flags);
        m.Joypad.ReleaseAll();
        Assert.Equal(0xCF, m.Joypad.ReadRegister());
        Assert.Equal(0xE0, m.Interrupts.Flags);
    }

    [Fact]
    public void StopIgnoresEarlierAndUnselectedPressesAndFreezesClockUntilExecutionResumes()
    {
        var system = TestRom.Start(0x3E, 0x10, 0xE0, 0x00, 0x10, 0, 0x3E, 0x42); // Buttons row.
        system.StepInstruction();
        system.StepInstruction();
        system.Joypad.SetButtonState(JoypadButton.A, true); // Before STOP: no wake.
        system.Joypad.SetButtonState(JoypadButton.A, false);
        system.Joypad.SetButtonState(JoypadButton.Right, true); // Unselected row.
        Assert.Equal(new RunResult(4, true), system.StepInstruction());
        Assert.Equal(0, system.GetDebugSnapshot().DividerCounter);
        var stopped = system.GetDebugSnapshot();
        system.Joypad.SetButtonState(JoypadButton.Up, true);
        Assert.Equal(new RunResult(0, true), system.RunForTCycles(100));
        Assert.Equal(stopped, system.GetDebugSnapshot());
        system.Joypad.SetButtonState(JoypadButton.A, true);
        Assert.Equal(new RunResult(8, false), system.RunForTCycles(1));
        Assert.Equal(0x42, system.GetDebugSnapshot().A);
        Assert.Equal(8, system.GetDebugSnapshot().DividerCounter);
    }

    [Fact]
    public void StopAfterEiWakesOnPressAndThenServicesTheJoypadInterrupt()
    {
        // The press wakes STOP at once and its joypad interrupt is serviced next.
        var system = TestRom.Start(0x3E, 0x10, 0xE0, 0x00, 0x3E, 0x10, 0xE0, 0xFF,
            0xAF, 0xE0, 0x0F, 0xFB, 0x10, 0x00, 0x3C);
        for (var i = 0; i < 8; i++)
        {
            system.StepInstruction();
        }

        Assert.True(system.IsStopped);
        Assert.True(system.GetDebugSnapshot().InterruptMasterEnable);
        Assert.Equal(new RunResult(0, true), system.StepInstruction());
        system.Joypad.SetButtonState(JoypadButton.A, true);
        Assert.Equal(new RunResult(20, false), system.StepInstruction());
        var state = system.GetDebugSnapshot();
        Assert.Equal((0x60, 0xFFFC, 20), (state.PC, state.SP, state.DividerCounter));
        var stack = new byte[2];
        system.CopyMemory(0xFFFC, stack);
        Assert.Equal(new byte[] { 0x5E, 0x01 }, stack); // After STOP's padding.
    }

    [Fact]
    public void SynchronizingExternalInputClearsOldWakeAndTreatsHeldButtonsAsNewPresses()
    {
        var m = new PeripheralTestMachine();
        m.Joypad.WriteRegister(0x10);
        m.Joypad.SetButtonState(JoypadButton.A, true);
        m.Interrupts.WriteFlags(0);
        m.Joypad.SynchronizeButtons(0);
        Assert.False(m.Joypad.ConsumeStopWake());
        Assert.Equal(0xDF, m.Joypad.ReadRegister());
        Assert.Equal(0xE0, m.Interrupts.Flags);
        m.Joypad.SynchronizeButtons(1 << (int)JoypadButton.A);
        Assert.True(m.Joypad.ConsumeStopWake());
        Assert.Equal(0xDE, m.Joypad.ReadRegister());
        Assert.Equal(0xF0, m.Interrupts.Flags);
    }

    [Fact]
    public void InvalidButtonDoesNotChangeState()
    {
        var m = new PeripheralTestMachine();
        Assert.Throws<ArgumentOutOfRangeException>(() => m.Joypad.SetButtonState((JoypadButton)8, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => m.Joypad.SetButtonState((JoypadButton)(-1), true));
        Assert.Equal(0xCF, m.Joypad.ReadRegister()); // Both rows, nothing pressed.
    }
}
