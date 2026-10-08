namespace GonFox.GameBoy.Core.Cartridge;

using System.Diagnostics;

// Cartridge hardware that counts emulated time from the console clock attached to it.
internal interface IClockedCartridge
{
    void AttachClock(Clock clock);
    void DetachClock();
}

// Current and latched clock registers, T-cycles into the second, and whether the latch is armed.
internal sealed record RtcState(RtcRegisters Current, RtcRegisters Latched, int SubSecond, bool LatchArmed);

// The MBC3 real-time clock, brought up to date from the console clock whenever it is accessed.
internal sealed class Mbc3Rtc
{
    private const int SecondsPerDay = 86_400;
    private byte seconds;
    private byte minutes;
    private byte hours;
    private int days; // 9 bits
    private bool halted;
    private bool carry;
    private bool latchArmed;
    private RtcRegisters latched;
    private int subSecond; // T-cycles into the second.
    private Clock? attachedClock;
    private ulong countedTo; // Last clock count applied.

    private RtcRegisters Current => new(seconds, minutes, hours, (byte)days,
        (byte)((days >> 8) | (halted ? 0x40 : 0) | (carry ? 0x80 : 0)));

    internal void AttachClock(Clock clock)
    {
        Update();
        attachedClock = clock;
        countedTo = clock.TotalTCycles;
    }

    internal void DetachClock()
    {
        Update();
        attachedClock = null;
    }

    // Reads a latched register: 0 = seconds (08) to 4 = DH (0C).
    internal byte Read(int register) => register switch
    {
        0 => latched.Seconds,
        1 => latched.Minutes,
        2 => latched.Hours,
        3 => latched.DayLow,
        _ => latched.DayHigh
    };

    internal void Write(int register, byte value)
    {
        Update(); // Counts time with the old values.
        switch (register)
        {
            case 0: seconds = (byte)(value & 63); subSecond = 0; break;
            case 1: minutes = (byte)(value & 63); break;
            case 2: hours = (byte)(value & 31); break;
            case 3: days = (days & 0x100) | value; break;
            default: SetDayHigh(value); break;
        }
    }

    internal void WriteLatch(byte value)
    {
        if (latchArmed && value == 1)
        {
            Update();
            latched = Current;
        }
        latchArmed = value == 0;
    }

    internal RtcSnapshot Export()
    {
        Update();
        return new(Current, latched);
    }

    internal void Import(RtcSnapshot clock)
    {
        Update();
        SetCurrent(clock.Current);
        latched = Mask(clock.Latched);
        subSecond = 0;
    }

    internal void Advance(long elapsedSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elapsedSeconds);
        Update();
        if (!halted)
        {
            AdvanceSeconds(elapsedSeconds);
        }
    }

    internal RtcState CaptureState()
    {
        Update();
        return new(Current, latched, subSecond, latchArmed);
    }

    internal static bool IsValid(RtcState? state) => state is not null && Mask(state.Current) == state.Current &&
        Mask(state.Latched) == state.Latched && state.SubSecond is >= 0 and < Clock.CyclesPerSecond;

    // Restores the state while the clock is detached.
    internal void RestoreState(RtcState state)
    {
        Debug.Assert(attachedClock is null, "Detach the cartridge clock before restoring a state.");
        SetCurrent(state.Current);
        latched = state.Latched;
        subSecond = state.SubSecond;
        latchArmed = state.LatchArmed;
    }

    private static RtcRegisters Mask(RtcRegisters value) => new((byte)(value.Seconds & 63), (byte)(value.Minutes & 63),
        (byte)(value.Hours & 31), value.DayLow, (byte)(value.DayHigh & 0xC1));

    private void SetCurrent(RtcRegisters value)
    {
        seconds = (byte)(value.Seconds & 63);
        minutes = (byte)(value.Minutes & 63);
        hours = (byte)(value.Hours & 31);
        days = value.DayLow;
        SetDayHigh(value.DayHigh);
    }

    private void SetDayHigh(byte value)
    {
        days = (days & 0xFF) | ((value & 1) << 8);
        halted = (value & 0x40) != 0;
        carry = (value & 0x80) != 0;
    }

    private void Update()
    {
        if (attachedClock is null)
        {
            return;
        }

        var now = attachedClock.TotalTCycles;
        Debug.Assert(now >= countedTo, "The console clock jumped back without detaching the cartridge clock.");
        if (!halted)
        {
            var total = (ulong)subSecond + (now - countedTo);
            if (total >= Clock.CyclesPerSecond)
            {
                AdvanceSeconds((long)(total / Clock.CyclesPerSecond));
            }

            subSecond = (int)(total % Clock.CyclesPerSecond);
        }
        countedTo = now;
    }

    // Counts one second while the time of day is out of range, which never carries into the days.
    private void Tick()
    {
        seconds = (byte)((seconds + 1) & 63);
        if (seconds != 60)
        {
            return;
        }

        seconds = 0;
        minutes = (byte)((minutes + 1) & 63);
        if (minutes != 60)
        {
            return;
        }

        minutes = 0;
        hours = (byte)((hours + 1) & 31);
    }

    // Adds seconds: single ticks until the time of day is in range, then the rest by arithmetic.
    private void AdvanceSeconds(long count)
    {
        for (; count > 0 && (seconds >= 60 || minutes >= 60 || hours >= 24); count--)
        {
            Tick();
        }

        if (count == 0)
        {
            return;
        }

        var time = (hours * 3600L) + (minutes * 60) + seconds + (count % SecondsPerDay);
        var totalDays = days + (count / SecondsPerDay) + (time / SecondsPerDay);
        time %= SecondsPerDay;
        hours = (byte)(time / 3600);
        minutes = (byte)((time / 60) % 60);
        seconds = (byte)(time % 60);
        if (totalDays >= 512)
        {
            carry = true;
        }

        days = (int)(totalDays % 512);
    }
}
