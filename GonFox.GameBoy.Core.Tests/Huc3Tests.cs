namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;

// HuC3: banking, the MCU's command protocol, its minute and day clock, and the infrared port.
[Trait("Category", "Unit")]
public sealed class Huc3Tests
{
    private const int Second = GameBoySystem.CyclesPerSecond;
    private const long Minute = 60L * Second;

    private static byte[] Image(byte romCode = 1, byte ramCode = 3, params byte[] program) =>
        TestRom.CreateMbc1(0xFE, romCode, ramCode, program); // Bank n is filled with n.

    private static ICartridge Load(byte romCode = 1, byte ramCode = 3, params byte[] program) =>
        CartridgeLoader.Load(Image(romCode, ramCode, program)).Cartridge;

    private static (ICartridge Cart, Clock Clock) Attached()
    {
        var cart = Load();
        var clock = new Clock();
        ((IClockedCartridge)cart).AttachClock(clock);
        return (cart, clock);
    }

    // Advances the clock as a run without cartridge accesses would.
    private static void Elapse(Clock clock, long tcycles) => clock.RestoreState(clock.TotalTCycles + (ulong)tcycles);

    // Runs one MCU command through the semaphore and the mailbox and returns the $C response.
    private static byte Run(ICartridge cart, byte command)
    {
        cart.Write(0, 0x0D);
        Assert.Equal(1, cart.Read(0xA000) & 1);
        cart.Write(0, 0x0B);
        cart.Write(0xA000, command);
        cart.Write(0, 0x0D);
        cart.Write(0xA000, 0xFE);
        Assert.Equal(1, cart.Read(0xA000) & 1);
        cart.Write(0, 0x0C);
        return cart.Read(0xA000);
    }

    // Sets the access address with commands 4 and 5.
    private static void Address(ICartridge cart, int address)
    {
        Run(cart, (byte)(0x40 | (address & 15)));
        Run(cart, (byte)(0x50 | (address >> 4)));
    }

    // Reads nibbles with command 1, least significant first.
    private static int Peek(ICartridge cart, int address, int nibbles)
    {
        Address(cart, address);
        var value = 0;
        for (var i = 0; i < nibbles; i++)
        {
            value |= (Run(cart, 0x10) & 15) << (4 * i);
        }

        return value;
    }

    // Writes nibbles with command 3, least significant first.
    private static void Poke(ICartridge cart, int address, int value, int nibbles)
    {
        Address(cart, address);
        for (var i = 0; i < nibbles; i++)
        {
            Run(cart, (byte)(0x30 | ((value >> (4 * i)) & 15)));
        }
    }

    // Reads the time as games do, through extended command 0.
    private static (int Minute, int Day) Now(ICartridge cart)
    {
        Run(cart, 0x60);
        return (Peek(cart, 0, 3), Peek(cart, 3, 3));
    }

    // Sets the time as games do, through extended command 1.
    private static void SetClock(ICartridge cart, int minute, int day)
    {
        Poke(cart, 0, minute, 3);
        Poke(cart, 3, day, 3);
        Run(cart, 0x61);
    }

    private static int Nibbles(byte[] memory, int at) => memory[at] | (memory[at + 1] << 4) | (memory[at + 2] << 8);

    private static void RunTo(GameBoySystem system, long target)
    {
        while ((long)system.TotalTCycles < target)
        {
            system.RunForTCycles((int)Math.Min(1 << 20, target - (long)system.TotalTCycles));
        }
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 2, 8192)]
    [InlineData(4, 3, 32768)]
    [InlineData(6, 3, 32768)]
    public void TypeFeLoadsWithItsSizesTheBatteryAndTheClock(byte romCode, byte ramCode, int ramBytes)
    {
        var image = Image(romCode, ramCode);
        var loaded = CartridgeLoader.Load(image);
        Array.Fill(image, (byte)0xFF);
        Assert.Equal(new CartridgeInfo("P01 TEST", 0xFE, 0x8000 << romCode, ramBytes, HasBattery: true), loaded.Info);
        Assert.Empty(loaded.Warnings);
        var cart = Assert.IsAssignableFrom<IHuc3ClockCartridge>(loaded.Cartridge);
        Assert.IsNotAssignableFrom<IRealTimeClockCartridge>(cart);
        Assert.Equal(0xFE, ((IStatefulCartridge)cart).TypeCode);
        Assert.Equal(ramBytes, cart.ExportRam().Length);
        var clock = cart.ExportClock();
        Assert.Equal(new byte[256], clock.Memory);
        Assert.Equal(0, clock.Seconds);
        Assert.Equal(1, cart.Read(0x4000)); // Power-on: bank 1.
    }

    // Rejects ROM above 2 MiB and RAM sizes other than none, 8 KiB and 32 KiB.
    [Theory]
    [InlineData(7, 3)]
    [InlineData(1, 1)]
    [InlineData(1, 4)]
    [InlineData(1, 5)]
    public void LargerSizesAreRejected(byte romCode, byte ramCode)
    {
        Assert.Throws<CartridgeLoadException>(() => CartridgeLoader.Load(Image(romCode, ramCode)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public void RomBankIsSevenBitsIncludingBankZeroAndWrapsAtTheRomSize(byte romCode)
    {
        var cart = Load(romCode, 0);
        var banks = 2 << romCode;
        for (var value = 0; value < 256; value++)
        {
            cart.Write((ushort)(0x2000 + (value * 31)), (byte)value); // Anywhere in 2000-3FFF.
            Assert.Equal((value & 0x7F) % banks, cart.Read(0x4000)); // Bank 0 included.
            Assert.Equal((value & 0x7F) % banks, cart.Read(0x7FFF));
            Assert.Equal(0, cart.Read(0x0000));
            Assert.Equal(0, cart.Read(0x3FFF));
        }
        cart.Write(0x2000, 1);
        cart.Write(0x6000, 0x01);
        cart.Write(0x7FFF, 0); // 6000-7FFF: no effect.
        Assert.Equal(1 % banks, cart.Read(0x4000));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 4)]
    public void RamIsReadOnlyInModeZeroAndBankedByTwoBits(byte ramCode, int banks)
    {
        var cart = Load(1, ramCode);
        Assert.Equal(0, cart.Read(0xA000)); // Power-on mode 0: read-only RAM.
        cart.Write(0xA000, 42);
        Assert.Equal(0, cart.Read(0xA000));
        cart.Write(0x1FFF, 0xFA); // Mode A.
        for (var value = 0; value < 8; value++)
        {
            cart.Write(0x5FFF, (byte)value);
            cart.Write(0xA000, (byte)(value + 1));
            cart.Write(0xBFFF, (byte)(value + 101));
        }
        cart.Write(0x0000, 0xF0); // Mode 0: read-only.
        for (var value = 0; value < 8; value++)
        {
            cart.Write(0x4000, (byte)value);
            cart.Write(0xA000, 0xEE);
            var last = Enumerable.Range(0, 8).Last(written => written % banks == value % banks); // The last write there.
            Assert.Equal(last + 1, cart.Read(0xA000));
            Assert.Equal(last + 101, cart.Read(0xBFFF));
        }
    }

    [Theory]
    [InlineData(0x01)]
    [InlineData(0x05)]
    [InlineData(0x09)]
    [InlineData(0x0F)]
    [InlineData(0x1B)]
    public void OtherSelectionsReadOpenBusAndIgnoreWrites(byte select)
    {
        var cart = Load();
        cart.Write(0, 0x0A);
        cart.Write(0xA000, 42);
        Assert.Equal(0xE1, Run(cart, 0x62));
        cart.Write(0, select);
        Assert.Equal(0xFF, cart.Read(0xA000));
        Assert.Equal(0xFF, cart.Read(0xBFFF)); // $B is write-only.
        if ((select & 15) != 0x0B)
        {
            cart.Write(0xA000, 0x31); // Ignored.
        }

        cart.Write(0, 0x0A);
        Assert.Equal(42, cart.Read(0xA000));
        cart.Write(0, 0x0C);
        Assert.Equal(0xE1, cart.Read(0xA000));
        Assert.Equal(0, ((IHuc3ClockCartridge)cart).ClockChanges);
    }

    [Fact]
    public void StatusRequestAnswersOneAndTheRegistersIgnoreTheAddressAndBitSeven()
    {
        var cart = Load();
        Assert.Equal(0xE1, Run(cart, 0x62));
        cart.Write(0, 0x0B);
        cart.Write(0xBFFF, 0xE2); // Bit 7 is lost.
        cart.Write(0, 0x0D);
        cart.Write(0xB123, 0x7E);
        Assert.Equal(0x81, cart.Read(0xBFFF));
        cart.Write(0, 0x0C);
        foreach (var address in new ushort[] { 0xA000, 0xB123, 0xBFFF })
        {
            Assert.Equal(0xE1, cart.Read(address));
        }
    }

    [Fact]
    public void OnlyClearingTheSemaphoreRunsTheMailboxCommand()
    {
        var cart = (IHuc3ClockCartridge)Load();
        cart.Write(0, 0x0B);
        cart.Write(0xA000, 0x35); // Write 5, only queued.
        Assert.Equal(0, cart.ExportClock().Memory[0]);
        cart.Write(0, 0x0C);
        Assert.Equal(0xB0, cart.Read(0xA000)); // The command just written, the old result.
        cart.Write(0, 0x0D);
        cart.Write(0xA000, 0x01); // Bit 0 set: no request.
        Assert.Equal(0, cart.ExportClock().Memory[0]);
        cart.Write(0xA000, 0x00);
        Assert.Equal(5, cart.ExportClock().Memory[0]); // Cleared: the command runs.
        cart.Write(0xA000, 0xFE);
        Assert.Equal(5, cart.ExportClock().Memory[1]); // Again, at the next address.
    }

    [Fact]
    public void ReadWriteAndAddressCommandsWalkTheMemory()
    {
        var cart = (IHuc3ClockCartridge)Load();
        Run(cart, 0x4E);
        Run(cart, 0x5F); // Address FE.
        Assert.Equal(0xB0, Run(cart, 0x3A)); // A write leaves the result.
        Run(cart, 0x3B);
        Run(cart, 0x3C); // The address wraps.
        var memory = cart.ExportClock().Memory;
        Assert.Equal((10, 11, 12), (memory[0xFE], memory[0xFF], memory[0x00]));
        Run(cart, 0x5F); // High nibble only: F1.
        Run(cart, 0x4E); // Low nibble only: FE.
        Assert.Equal(0x9A, Run(cart, 0x10)); // Command 1 with the nibble read.
        Assert.Equal(0x9B, Run(cart, 0x10));
        Assert.Equal(0x9C, Run(cart, 0x10));
        Assert.Equal(0x90, Run(cart, 0x10));
    }

    [Fact]
    public void CommandTwoWritesInPlaceAndZeroSevenAndOtherExtendedCommandsDoNothing()
    {
        var cart = (IHuc3ClockCartridge)Load();
        Poke(cart, 0x31, 5, 1);
        Address(cart, 0x30);
        Run(cart, 0x27);
        Run(cart, 0x28); // No step: 8 replaces 7.
        Assert.Equal(0x98, Run(cart, 0x10)); // Read at 30.
        var before = cart.ExportClock().Memory;
        foreach (var command in new byte[] { 0x05, 0x7A, 0x63, 0x6E, 0x6E, 0x6F })
        {
            Assert.Equal(0x80 | (command & 0x70) | 8, Run(cart, command)); // The result stays.
        }

        Assert.Equal(before, cart.ExportClock().Memory);
        Assert.Equal(0x95, Run(cart, 0x10)); // Still at 31.
    }

    [Fact]
    public void ClockCountsMinutesOfEmulatedTimeAtTheLocationsPanDocsNames()
    {
        var (cart, clock) = Attached();
        Assert.Equal((0, 0), Now(cart));
        Elapse(clock, Minute - 1);
        Assert.Equal((0, 0), Now(cart));
        Elapse(clock, 1);
        Assert.Equal((1, 0), Now(cart));
        Elapse(clock, (1439 * Minute) + (2 * Second));
        Assert.Equal((0, 1), Now(cart)); // A day later.
        Assert.Equal((0, 1), (Peek(cart, 0x10, 3), Peek(cart, 0x13, 3))); // The counters themselves.
    }

    // The time set just before a minute ends, and the time after it.
    [Theory]
    [InlineData(100, 5, 101, 5)]
    [InlineData(1439, 0, 0, 1)] // Rolls into the day.
    [InlineData(1439, 0xFFF, 0, 0)] // The day wraps.
    [InlineData(1440, 7, 1441, 7)] // Past 1439: no rollover.
    [InlineData(0xFFF, 7, 0, 7)] // Wraps without a day.
    public void OneMinuteFollowsTheRolloverRules(int minute, int day, int minute2, int day2)
    {
        var (cart, clock) = Attached();
        SetClock(cart, minute, day);
        Elapse(clock, Minute - 1);
        Assert.Equal((minute, day), Now(cart));
        Elapse(clock, 1);
        Assert.Equal((minute2, day2), Now(cart));
    }

    [Fact]
    public void SettingTheClockKeepsThePartOfTheMinute()
    {
        var (cart, clock) = Attached();
        Elapse(clock, Minute / 2);
        SetClock(cart, 5, 9);
        Elapse(clock, (Minute / 2) - 1);
        Assert.Equal((5, 9), Now(cart));
        Elapse(clock, 1);
        Assert.Equal((6, 9), Now(cart));
    }

    [Fact]
    public void SettingTheClockCopiesSixNibblesAndKeepsTheTimeLeftUntilTheEvent()
    {
        var (cart, _) = Attached();
        SetClock(cart, 100, 2);
        Poke(cart, 0x58, 600, 3);
        Poke(cart, 0x5B, 10, 3); // The event: day 10, minute 600.
        Poke(cart, 0x06, 9, 1);
        SetClock(cart, 1430, 3); // 2770 minutes on, the event too.
        Assert.Equal((490, 12), (Peek(cart, 0x58, 3), Peek(cart, 0x5B, 3)));
        Assert.Equal((1430, 3), (Peek(cart, 0x10, 3), Peek(cart, 0x13, 3)));
        Run(cart, 0x60);
        Assert.Equal((9, 0), (Peek(cart, 0x06, 1), Peek(cart, 0x16, 1))); // $06 is not part of either copy.
        SetClock(cart, 0, 0); // 5750 minutes back.
        Assert.Equal((500, 8), (Peek(cart, 0x58, 3), Peek(cart, 0x5B, 3)));
        Poke(cart, 0x58, 100, 3);
        Poke(cart, 0x5B, 0, 3);
        SetClock(cart, 0, 1);
        SetClock(cart, 0, 0);
        Assert.Equal((100, 0), (Peek(cart, 0x58, 3), Peek(cart, 0x5B, 3))); // A day on and back.
        SetClock(cart, 0, 1);
        Poke(cart, 0x5B, 0, 3);
        SetClock(cart, 0, 0);
        Assert.Equal((100, 0xFFF), (Peek(cart, 0x58, 3), Peek(cart, 0x5B, 3))); // The 12-bit day wraps.
    }

    [Fact]
    public void AdvanceAddsWholeSecondsLikeCountingEachMinute()
    {
        var random = new Random(29);
        var (cart, _) = Attached();
        var host = (IHuc3ClockCartridge)cart;
        for (var round = 0; round < 200; round++)
        {
            // Any counter values, including minutes past 1439.
            int minute = random.Next(round < 100 ? 1440 : 4096), day = random.Next(4096), seconds = random.Next(60);
            long count = round < 190 ? random.Next(200_000) : random.Next(5_000_000);
            var memory = new byte[256];
            for (var i = 0; i < 3; i++)
            {
                memory[0x10 + i] = (byte)((minute >> (4 * i)) & 15);
                memory[0x13 + i] = (byte)((day >> (4 * i)) & 15);
            }
            host.ImportClock(new(memory, seconds));
            host.AdvanceClock(count);
            var after = host.ExportClock();
            Assert.Equal((MinuteReference(minute, day, (seconds + count) / 60), (int)((seconds + count) % 60)),
                ((Nibbles(after.Memory, 0x10), Nibbles(after.Memory, 0x13)), after.Seconds));
        }
    }

    [Fact]
    public void AdvanceHandlesYearsAndRejectsNegativeTime()
    {
        var (cart, _) = Attached();
        var host = (IHuc3ClockCartridge)cart;
        host.AdvanceClock((10L * 365 * 86_400) + 3661); // 3650 days, 61 minutes and a second.
        var clock = host.ExportClock();
        Assert.Equal((61, 3650, 1), (Nibbles(clock.Memory, 0x10), Nibbles(clock.Memory, 0x13), clock.Seconds));
        host.ImportClock(new(new byte[256], 0));
        host.AdvanceClock(long.MaxValue);
        clock = host.ExportClock();
        Assert.Equal((930, 0x944, 7), (Nibbles(clock.Memory, 0x10), Nibbles(clock.Memory, 0x13), clock.Seconds));
        Assert.Throws<ArgumentOutOfRangeException>(() => host.AdvanceClock(-1));
    }

    [Fact]
    public void HostExportAndImportCarryTheMemoryAndThePartOfTheMinute()
    {
        var (cart, clock) = Attached();
        var host = (IHuc3ClockCartridge)cart;
        SetClock(cart, 1439, 0x123);
        Poke(cart, 0x80, 0xA, 1);
        Elapse(clock, Minute + (30 * Second) + (Second / 2));
        var exported = host.ExportClock();
        Assert.Equal((0, 0x124, 30, 0xA), (Nibbles(exported.Memory, 0x10), Nibbles(exported.Memory, 0x13), exported.Seconds, exported.Memory[0x80]));
        exported.Memory[0x80] = 0xFB;
        Assert.Equal(0xA, host.ExportClock().Memory[0x80]); // A detached copy.
        var (copy, copyClock) = Attached();
        var target = (IHuc3ClockCartridge)copy;
        Elapse(copyClock, Second / 4);
        target.ImportClock(exported);
        Assert.Equal(0xB, target.ExportClock().Memory[0x80]); // Only the low nibble.
        Elapse(copyClock, (30 * Second) - 1);
        Assert.Equal((0, 0x124), Now(copy)); // 30 s counted.
        Elapse(copyClock, 1);
        Assert.Equal((1, 0x124), Now(copy));
        var kept = target.ExportClock().Memory;
        Assert.Throws<ArgumentException>(() => target.ImportClock(new(new byte[255], 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => target.ImportClock(new(new byte[256], 60)));
        Assert.Throws<ArgumentOutOfRangeException>(() => target.ImportClock(new(new byte[256], -1)));
        Assert.Equal(kept, target.ExportClock().Memory);
    }

    [Fact]
    public void ClockChangesCountTheProgramsChangesOnly()
    {
        var (cart, clock) = Attached();
        var host = (IHuc3ClockCartridge)cart;
        Assert.Equal(0, host.ClockChanges);
        Poke(cart, 0x58, 5, 1);
        Assert.Equal(1, host.ClockChanges);
        Poke(cart, 0x58, 5, 1);
        Assert.Equal(1, host.ClockChanges); // The same nibble again.
        Elapse(clock, 10 * Minute);
        Run(cart, 0x60);
        Assert.Equal(1, host.ClockChanges); // Counting, and its copy.
        SetClock(cart, 0x64, 0); // Two nibbles and the clock.
        Assert.Equal(4, host.ClockChanges);
        Run(cart, 0x61);
        Assert.Equal(4, host.ClockChanges); // The clock set to what it is.
        host.ImportClock(host.ExportClock());
        host.AdvanceClock(3600);
        host.ImportRam(new byte[32768]);
        Assert.Equal(4, host.ClockChanges);
    }

    [Fact]
    public void ClockRunsOnThroughAConsoleResetAndStopsInARemovedCartridge()
    {
        var cart = Load(1, 3, 0xF3, 0x76); // DI; HALT.
        var system = new GameBoySystem();
        system.InsertCartridge(cart);
        cart.Write(0, 0x0A);
        cart.Write(0x4000, 2);
        cart.Write(0xA000, 42);
        cart.Write(0x2000, 0);
        Poke(cart, 0x80, 7, 1);
        var seed = system.CaptureState(); // Two seconds before the minute ends.
        system.RestoreState(seed with { Cartridge = seed.Cartridge with { Huc3 = seed.Cartridge.Huc3! with { SubMinute = (int)Minute - (2 * Second) } } });
        RunTo(system, Second * 3 / 2);
        system.Reset();
        Assert.Equal(0UL, system.TotalTCycles);
        Assert.Equal(1, cart.Read(0x4000));
        Assert.Equal(0, cart.Read(0xA000)); // ROM bank 1, mode 0, RAM bank 0.
        cart.Write(0x4000, 2);
        Assert.Equal(42, cart.Read(0xA000));
        Assert.Equal(7, Peek(cart, 0x80, 1)); // The MCU memory stays.
        RunTo(system, Second * 4 / 10);
        Assert.Equal((0, 0), Now(cart)); // 1.9 s.
        RunTo(system, Second * 6 / 10);
        Assert.Equal((1, 0), Now(cart)); // 2.1 s: across the reset.
        var late = system.CaptureState();
        system.RestoreState(late with { Cartridge = late.Cartridge with { Huc3 = late.Cartridge.Huc3! with { SubMinute = (int)Minute - Second } } });
        system.InsertCartridge(CartridgeLoader.Load(TestRom.Create(0xF3, 0x76)).Cartridge);
        RunTo(system, 3 * Second);
        Assert.Equal((1, 0), Now(cart)); // Out of the console: no time.
        var other = new GameBoySystem();
        other.InsertCartridge(cart);
        RunTo(other, Second - 16);
        Assert.Equal((1, 0), Now(cart));
        RunTo(other, Second);
        Assert.Equal((2, 0), Now(cart));
    }

    [Fact]
    public void InfraredReadsNoLightAndTheLedTakesBitZero()
    {
        var cart = (Huc3Cartridge)Load();
        cart.Write(0, 0x0E);
        Assert.Equal(0xC0, cart.Read(0xA000));
        Assert.Equal(0xC0, cart.Read(0xBFFF)); // No light.
        cart.Write(0xB000, 0x01);
        Assert.True(cart.InfraredLed);
        Assert.Equal(0xC0, cart.Read(0xA000)); // Not even its own light.
        cart.Write(0xA000, 0xFE);
        Assert.False(cart.InfraredLed);
        cart.Write(0xA000, 0x81);
        cart.ResetController();
        Assert.False(cart.InfraredLed);
    }

    [Fact]
    public void DebugSnapshotShowsTheBanksAndTheMode()
    {
        var cart = Load(6, 3, 0x18, 0xFE);
        var system = new GameBoySystem();
        system.InsertCartridge(cart);
        cart.Write(0x2000, 0);
        cart.Write(0x4000, 3);
        cart.Write(0, 0x0D);
        var banks = system.GetDebugSnapshot();
        Assert.Equal((0, 0, 3, false, (byte)0x0D), (banks.RomBank0, banks.RomBank1, banks.RamBank, banks.RamEnabled, banks.BankingMode));
        cart.Write(0, 0x0A);
        Assert.True(system.GetDebugSnapshot().RamEnabled);
    }

    [Fact]
    public void CpuRunsTheStatusRequest()
    {
        byte[] program =
        [
            0xF3, 0x3E, 0x0B, 0xEA, 0x00, 0x00, 0x3E, 0x62, 0xEA, 0x00, 0xA0, // DI; select $B; command 6, argument 2.
            0x3E, 0x0D, 0xEA, 0x00, 0x00, 0x3E, 0xFE, 0xEA, 0x00, 0xA0, // Select $D; clear the semaphore.
            0xFA, 0x00, 0xA0, 0xEA, 0x01, 0xC0, // The semaphore to C001.
            0x3E, 0x0C, 0xEA, 0x00, 0x00, 0xFA, 0x00, 0xA0, 0xEA, 0x00, 0xC0, 0x76 // The response to C000; HALT.
        ];
        var system = new GameBoySystem();
        system.InsertCartridge(Load(1, 3, program));
        system.RunForTCycles(1000);
        Assert.True(system.IsHalted);
        var memory = new byte[2];
        system.CopyMemory(0xC000, memory);
        Assert.Equal(new byte[] { 0xE1, 0x81 }, memory);
    }

    [Fact]
    public void StateKeepsTheMapperTheMcuAndThePartOfTheMinuteAndRejectsInvalidFields()
    {
        var cart = Load(6, 3, 0xF3, 0x76);
        var system = new GameBoySystem();
        system.InsertCartridge(cart);
        var host = (IHuc3ClockCartridge)cart;
        cart.Write(0, 0x0A);
        for (var bank = 0; bank < 4; bank++)
        {
            cart.Write(0x4000, (byte)bank);
            cart.Write(0xBFFF, (byte)(bank + 31));
        }
        SetClock(cart, 1439, 7);
        Poke(cart, 0x58, 0x5A3, 3);
        Address(cart, 0x58);
        cart.Write(0, 0x0B);
        cart.Write(0xA000, 0x91); // A read, queued.
        cart.Write(0, 0x0E);
        cart.Write(0xA000, 1); // The LED on.
        cart.Write(0x2000, 0xC5);
        cart.Write(0x4000, 0xFE);
        cart.Write(0, 0xFC); // Unused bits are not kept.
        system.RunForTCycles(Second / 2);
        var saved = system.CaptureState();
        var state = saved.Cartridge;
        Assert.Equal((0x45, 1, 0x0C, false, 2), (state.Bank, state.Upper, state.Mode, state.RamEnabled, state.RamBank));
        var mcu = state.Huc3!;
        Assert.Equal(((int)system.TotalTCycles, 0x58, 1, 1, 0), (mcu.SubMinute, mcu.Address, mcu.Command, mcu.Argument, mcu.Result));
        Assert.Equal((1439, 7, 0x5A3), (Nibbles(mcu.Memory, 0x10), Nibbles(mcu.Memory, 0x13), Nibbles(mcu.Memory, 0x58)));
        var copy = GameBoyState.Deserialize(saved.Serialize());
        StateTests.EqualState(saved, copy);
        Assert.Equal(saved.Serialize(), copy.Serialize());

        cart.Write(0, 0x0A);
        cart.Write(0xBFFF, 99);
        cart.Write(0x2000, 2);
        SetClock(cart, 5, 5);
        Poke(cart, 0x58, 0, 3);
        var changes = host.ClockChanges;
        system.RunForTCycles(100_000);
        system.RestoreState(copy);
        Assert.Equal(changes + 1, host.ClockChanges); // A restored state counts as a change.
        Assert.Equal(0x45, cart.Read(0x4000));
        Assert.True(((Huc3Cartridge)cart).InfraredLed);
        Assert.Equal(0x90, cart.Read(0xA000)); // Mode C: the queued read.
        cart.Write(0, 0x0D);
        cart.Write(0xA000, 0xFE);
        cart.Write(0, 0x0C);
        Assert.Equal(0x93, cart.Read(0xA000)); // $58 again.
        cart.Write(0, 0x0A);
        for (var bank = 0; bank < 4; bank++)
        {
            cart.Write(0x4000, (byte)bank);
            Assert.Equal(bank + 31, cart.Read(0xBFFF));
        }
        Assert.Equal((1439, 7), (Peek(cart, 0x10, 3), Peek(cart, 0x13, 3)));
        system.RestoreState(saved with { Cartridge = state with { Huc3 = mcu with { SubMinute = (int)Minute - 1000 } } });
        RunTo(system, (long)saved.TotalTCycles + 900);
        Assert.Equal((1439, 7), (Peek(cart, 0x10, 3), Peek(cart, 0x13, 3)));
        RunTo(system, (long)saved.TotalTCycles + 1000);
        Assert.Equal((0, 8), (Peek(cart, 0x10, 3), Peek(cart, 0x13, 3)));
        StateTests.ReplayTwice(system, s => RunTo(s, (long)s.TotalTCycles + Second));

        cart.Write(0x4000, 3);
        var before = host.ExportClock().Memory;
        changes = host.ClockChanges;
        foreach (var invalid in new[]
        {
            saved with { Cartridge = state with { Bank = 0x80 } },
            saved with { Cartridge = state with { Upper = 2 } },
            saved with { Cartridge = state with { Mode = 16 } },
            saved with { Cartridge = state with { RamEnabled = true } },
            saved with { Cartridge = state with { RamBank = 4 } },
            saved with { Cartridge = state with { Ram = new byte[8192] } },
            saved with { Cartridge = state with { Rtc = new RtcState(default, default, 0, false) } },
            saved with { Cartridge = state with { Huc3 = null } },
            saved with { Cartridge = state with { Huc3 = mcu with { Memory = new byte[255] } } },
            saved with { Cartridge = state with { Huc3 = mcu with { Memory = [.. new byte[255], 16] } } },
            saved with { Cartridge = state with { Huc3 = mcu with { SubMinute = -1 } } },
            saved with { Cartridge = state with { Huc3 = mcu with { SubMinute = (int)Minute } } },
            saved with { Cartridge = state with { Huc3 = mcu with { Command = 8 } } },
            saved with { Cartridge = state with { Huc3 = mcu with { Argument = 16 } } },
            saved with { Cartridge = state with { Huc3 = mcu with { Result = 16 } } },
            saved with { CartridgeType = 0x10 }
        })
        {
            Assert.Throws<ArgumentException>(() => system.RestoreState(invalid));
            Assert.Equal(3, system.GetDebugSnapshot().RamBank); // The rejected state changed nothing.
            Assert.Equal(before, host.ExportClock().Memory);
            Assert.Equal(changes, host.ClockChanges);
        }
    }

    // Counts minutes one at a time; only reaching 1440 carries into the day.
    private static (int Minute, int Day) MinuteReference(int minute, int day, long count)
    {
        for (long i = 0; i < count; i++)
        {
            minute = (minute + 1) & 0xFFF;
            if (minute == 1440)
            {
                minute = 0;
                day = (day + 1) & 0xFFF;
            }
        }
        return (minute, day);
    }
}
