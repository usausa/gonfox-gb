namespace GonFox.GameBoy.Core.Devices;

internal sealed class Timer(Interrupts interrupts)
{
    internal ushort DividerCounter { get; private set; }
    internal byte Tima { get; private set; }
    internal byte Tma { get; private set; }
    private byte tac;
    private const int DivApuBit = 0x1000; // DIV bit 4: frame sequencer.
    private const int SerialClockBit = 0x80;
    private ushort signalMask;
    private int edgeMask;
    private int reloadWindow;
    private Apu? apu;
    private Serial? serial;

    internal sealed record State(ushort Divider, byte Tima, byte Tma, byte Tac, int ReloadDelay, int ReloadWindow);

    internal State CaptureState() => new(DividerCounter, Tima, Tma, tac, ReloadDelay, reloadWindow);

    internal static void ValidateState(State? state) => StateValidation.Require(state is not null && state.Tac <= 7 &&
        state.ReloadDelay is >= 0 and <= 4 && state.ReloadWindow is >= 0 and <= 4 &&
        (state.ReloadDelay == 0 || state.ReloadWindow == 0), "Timer");

    internal void RestoreState(State state)
    {
        DividerCounter = state.Divider;
        Tima = state.Tima;
        Tma = state.Tma;
        SetTac(state.Tac);
        ReloadDelay = state.ReloadDelay;
        reloadWindow = state.ReloadWindow;
    }

    internal void ConnectApu(Apu target)
    {
        apu = target;
        SetTac(tac);
    }

    internal void ConnectSerial(Serial target)
    {
        serial = target;
        SetTac(tac);
    }

    internal byte Tac => (byte)(tac | 0xF8);
    internal int ReloadDelay { get; private set; }
    private bool Signal => (DividerCounter & signalMask) != 0;

    private void SetTac(byte value)
    {
        tac = value;
        signalMask = (value & 4) == 0 ? (ushort)0 : (ushort)(1 << (value & 3) switch { 0 => 9, 1 => 3, 2 => 5, _ => 7 });
        edgeMask = signalMask | (apu is null ? 0 : DivApuBit) | (serial is null ? 0 : SerialClockBit);
    }

    internal void Tick()
    {
        if ((reloadWindow | ReloadDelay) != 0)
        {
            TickReload();
        }

        int previous = DividerCounter, next = previous + 1;
        DividerCounter = unchecked((ushort)next);
        var fell = previous & ~next & edgeMask;
        if (fell != 0)
        {
            DividerFell(fell, true);
        }
    }

    // T-cycles before the next listened falling edge; none while a TIMA reload is due.
    internal int QuietTCycles
    {
        get
        {
            if ((reloadWindow | ReloadDelay) != 0)
            {
                return 0;
            }

            if (edgeMask == 0)
            {
                return int.MaxValue;
            }

            var span = (edgeMask & -edgeMask) << 1; // Period of the lowest listened bit.
            return span - (DividerCounter & (span - 1)) - 1;
        }
    }

    internal void Skip(int tCycles) => DividerCounter = unchecked((ushort)(DividerCounter + tCycles));

    // Dispatches a DIV falling edge from counting, an FF04 write or the STOP reset.
    private void DividerFell(int fell, bool duringTick)
    {
        if ((fell & signalMask) != 0)
        {
            Increment();
        }

        if ((fell & SerialClockBit) != 0)
        {
            serial?.ClockEdge();
        }

        if ((fell & DivApuBit) != 0)
        {
            apu?.ClockFrameSequencer(duringTick);
        }
    }

    // Counts down the TIMA reload delay and the window after it.
    private void TickReload()
    {
        if (reloadWindow > 0)
        {
            reloadWindow--;
        }

        if (ReloadDelay > 0 && --ReloadDelay == 0)
        {
            Tima = Tma;
            interrupts.Request(InterruptSource.Timer);
            reloadWindow = 4;
        }
    }

    internal byte ReadRegister(ushort address) => address switch
    {
        0xFF04 => (byte)(DividerCounter >> 8),
        0xFF05 => Tima,
        0xFF06 => Tma,
        0xFF07 => Tac,
        _ => throw new ArgumentOutOfRangeException(nameof(address))
    };

    internal void WriteRegister(ushort address, byte value)
    {
        switch (address)
        {
            case 0xFF04: ResetDivider(); break;
            case 0xFF05:
                if (reloadWindow > 0)
                {
                    return;
                }

                Tima = value;
                ReloadDelay = 0; // Cancels the reload and its IRQ.
                break;
            case 0xFF06:
                Tma = value;
                if (reloadWindow > 0)
                {
                    Tima = value;
                }

                break;
            case 0xFF07:
                var previous = Signal;
                SetTac((byte)(value & 7));
                if (previous && !Signal)
                {
                    Increment();
                }

                break;
            default: throw new ArgumentOutOfRangeException(nameof(address));
        }
    }

    internal void ResetDivider()
    {
        var fell = DividerCounter & edgeMask; // Every set bit falls to 0.
        DividerCounter = 0;
        if (fell != 0)
        {
            DividerFell(fell, false);
        }
    }

    private void Increment()
    {
        if (ReloadDelay > 0 || reloadWindow > 0)
        {
            return;
        }

        Tima = unchecked((byte)(Tima + 1));
        if (Tima == 0)
        {
            ReloadDelay = 4;
        }
    }

    // Before a boot ROM: the divider starts from 0.
    internal void PowerOn() => DividerCounter = 0;

    internal void Reset()
    {
        DividerCounter = DmgBootProfile.DividerCounter;
        Tima = Tma = 0;
        SetTac(0);
        ReloadDelay = reloadWindow = 0;
    }
}
