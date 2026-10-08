namespace GonFox.GameBoy.Core.Devices;

// A pulse channel: CH1 with its period sweep, or CH2 without it.
internal sealed class PulseChannel(bool hasSweep)
{
    // Duty waveforms: bit n is the output of step n.
    private static ReadOnlySpan<byte> Duties => [0x80, 0x81, 0xE1, 0x7E];

    private readonly LengthCounter length = new(64);
    private readonly VolumeEnvelope envelope = new();
    private bool high;
    private bool sweepEnabled;
    private bool negated;
    private byte sweep; // NR10 as written.
    private int duty;
    private int period;
    private int position;
    private int shadow;
    private int sweepTimer;

    internal sealed record State(bool Enabled, bool High, int Duty, int Period, int Timer, int Position,
        LengthCounter.State Length, VolumeEnvelope.State Envelope, byte Sweep, int Shadow, int SweepTimer, bool SweepEnabled, bool Negated);

    internal State CaptureState() => new(Enabled, high, duty, period, Timer, position,
        length.CaptureState(), envelope.CaptureState(), sweep, shadow, sweepTimer, sweepEnabled, negated);

    internal static bool IsValid(State? s, bool hasSweep) => s?.Duty is >= 0 and <= 3 && s.Period is >= 0 and <= 0x7FF &&
        s.Position is >= 0 and <= 7 && LengthCounter.IsValid(s.Length, 64) && VolumeEnvelope.IsValid(s.Envelope) &&
        s.Shadow is >= 0 and <= 0x7FF && s.SweepTimer is >= 0 and <= 9 &&
        (!s.Enabled || ((s.Envelope.Register & 0xF8) != 0 && s.Timer is >= 1 and <= 8192 + StartDelay)) &&
        (hasSweep || (s.Sweep == 0 && s.Shadow == 0 && s.SweepTimer == 0 && !s.SweepEnabled && !s.Negated));

    internal void RestoreState(State s)
    {
        (Enabled, high, duty, period, Timer, position) = (s.Enabled, s.High, s.Duty, s.Period, s.Timer, s.Position);
        length.RestoreState(s.Length);
        envelope.RestoreState(s.Envelope);
        (sweep, shadow, sweepTimer, sweepEnabled, negated) = (s.Sweep, s.Shadow, s.SweepTimer, s.SweepEnabled, s.Negated);
    }

    // Extra T-cycles before a trigger's first duty step, from off or from a running channel.
    private const int StartDelay = 8;
    private const int RestartDelay = 4;

    internal bool Enabled { get; private set; }
    internal bool DacOn => envelope.DacOn;
    internal int Volume => envelope.Volume;
    internal int Timer { get; private set; }

    // Digital level 0..15; a duty change shows from the next duty step.
    internal int Output => Enabled && high ? envelope.Volume : 0;
    private int Reload => (2048 - period) * 4; // 1,048,576 Hz divider, in T-cycles.
    private bool DutyBit => ((Duties[duty] >> position) & 1) != 0;

    // Advances at most Timer cycles; returns true at a duty step.
    internal bool Advance(int cycles)
    {
        if (!Enabled || (Timer -= cycles) != 0)
        {
            return false;
        }

        Timer = Reload; // Period writes apply from here.
        position = (position + 1) & 7;
        high = DutyBit;
        return true;
    }

    // Advances any number of cycles at once.
    internal void Elapse(ulong cycles)
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
        Timer = (int)(reload - (after % reload));
        position = (int)((ulong)position + 1 + (after / reload)) & 7;
        high = DutyBit;
    }

    internal void WriteSweep(byte value)
    {
        // Leaving subtraction mode after a subtracting calculation disables CH1.
        var leavesNegate = (sweep & 8) != 0 && (value & 8) == 0;
        sweep = value;
        if (leavesNegate && negated)
        {
            Enabled = false;
        }
    }

    internal void WriteLength(byte value, bool powered)
    {
        length.Load(value & 0x3F);
        if (powered)
        {
            duty = value >> 6;
        }
    }

    internal void WriteEnvelope(byte value)
    {
        if (!envelope.Write(value, Enabled))
        {
            Enabled = false; // DAC off.
        }
    }

    internal void WritePeriodLow(byte value) => period = (period & 0x700) | value;

    // Applies an NRx4 write; sweepStepNow: this M-cycle's sweep step does not count for the new timer.
    internal void WriteControl(byte value, bool lengthNext, bool envelopeNext, bool sweepStepNow = false)
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

        var running = Enabled;
        if (envelope.DacOn)
        {
            if (!running)
            {
                high = false; // Silent until the first step.
            }

            Enabled = true;
        }
        Timer = Reload + (running ? RestartDelay : StartDelay);
        envelope.Trigger(envelopeNext);
        if (!hasSweep)
        {
            return;
        }

        shadow = period;
        var pace = (sweep >> 4) & 7;
        sweepTimer = (pace == 0 ? 8 : pace) + (sweepStepNow ? 1 : 0);
        sweepEnabled = pace != 0 || (sweep & 7) != 0;
        negated = false;
        if ((sweep & 7) != 0 && Sweep() > 0x7FF)
        {
            Enabled = false;
        }
    }

    internal void ClockLength()
    {
        if (!length.Clock())
        {
            Enabled = false;
        }
    }

    internal void ClockEnvelope() => envelope.Clock();

    internal void ClockSweep()
    {
        if (!Enabled || !sweepEnabled || --sweepTimer != 0)
        {
            return;
        }

        var pace = (sweep >> 4) & 7;
        if (pace == 0)
        {
            sweepTimer = 8;
            return;
        }
        sweepTimer = pace;
        var swept = Sweep();
        if (swept > 0x7FF)
        {
            Enabled = false;
            return;
        }
        if ((sweep & 7) == 0)
        {
            return;
        }

        shadow = period = swept;
        if (Sweep() > 0x7FF)
        {
            Enabled = false; // Second check, not written back.
        }
    }

    private int Sweep()
    {
        var delta = shadow >> (sweep & 7);
        if ((sweep & 8) == 0)
        {
            return shadow + delta;
        }

        negated = true;
        return shadow - delta;
    }

    internal void PowerOff()
    {
        Enabled = high = sweepEnabled = negated = false;
        sweep = 0;
        duty = period = Timer = position = shadow = sweepTimer = 0;
        length.PowerOff();
        envelope.PowerOff();
    }

    // BootBypass: the channel left on by the boot sound, already silent, at the given duty step.
    internal void Boot(byte initialSweep, int initialDuty, byte initialEnvelope, int initialPeriod, int initialPosition, int timer)
    {
        PowerOff();
        (sweep, duty, period, position, Timer) = (initialSweep, initialDuty, initialPeriod, initialPosition, timer);
        length.Boot(64);
        envelope.Boot(initialEnvelope);
        Enabled = true;
        high = DutyBit;
    }
}
