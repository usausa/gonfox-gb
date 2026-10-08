namespace GonFox.GameBoy.Core.Devices;

public enum JoypadButton
{
    Right,
    Left,
    Up,
    Down,
    A,
    B,
    Select,
    Start
}

public sealed class Joypad
{
    private readonly Interrupts interrupts;
    private byte pressedButtons;
    private byte selection;
    private bool stopWakeRequested;

    internal sealed record State(byte Pressed, byte Selection, bool StopWakeRequested);

    internal State CaptureState() => new(pressedButtons, selection, stopWakeRequested);
    internal static void ValidateState(State? state) => StateValidation.Require(state is not null && (state.Selection & ~0x30) == 0, "Joypad");

    internal void RestoreState(State state)
    {
        pressedButtons = state.Pressed;
        selection = state.Selection;
        stopWakeRequested = state.StopWakeRequested;
    }

    internal Joypad(Interrupts interrupts) => this.interrupts = interrupts;

    internal byte ReadRegister()
    {
        var lines = 0x0F;
        if ((selection & 0x10) == 0)
        {
            lines &= ~pressedButtons;
        }

        if ((selection & 0x20) == 0)
        {
            lines &= ~(pressedButtons >> 4);
        }

        return (byte)(0xC0 | selection | (lines & 0x0F));
    }

    // Whether a held button in a selected row pulls a P10-P13 line low.
    internal bool AnyLineLow => (ReadRegister() & 0x0F) != 0x0F;

    internal void WriteRegister(byte value)
    {
        var previous = ReadRegister();
        selection = (byte)(value & 0x30);
        RequestOnFallingEdge(previous);
    }

    public void SetButtonState(JoypadButton button, bool pressed)
    {
        if ((uint)button > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(button));
        }

        var mask = (byte)(1 << (int)button);
        var previous = ReadRegister();
        if (pressed)
        {
            pressedButtons |= mask;
        }
        else
        {
            pressedButtons &= (byte)~mask;
        }

        RequestOnFallingEdge(previous);
    }

    public void ReleaseAll() => pressedButtons = 0;

    // Sets the held buttons after a restore, each as a fresh press.
    public void SynchronizeButtons(byte pressed)
    {
        pressedButtons = 0;
        stopWakeRequested = false;
        for (var i = 0; i < 8; i++)
        {
            if ((pressed & (1 << i)) != 0)
            {
                SetButtonState((JoypadButton)i, true);
            }
        }
    }

    // Requests the joypad interrupt and a STOP wake-up when a selected line falls.
    private void RequestOnFallingEdge(byte previous)
    {
        if ((previous & ~ReadRegister() & 0x0F) == 0)
        {
            return;
        }

        interrupts.Request(InterruptSource.Joypad);
        stopWakeRequested = true;
    }

    internal void ClearStopWake() => stopWakeRequested = false;

    // Consumes the latched fall, or a line low now, as a STOP wake-up.
    internal bool ConsumeStopWake()
    {
        var wake = stopWakeRequested || AnyLineLow;
        stopWakeRequested = false;
        return wake;
    }

    internal void Reset()
    {
        pressedButtons = 0;
        selection = DmgBootProfile.JoypadSelection;
        stopWakeRequested = false;
    }
}
