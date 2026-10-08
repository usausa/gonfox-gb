namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cpu;
using GonFox.GameBoy.Core.Devices;

// What STOP does with a held selected button and a pending interrupt, and how it wakes.
[Trait("Category", "Unit")]
public sealed class StopTests
{
    private const ushort Stop = 0x15E;
    private const ushort Padding = 0x15F;
    private const ushort Next = 0x160;

    // Sets P1, IE and IF, optionally holds A, and runs up to the STOP.
    private static GameBoySystem AtStop(byte p1, byte ie, byte requested, byte padding, bool held, bool ei = false)
    {
        var system = TestRom.Start(0x3E, p1, 0xE0, 0x00, 0x3E, ie, 0xE0, 0xFF, 0x3E, requested, 0xE0, 0x0F,
            0xAF, ei ? (byte)0xFB : (byte)0x00, 0x10, padding, 0x04, 0x18, 0xFE);
        if (held)
        {
            system.Joypad.SetButtonState(JoypadButton.A, true);
        }

        for (var i = 0; i < 8; i++)
        {
            system.StepInstruction();
        }

        Assert.Equal(Stop, system.GetDebugSnapshot().PC);
        return system;
    }

    // Checks STOP's length, time, DIV and mode, then whether the padding byte runs.
    [Theory]
    [InlineData(true, true, 4, Padding, "nop")]
    [InlineData(true, false, 8, Next, "halt")] // Padding byte read.
    [InlineData(false, true, 4, Padding, "stop")]
    [InlineData(false, false, 4, Next, "stop")] // Padding byte skipped.
    public void TheHeldSelectedButtonAndThePendingInterruptDecideWhatStopDoes(bool held, bool pending, int cycles, ushort pc, string mode)
    {
        var system = AtStop(0x10, 0x01, pending ? (byte)0x01 : (byte)0x00, 0x3C, held);
        var divider = system.GetDebugSnapshot().DividerCounter;
        Assert.Equal(new RunResult(cycles, mode == "stop"), system.StepInstruction());
        var after = system.GetDebugSnapshot();
        Assert.Equal((pc, mode == "halt", mode == "stop"), (after.PC, after.IsHalted, after.IsStopped));
        Assert.Equal(mode == "stop" ? 0 : (ushort)(divider + cycles), after.DividerCounter); // Reset only by STOP mode.
        Assert.Equal(mode == "halt", system.CaptureState().Cpu.JustHalted); // An ordinary HALT.
        if (mode == "stop")
        {
            system.Joypad.SetButtonState(JoypadButton.A, true);
        }

        while (system.IsHalted)
        {
            system.StepInstruction();
        }

        system.StepInstruction();
        Assert.Equal(pc == Padding ? (1, 0) : (0, 1), (system.GetDebugSnapshot().A, system.GetDebugSnapshot().B));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void AnUndefinedPaddingByteLocksTheCpuOnlyWhereStopTakesOneByte(bool held, bool pending, bool locks)
    {
        var system = AtStop(0x10, 0x01, pending ? (byte)0x01 : (byte)0x00, 0xD3, held);
        system.StepInstruction();
        if (system.IsStopped)
        {
            system.Joypad.SetButtonState(JoypadButton.A, true);
        }

        while (system.IsHalted)
        {
            system.StepInstruction();
        }

        system.StepInstruction();
        Assert.Equal(locks ? new CpuFault(Padding, 0xD3) : null, system.Fault);
        if (!locks)
        {
            Assert.Equal((Next + 1, 1), (system.GetDebugSnapshot().PC, system.GetDebugSnapshot().B)); // INC B ran.
        }
    }

    // The VBlank is taken at once in the NOP form and after the wake in STOP mode.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithImeTheInterruptIsTakenBeforeThePaddingByte(bool held)
    {
        var system = AtStop(0x10, 0x01, 0x01, 0x3C, held, ei: true);
        Assert.Equal(new RunResult(4, !held), system.StepInstruction());
        Assert.True(system.GetDebugSnapshot().InterruptMasterEnable);
        if (!held)
        {
            system.Joypad.SetButtonState(JoypadButton.A, true);
        }

        Assert.Equal(new RunResult(20, false), system.StepInstruction());
        var state = system.GetDebugSnapshot();
        Assert.Equal((0x40, 0xFFFC, 0), (state.PC, state.SP, state.A));
        var stack = new byte[2];
        system.CopyMemory(0xFFFC, stack);
        Assert.Equal(new byte[] { 0x5F, 0x01 }, stack);
    }

    // Buttons of the unselected row neither prevent STOP mode nor end it.
    [Theory]
    [InlineData(0x10, JoypadButton.Right, JoypadButton.A)]
    [InlineData(0x20, JoypadButton.A, JoypadButton.Right)]
    public void OnlyAFallingLineOfASelectedRowEndsStopMode(byte p1, JoypadButton other, JoypadButton selected)
    {
        var system = AtStop(p1, 0x00, 0x00, 0x00, held: false);
        system.Joypad.SetButtonState(other, true);
        Assert.Equal(new RunResult(4, true), system.StepInstruction());
        Assert.Equal(0, system.GetDebugSnapshot().DividerCounter);
        for (var i = 1; i < 4; i++)
        {
            system.Joypad.SetButtonState(other + i, true); // The rest of that row.
        }

        system.Joypad.SetButtonState(other, false);
        Assert.Equal(new RunResult(0, true), system.RunForTCycles(1000));
        system.Joypad.SetButtonState(selected, true);
        Assert.Equal(new RunResult(4, false), system.RunForTCycles(1)); // Wakes at once.
        Assert.Equal((Next + 1, 1), (system.GetDebugSnapshot().PC, system.GetDebugSnapshot().B));
    }

    [Fact]
    public void WithNoRowSelectedOnlyResetEndsStopMode()
    {
        var system = AtStop(0x30, 0x00, 0x00, 0x00, held: true); // A is held but not selected.
        Assert.Equal(new RunResult(4, true), system.StepInstruction());
        var stopped = system.GetDebugSnapshot();
        for (var i = 0; i < 8; i++)
        {
            system.Joypad.SetButtonState((JoypadButton)i, true);
            system.Joypad.SetButtonState((JoypadButton)i, false);
            Assert.Equal(new RunResult(0, true), system.RunForTCycles(10_000));
        }
        system.Joypad.SynchronizeButtons(0xFF);
        Assert.Equal(new RunResult(0, true), system.StepInstruction());
        Assert.Equal(stopped, system.GetDebugSnapshot());
        system.Reset();
        Assert.False(system.IsStopped);
        Assert.Equal(new RunResult(16, false), system.StepInstruction()); // The entry JP again.
    }

    // The fall is latched from the STOP on; one from before the STOP does not count.
    [Fact]
    public void AFallSinceTheStopEndsItEvenIfReleasedBeforeTheNextRun()
    {
        var system = AtStop(0x10, 0x00, 0x00, 0x00, held: false);
        system.Joypad.SetButtonState(JoypadButton.B, true);
        system.Joypad.SetButtonState(JoypadButton.B, false);
        Assert.Equal(new RunResult(4, true), system.StepInstruction());
        Assert.Equal(new RunResult(0, true), system.RunForTCycles(1));
        system.Joypad.SetButtonState(JoypadButton.B, true);
        system.Joypad.SetButtonState(JoypadButton.B, false);
        Assert.Equal(new RunResult(4, false), system.RunForTCycles(1));
        Assert.Equal(1, system.GetDebugSnapshot().B);
    }

    [Fact]
    public void AStateRestoredInStopModeWakesWhenASelectedLineIsLow()
    {
        var system = AtStop(0x10, 0x00, 0x00, 0x00, held: false);
        system.StepInstruction();
        var stopped = system.CaptureState();
        Assert.True(stopped.Cpu.Stopped);
        Assert.False(stopped.Joypad.StopWakeRequested);
        foreach (var button in new[] { JoypadButton.Right, JoypadButton.A })
        {
            system.RestoreState(stopped with { Joypad = stopped.Joypad with { Pressed = (byte)(1 << (int)button) } });
            var selected = button == JoypadButton.A;
            Assert.Equal(new RunResult(selected ? 4 : 0, !selected), system.StepInstruction());
        }
        system.RestoreState(stopped);
        Assert.Equal(new RunResult(0, true), system.StepInstruction());
    }
}
