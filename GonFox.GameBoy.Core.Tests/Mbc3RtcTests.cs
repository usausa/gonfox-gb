namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;

using static GonFox.GameBoy.Core.Mbc3Tests;

// MBC3 clock rules one at a time, with time counted in T-cycles of a bare clock.
[Trait("Category", "Unit")]
public sealed class Mbc3RtcTests
{
    private const int Second = GameBoySystem.CyclesPerSecond;

    private static (ICartridge Cart, Clock Clock) Attached(byte type = 0x10, byte ramCode = 3)
    {
        var cart = CartridgeLoader.Load(TestRom.CreateMbc3(type, 1, ramCode)).Cartridge;
        var clock = new Clock();
        ((IClockedCartridge)cart).AttachClock(clock);
        cart.Write(0, 0x0A);
        return (cart, clock);
    }

    // Jumps the clock ahead without any cartridge access.
    private static void Elapse(Clock clock, long tcycles) => clock.RestoreState(clock.TotalTCycles + (ulong)tcycles);

    [Fact]
    public void TicksOncePerSecondOfEmulatedTimeFromPowerOn()
    {
        var (cart, clock) = Attached();
        Assert.Equal(new RtcRegisters(0, 0, 0, 0, 0), Now(cart));
        clock.AdvanceTCycles(Second - 1);
        Assert.Equal(0, Now(cart).Seconds);
        clock.AdvanceTCycles(1);
        Assert.Equal(1, Now(cart).Seconds);
        Elapse(clock, 60L * Second);
        Assert.Equal(new RtcRegisters(1, 1, 0, 0, 0), Now(cart));
        Elapse(clock, 3600L * 24 * Second);
        Assert.Equal(new RtcRegisters(1, 1, 0, 1, 0), Now(cart));
    }

    // Registers set just before a tick and expected after it.
    [Theory]
    [InlineData(59, 10, 5, 7, 0x00, 0, 11, 5, 7, 0x00)] // Seconds carry.
    [InlineData(59, 59, 23, 255, 0x00, 0, 0, 0, 0, 0x01)] // Day 255 -> 256.
    [InlineData(59, 59, 23, 255, 0x01, 0, 0, 0, 0, 0x80)] // Day 511 -> 0 sets carry.
    [InlineData(59, 59, 23, 255, 0x81, 0, 0, 0, 0, 0x80)] // Carry is sticky.
    [InlineData(59, 59, 23, 3, 0x80, 0, 0, 0, 4, 0x80)] // Carry kept.
    [InlineData(60, 63, 28, 3, 0x00, 61, 63, 28, 3, 0x00)] // Invalid seconds: +1.
    [InlineData(63, 30, 4, 3, 0x00, 0, 30, 4, 3, 0x00)] // Seconds 63 -> 0, no carry.
    [InlineData(59, 63, 4, 3, 0x00, 0, 0, 4, 3, 0x00)] // Minutes 63 -> 0, no carry.
    [InlineData(59, 59, 31, 3, 0x01, 0, 0, 0, 3, 0x01)] // Hours 31 -> 0, no carry.
    [InlineData(59, 60, 4, 3, 0x00, 0, 61, 4, 3, 0x00)] // Minutes 60 -> 61.
    [InlineData(59, 59, 24, 3, 0x00, 0, 0, 25, 3, 0x00)] // Hours 24 -> 25.
    public void OneTickFollowsTheHardwareRules(byte s, byte m, byte h, byte dl, byte dh,
        byte s2, byte m2, byte h2, byte dl2, byte dh2)
    {
        var (cart, clock) = Attached();
        SetClock(cart, new(s, m, h, dl, dh)); // Starts a whole second.
        clock.AdvanceTCycles(Second - 1);
        Assert.Equal(new RtcRegisters(s, m, h, dl, dh), Now(cart));
        clock.AdvanceTCycles(1);
        Assert.Equal(new RtcRegisters(s2, m2, h2, dl2, dh2), Now(cart));
    }

    [Fact]
    public void RegistersKeepOnlyTheirBitsAndReadTheLatchedCopy()
    {
        var (cart, clock) = Attached();
        SetClock(cart, new(0xFF, 0xFF, 0xFF, 0xFF, 0xFF));
        Latch(cart);
        Assert.Equal(new RtcRegisters(0x3F, 0x3F, 0x1F, 0xFF, 0xC1), Latched(cart)); // Halted: nothing moves.
        SetClock(cart, new(0xC0, 0xC0, 0xE0, 0x00, 0x3E)); // Only invalid bits set, DH running.
        Elapse(clock, 5L * Second);
        Assert.Equal(new RtcRegisters(0x3F, 0x3F, 0x1F, 0xFF, 0xC1), Latched(cart)); // Old latch kept.
        Assert.Equal(new RtcRegisters(5, 0, 0, 0, 0), Now(cart));
    }

    [Fact]
    public void LatchNeedsAWriteOfZeroThenOne()
    {
        var (cart, clock) = Attached();
        cart.Write(0x6000, 1);
        Elapse(clock, 2L * Second);
        cart.Write(0x6000, 1);
        Assert.Equal(0, Latched(cart).Seconds); // 01 alone does not latch.
        cart.Write(0x6000, 0);
        cart.Write(0x6000, 2);
        cart.Write(0x6000, 1);
        Assert.Equal(0, Latched(cart).Seconds); // The write before 01 must be 00.
        cart.Write(0x7000, 0);
        cart.Write(0x6FFF, 1);
        Assert.Equal(2, Latched(cart).Seconds);
        Elapse(clock, Second);
        cart.Write(0x6000, 1);
        Assert.Equal(2, Latched(cart).Seconds);
        cart.Write(0x6000, 0);
        cart.Write(0x6000, 0);
        cart.Write(0x6000, 1);
        Assert.Equal(3, Latched(cart).Seconds);
    }

    [Fact]
    public void HaltStopsTheClockAndPausesThePartOfTheSecond()
    {
        var (cart, clock) = Attached();
        const int counted = Second * 6 / 10; // 600 ms into the second.
        clock.AdvanceTCycles(counted);
        SetRegister(cart, 4, 0x40);
        Elapse(clock, 30L * Second);
        Assert.Equal(new RtcRegisters(0, 0, 0, 0, 0x40), Now(cart));
        SetRegister(cart, 4, 0x00);
        clock.AdvanceTCycles(Second - counted - 1);
        Assert.Equal(0, Now(cart).Seconds);
        clock.AdvanceTCycles(1);
        Assert.Equal(1, Now(cart).Seconds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void OnlyWritingTheSecondsRestartsTheSecond(int register)
    {
        var (cart, clock) = Attached();
        clock.AdvanceTCycles(Second / 4);
        SetRegister(cart, register, 1); // The tick stays.
        clock.AdvanceTCycles(Second - (Second / 4) - 1);
        Assert.Equal(0, Now(cart).Seconds);
        clock.AdvanceTCycles(1);
        Assert.Equal(1, Now(cart).Seconds);
        clock.AdvanceTCycles(Second / 2);
        SetRegister(cart, 0, 10); // A whole second from here.
        clock.AdvanceTCycles(Second - 1);
        Assert.Equal(10, Now(cart).Seconds);
        clock.AdvanceTCycles(1);
        Assert.Equal(11, Now(cart).Seconds);
    }

    [Fact]
    public void AdvanceAddsWholeSecondsLikeTickingEachOne()
    {
        var random = new Random(21);
        var (cart, _) = Attached();
        var host = (IRealTimeClockCartridge)cart;
        for (var round = 0; round < 300; round++)
        {
            // Any register values, including out-of-range times; the clock stays running.
            var start = new RtcRegisters((byte)random.Next(64), (byte)random.Next(64), (byte)random.Next(32),
                (byte)random.Next(256), (byte)(random.Next(2) | (random.Next(2) << 7)));
            long count = round < 280 ? random.Next(20_000) : random.Next(3 * 86_400);
            host.ImportClock(new(start, default));
            host.AdvanceClock(count);
            Assert.Equal(TickReference(start, count), host.ExportClock().Current);
        }
    }

    [Fact]
    public void AdvanceHandlesYearsKeepsAHaltedClockAndRejectsNegativeTime()
    {
        var (cart, _) = Attached();
        var host = (IRealTimeClockCartridge)cart;
        host.AdvanceClock((10L * 365 * 86_400) + 3661); // 3650 days: carry set.
        Assert.Equal(new RtcRegisters(1, 1, 1, (3650 % 512) & 0xFF, 0x80), host.ExportClock().Current);
        host.ImportClock(new(new(5, 0, 0, 0, 0x40), default));
        host.AdvanceClock(1000);
        Assert.Equal(new RtcRegisters(5, 0, 0, 0, 0x40), host.ExportClock().Current);
        Assert.Throws<ArgumentOutOfRangeException>(() => host.AdvanceClock(-1));
        host.ImportClock(new(new(59, 59, 23, 0, 0), default));
        host.AdvanceClock(long.MaxValue); // No overflow.
        Assert.Equal(new RtcRegisters(6, 30, 15, 0x45, 0x81), host.ExportClock().Current);
    }

    [Fact]
    public void HostExportAndImportCarryBothRegisterSetsAndRestartTheSecond()
    {
        var (cart, clock) = Attached();
        var host = (IRealTimeClockCartridge)cart;
        SetClock(cart, new(10, 20, 3, 4, 1));
        Latch(cart);
        Elapse(clock, Second * 5 / 2);
        Assert.Equal(new RtcSnapshot(new(12, 20, 3, 4, 1), new(10, 20, 3, 4, 1)), host.ExportClock());
        var (copy, copyClock) = Attached();
        var target = (IRealTimeClockCartridge)copy;
        copyClock.AdvanceTCycles(Second / 2);
        target.ImportClock(new(new(0xFC, 0xFF, 0xE3, 4, 0xFF), new(12, 20, 3, 4, 1))); // Invalid bits are dropped.
        Assert.Equal(new RtcSnapshot(new(0x3C, 0x3F, 0x03, 4, 0xC1), new(12, 20, 3, 4, 1)), target.ExportClock());
        Assert.Equal(new RtcRegisters(12, 20, 3, 4, 1), Latched(copy));
        target.ImportClock(new(new(1, 0, 0, 0, 0), default));
        copyClock.AdvanceTCycles(Second - 1);
        Assert.Equal(1, target.ExportClock().Current.Seconds); // A new whole second.
        copyClock.AdvanceTCycles(1);
        Assert.Equal(2, target.ExportClock().Current.Seconds);
    }

    [Fact]
    public void ClockRunsOnThroughAConsoleReset()
    {
        var cart = CartridgeLoader.Load(TestRom.CreateMbc3(0x0F, 0, 0, 0xF3, 0x76)).Cartridge; // DI; HALT.
        var system = new GameBoySystem();
        system.InsertCartridge(cart);
        RunTo(system, Second * 3 / 2);
        system.Reset();
        Assert.Equal(0UL, system.TotalTCycles);
        cart.Write(0, 0x0A);
        Assert.Equal(1, Now(cart).Seconds);
        RunTo(system, Second * 6 / 10);
        Assert.Equal(2, Now(cart).Seconds); // 1.5 s + 0.6 s.
    }

    [Fact]
    public void StateRestoreBringsBackTheClockAndThePartOfTheSecond()
    {
        var cart = CartridgeLoader.Load(TestRom.CreateMbc3(0x10, 1, 2, 0xF3, 0x76)).Cartridge;
        var system = new GameBoySystem();
        system.InsertCartridge(cart);
        cart.Write(0, 0x0A);
        SetClock(cart, new(30, 0, 0, 0, 0));
        Latch(cart);
        RunTo(system, Second * 7 / 10);
        var saved = system.CaptureState();
        Assert.Equal(new RtcState(new(30, 0, 0, 0, 0), new(30, 0, 0, 0, 0), (int)system.TotalTCycles, false), saved.Cartridge.Rtc);
        RunTo(system, Second * 5 / 2);
        Assert.Equal(32, Now(cart).Seconds);
        system.RestoreState(saved);
        Assert.Equal(new RtcRegisters(30, 0, 0, 0, 0), Latched(cart));
        RunTo(system, Second - 16);
        Assert.Equal(30, Now(cart).Seconds);
        RunTo(system, Second);
        Assert.Equal(31, Now(cart).Seconds);
        StateTests.ReplayTwice(system, s => RunTo(s, (long)s.TotalTCycles + Second));
    }

    [Fact]
    public void ARemovedCartridgeStopsCountingAndAnotherConsoleContinuesIt()
    {
        var cart = CartridgeLoader.Load(TestRom.CreateMbc3(0x0F, 0, 0, 0xF3, 0x76)).Cartridge;
        var first = new GameBoySystem();
        first.InsertCartridge(cart);
        RunTo(first, Second * 12 / 10);
        first.InsertCartridge(CartridgeLoader.Load(TestRom.Create(0xF3, 0x76)).Cartridge);
        RunTo(first, Second * 5);
        var host = (IRealTimeClockCartridge)cart;
        Assert.Equal(1, host.ExportClock().Current.Seconds);
        var second = new GameBoySystem();
        second.InsertCartridge(cart);
        RunTo(second, (Second * 8 / 10) - 16);
        Assert.Equal(1, host.ExportClock().Current.Seconds);
        RunTo(second, Second * 8 / 10);
        Assert.Equal(2, host.ExportClock().Current.Seconds); // 1.2 s + 0.8 s.
    }

    private static void RunTo(GameBoySystem system, long target)
    {
        while ((long)system.TotalTCycles < target)
        {
            system.RunForTCycles((int)Math.Min(1 << 20, target - (long)system.TotalTCycles));
        }
    }

    private static void SetRegister(ICartridge cart, int register, byte value)
    {
        cart.Write(0x4000, (byte)(8 + register));
        cart.Write(0xA000, value);
    }

    // Ticks a reference clock one second at a time; a wrap at the register width does not carry.
    private static RtcRegisters TickReference(RtcRegisters start, long count)
    {
        int s = start.Seconds, m = start.Minutes, h = start.Hours, d = start.DayLow | ((start.DayHigh & 1) << 8);
        var carry = (start.DayHigh & 0x80) != 0;
        for (long i = 0; i < count; i++)
        {
            if (++s == 60)
            {
                s = 0;
                if (++m == 60)
                {
                    m = 0;
                    if (++h == 24)
                    {
                        h = 0;
                        if (++d == 512)
                        {
                            d = 0;
                            carry = true;
                        }
                    }
                    else
                    {
                        h %= 32;
                    }
                }
                else
                {
                    m %= 64;
                }
            }
            else
            {
                s %= 64;
            }
        }
        return new((byte)s, (byte)m, (byte)h, (byte)d, (byte)((d >> 8) | (carry ? 0x80 : 0)));
    }
}
