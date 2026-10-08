namespace GonFox.GameBoy.Core.Devices;

// CH4: a 15-bit LFSR (7-bit in short mode) clocked every divisor << shift T-cycles.
internal sealed class NoiseChannel
{
    private static ReadOnlySpan<byte> Divisors => [8, 16, 32, 48, 64, 80, 96, 112]; // NR43 bits 0-2, in T-cycles.

    private readonly LengthCounter length = new(64);
    private readonly VolumeEnvelope envelope = new();
    private byte control; // NR43 as written.
    private int lfsr;

    internal sealed record State(bool Enabled, byte Control, int Lfsr, int Timer, LengthCounter.State Length, VolumeEnvelope.State Envelope);

    internal State CaptureState() => new(Enabled, control, lfsr, Timer, length.CaptureState(), envelope.CaptureState());

    internal static bool IsValid(State? s) => s?.Lfsr is >= 0 and <= 0x7FFF && s.Timer is >= 0 and <= 112 << 15 &&
        LengthCounter.IsValid(s.Length, 64) && VolumeEnvelope.IsValid(s.Envelope) &&
        (!s.Enabled || ((s.Envelope.Register & 0xF8) != 0 && s.Timer >= 1));

    internal void RestoreState(State s)
    {
        (Enabled, control, lfsr, Timer) = (s.Enabled, s.Control, s.Lfsr, s.Timer);
        length.RestoreState(s.Length);
        envelope.RestoreState(s.Envelope);
    }

    internal bool Enabled { get; private set; }
    internal bool DacOn => envelope.DacOn;
    internal int Volume => envelope.Volume;
    internal bool Clocked => (control >> 4) < 14; // Shifts 14 and 15 stop the LFSR.
    internal int Timer { get; private set; }
    internal int Output => Enabled && (lfsr & 1) != 0 ? envelope.Volume : 0;
    private int Reload => Divisors[control & 7] << (control >> 4);

    // Advances at most Timer cycles; returns true at an LFSR clock.
    internal bool Advance(int cycles)
    {
        if (!Enabled || !Clocked || (Timer -= cycles) != 0)
        {
            return false;
        }

        Timer = Reload;
        Shift();
        return true;
    }

    // Advances any number of cycles at once, one LFSR clock at a time.
    internal void Elapse(ulong cycles)
    {
        if (!Enabled || !Clocked)
        {
            return;
        }

        while (cycles >= (ulong)Timer)
        {
            cycles -= (ulong)Timer;
            Timer = Reload;
            Shift();
        }
        Timer -= (int)cycles;
    }

    private void Shift()
    {
        var bit = ~(lfsr ^ (lfsr >> 1)) & 1;
        var mask = (control & 8) != 0 ? 0x4040 : 0x4000; // Short mode also feeds bit 6.
        lfsr = bit != 0 ? (lfsr >> 1) | mask : (lfsr >> 1) & ~mask;
    }

    internal void WriteLength(byte value) => length.Load(value & 0x3F);

    internal void WriteEnvelope(byte value)
    {
        if (!envelope.Write(value, Enabled))
        {
            Enabled = false; // DAC off.
        }
    }

    internal void WriteControl(byte value) => control = value; // Takes effect from the next reload.

    internal void WriteTrigger(byte value, bool lengthNext, bool envelopeNext)
    {
        var trigger = (value & 0x80) != 0;
        if (!length.Write((value & 0x40) != 0, trigger, lengthNext))
        {
            Enabled = false;
        }

        if (!trigger)
        {
            return;
        }

        if (envelope.DacOn)
        {
            Enabled = true;
        }

        lfsr = 0;
        Timer = Reload;
        envelope.Trigger(envelopeNext);
    }

    internal void ClockLength()
    {
        if (!length.Clock())
        {
            Enabled = false;
        }
    }

    internal void ClockEnvelope() => envelope.Clock();

    internal void PowerOff()
    {
        Enabled = false;
        (control, lfsr, Timer) = (0, 0, 0);
        length.PowerOff();
        envelope.PowerOff();
    }
}
