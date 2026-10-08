namespace GonFox.GameBoy.Core.Devices;

internal enum InterruptSource
{
    VBlank,
    LcdStat,
    Timer,
    Serial,
    Joypad
}

internal sealed class Interrupts
{
    private byte requested;

    internal sealed record State(byte Requested, byte Enabled);

    internal State CaptureState() => new(requested, Enable);
    internal static void ValidateState(State? state) => StateValidation.Require(state is not null && state.Requested <= 31, "Interrupts");

    internal void RestoreState(State state)
    {
        requested = state.Requested;
        Enable = state.Enabled;
    }

    internal byte Flags => (byte)(requested | 0xE0);
    internal byte Enable { get; private set; }
    internal byte Pending => (byte)(requested & Enable & 0x1F);
    internal void WriteFlags(byte value) => requested = (byte)(value & 0x1F);
    internal void WriteEnable(byte value) => Enable = value;
    internal void Request(InterruptSource source) => requested |= (byte)(1 << (int)source);
    internal void Acknowledge(int bit) => requested &= (byte)~(1 << bit);

    internal void Reset()
    {
        requested = DmgBootProfile.InterruptFlags;
        Enable = DmgBootProfile.InterruptEnable;
    }

    internal void PowerOn() => requested = 0;
}
