namespace GonFox.GameBoy.Core.Devices;

// CH3: plays 32 four-bit samples from Wave RAM, one every (2048 - period) * 2 T-cycles.
internal sealed class WaveChannel
{
    private const int TriggerDelay = 6; // Extra T-cycles before the first read.
    private static ReadOnlySpan<byte> Shifts => [4, 0, 1, 2]; // NR32 levels: mute, 100%, 50%, 25%.

    internal byte[] Ram { get; } = new byte[16]; // FF30-FF3F, upper nibble first.
    private readonly LengthCounter length = new(256);
    private int shift = 4;
    private int period;
    private byte sample;

    internal sealed record State(bool Enabled, bool DacOn, int Shift, int Period, int Timer, int Position, byte Sample, ulong ReadAt,
        LengthCounter.State Length);

    internal State CaptureState() => new(Enabled, DacOn, shift, period, Timer, Position, sample, ReadAt, length.CaptureState());

    internal static bool IsValid(State? s) => s?.Shift is 0 or 1 or 2 or 4 && s.Period is >= 0 and <= 0x7FF &&
        s.Position is >= 0 and <= 31 && LengthCounter.IsValid(s.Length, 256) &&
        (!s.Enabled || (s.DacOn && s.Timer is >= 1 and <= 4096 + TriggerDelay));

    internal void RestoreState(State s)
    {
        (Enabled, DacOn, shift, period, Timer, Position, sample, ReadAt) =
            (s.Enabled, s.DacOn, s.Shift, s.Period, s.Timer, s.Position, s.Sample, s.ReadAt);
        length.RestoreState(s.Length);
    }

    internal bool Enabled { get; private set; }
    internal bool DacOn { get; private set; }
    internal bool Muted => shift == 4;
    internal int Timer { get; private set; }
    internal int Position { get; private set; }
    internal ulong ReadAt { get; private set; } = ulong.MaxValue;
    internal int Output => Enabled ? (((Position & 1) == 0 ? sample >> 4 : sample & 0xF) >> shift) : 0;
    private int Reload => (2048 - period) * 2;

    // Advances at most Timer cycles, to T-cycle end; returns true at a read.
    internal bool Advance(int cycles, ulong end)
    {
        if (!Enabled || (Timer -= cycles) != 0)
        {
            return false;
        }

        Read(end);
        return true;
    }

    // Advances any number of cycles at once, to T-cycle end.
    internal void Elapse(ulong cycles, ulong end)
    {
        if (!Enabled)
        {
            return;
        }

        if (cycles < (ulong)Timer)
        {
            Timer -= (int)cycles;
            return;
        }
        ulong reload = (ulong)Reload, after = cycles - (ulong)Timer;
        Position = (int)((ulong)Position + (after / reload)) & 31; // Read() adds the last one.
        Read(end - (after % reload));
        Timer = (int)(reload - (after % reload));
    }

    private void Read(ulong at)
    {
        Timer = Reload;
        Position = (Position + 1) & 31;
        sample = Ram[Position >> 1];
        ReadAt = at;
    }

    internal void WriteDac(byte value)
    {
        DacOn = (value & 0x80) != 0;
        if (!DacOn)
        {
            Enabled = false;
        }
    }

    internal void WriteLength(byte value) => length.Load(value);
    internal void WriteLevel(byte value) => shift = Shifts[(value >> 5) & 3];
    internal void WritePeriodLow(byte value) => period = (period & 0x700) | value;

    internal void WriteControl(byte value, bool lengthNext)
    {
        period = (period & 0xFF) | ((value & 7) << 8);
        var trigger = (value & 0x80) != 0;
        if (!length.Write((value & 0x40) != 0, trigger, lengthNext))
        {
            Enabled = false;
        }

        if (!trigger)
        {
            return;
        }

        if (Enabled && Timer == 2)
        {
            // Retriggering as a sample is read corrupts the start of Wave RAM (DMG).
            var offset = ((Position + 1) >> 1) & 0xF;
            if (offset < 4)
            {
                Ram[0] = Ram[offset];
            }
            else
            {
                Ram.AsSpan(offset & ~3, 4).CopyTo(Ram);
            }

            sample = Ram[0];
        }
        Position = 0; // The sample byte is kept.
        if (DacOn)
        {
            Enabled = true;
        }

        Timer = Reload + TriggerDelay;
    }

    internal void ClockLength()
    {
        if (!length.Clock())
        {
            Enabled = false;
        }
    }

    internal void PowerOff()
    {
        Enabled = DacOn = false;
        (shift, period, Timer, Position, sample, ReadAt) = (4, 0, 0, 0, 0, ulong.MaxValue);
        length.PowerOff();
    }

    internal void Boot(byte nr30, byte nr32)
    {
        PowerOff();
        WriteDac(nr30);
        WriteLevel(nr32);
    }
}
