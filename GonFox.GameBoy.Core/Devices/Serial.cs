namespace GonFox.GameBoy.Core.Devices;

// Serial port with an internal 8192 Hz clock toggled by falls of DIV counter bit 7.
public sealed class Serial
{
    private readonly Interrupts interrupts;
    private readonly Queue<byte> completed = new();
    private byte control;
    private byte sent;
    private int bits;
    private bool clock;
    public long DroppedByteCount { get; private set; }

    internal sealed record State(byte Data, byte Control, byte Sent, bool Clock, int Bits, byte[] Completed, long Dropped);

    internal State CaptureState() => new(Data, control, sent, clock, bits, completed.ToArray(), DroppedByteCount);

    internal static void ValidateState(State? state) => StateValidation.Require(state is not null &&
        (state.Control & ~0x81) == 0 && state.Bits is >= 0 and < 8 && state.Completed is { Length: <= 256 } && state.Dropped >= 0, "Serial");

    internal void RestoreState(State state)
    {
        Data = state.Data;
        control = state.Control;
        sent = state.Sent;
        clock = state.Clock;
        bits = state.Bits;
        completed.Clear();
        foreach (var value in state.Completed)
        {
            completed.Enqueue(value);
        }

        DroppedByteCount = state.Dropped;
    }

    internal Serial(Interrupts interrupts) => this.interrupts = interrupts;
    internal byte Data { get; private set; }
    internal byte Control => (byte)(control | 0x7E); // DMG has no fast-clock bit.
    public bool TryReadTransmittedByte(out byte value) => completed.TryDequeue(out value);

    internal void WriteData(byte value) => Data = value;

    internal void WriteControl(byte value)
    {
        bits = 0;
        sent = 0;
        if (clock)
        {
            ClockEdge(); // Takes a high clock low.
        }

        control = (byte)(value & 0x81);
    }

    // Toggles the clock on a fall of DIV counter bit 7, shifting a bit when it goes low.
    internal void ClockEdge()
    {
        clock = !clock;
        if (clock || (control & 0x81) != 0x81)
        {
            return;
        }

        sent = (byte)((sent << 1) | (Data >> 7));
        Data = (byte)((Data << 1) | 1); // Disconnected cable receives ones.
        if (++bits != 8)
        {
            return;
        }

        bits = 0;
        control &= 1;
        interrupts.Request(InterruptSource.Serial);
        if (completed.Count == 256)
        {
            completed.Dequeue();
            DroppedByteCount++;
        }
        completed.Enqueue(sent);
    }

    internal void Reset()
    {
        Data = control = sent = 0;
        bits = 0;

        // The clock has toggled once per 256 T since power-on: bit 8 of the DIV counter.
        clock = (DmgBootProfile.DividerCounter & 0x100) != 0;
        completed.Clear();
        DroppedByteCount = 0;
    }

    internal void PowerOn() => clock = false; // DIV counts from 0.
}
