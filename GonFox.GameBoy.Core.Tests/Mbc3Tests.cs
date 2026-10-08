namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;

// MBC3 and MBC30 banking, RAM and clock registers.
[Trait("Category", "Unit")]
public sealed class Mbc3Tests
{
    private static ICartridge Load(byte type = 0x13, byte romCode = 1, byte ramCode = 3) =>
        CartridgeLoader.Load(TestRom.CreateMbc3(type, romCode, ramCode)).Cartridge;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public void RomBankIsSevenBitsZeroSelectsOneAndTheRomSizeMasksIt(byte romCode)
    {
        var cart = Load(0x11, romCode, 0);
        var banks = 2 << romCode;
        Assert.Equal(1, cart.Read(0x4000)); // Power-on: bank 1.
        for (var value = 0; value < 256; value++)
        {
            cart.Write((ushort)(0x2000 + (value * 31)), (byte)value); // Anywhere in 2000-3FFF.
            var bank = value & 0x7F; // Seven bits.
            Assert.Equal((bank == 0 ? 1 : bank) % banks, cart.Read(0x4000));
            Assert.Equal((bank == 0 ? 1 : bank) % banks, cart.Read(0x7FFF));
            Assert.Equal(0, cart.Read(0x0000));
            Assert.Equal(0, cart.Read(0x3FFF));
        }
    }

    [Fact]
    public void Mbc30SelectsAllEightRomBitsAndEightRamBanks()
    {
        var large = Load(0x13, 7); // 4 MiB ROM: MBC30.
        for (var value = 0; value < 256; value++)
        {
            large.Write(0x2000, (byte)value);
            Assert.Equal(value == 0 ? 1 : value, large.Read(0x4000));
        }
        var ram = Load(0x13, 1, 5); // 64 KiB RAM: MBC30.
        ram.Write(0x2000, 0x80);
        Assert.Equal(0, ram.Read(0x4000)); // Bit 7 kept: no 0 -> 1.
        var mbc3 = Load();
        mbc3.Write(0x2000, 0x80);
        Assert.Equal(1, mbc3.Read(0x4000)); // 7 bits: 0 -> 1.
        ram.Write(0, 0x0A);
        for (var bank = 0; bank < 8; bank++)
        {
            ram.Write(0x4000, (byte)bank);
            ram.Write(0xA000, (byte)(bank + 40));
        }
        for (var bank = 0; bank < 8; bank++)
        {
            ram.Write(0x4000, (byte)bank);
            Assert.Equal(bank + 40, ram.Read(0xA000));
        }
        Assert.Equal(65536, ((IBatteryBackedCartridge)ram).ExportRam().Length);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 4)]
    public void RamBankIsTwoBitsAndTheRamSizeMasksIt(byte ramCode, int banks)
    {
        var cart = Load(0x13, 1, ramCode);
        cart.Write(0, 0x0A);
        for (var value = 0; value < 8; value++)
        {
            cart.Write(0x5FFF, (byte)(0xF0 | value)); // Bits above bit 3 are ignored.
            cart.Write(0xA000, (byte)(value + 1));
            cart.Write(0xBFFF, (byte)(value + 101));
        }
        for (var value = 0; value < 8; value++)
        {
            cart.Write(0x4000, (byte)value);
            var last = Enumerable.Range(0, 8).Last(written => (written & 3) % banks == (value & 3) % banks); // Last write there.
            Assert.Equal(last + 1, cart.Read(0xA000));
            Assert.Equal(last + 101, cart.Read(0xBFFF));
        }
    }

    [Theory]
    [InlineData(0x0A, true)]
    [InlineData(0x1A, true)]
    [InlineData(0xFA, true)]
    [InlineData(0x00, false)]
    [InlineData(0x0B, false)]
    [InlineData(0xA0, false)]
    public void RamAndClockAreEnabledByALowNibbleOfA(byte value, bool enabled)
    {
        var cart = Load(0x10);
        cart.Write(0x1FFF, 0x0A);
        cart.Write(0xA000, 42);
        cart.Write(0x4000, 0x0B);
        cart.Write(0xA000, 7);
        Latch(cart); // Day low = 7.
        cart.Write(0x0000, value);
        Assert.Equal(enabled ? 7 : 0xFF, cart.Read(0xA000));
        cart.Write(0x4000, 0);
        Assert.Equal(enabled ? 42 : 0xFF, cart.Read(0xA000));
        cart.Write(0xA000, 9);
        cart.Write(0x4000, 0x0B);
        cart.Write(0xA000, 9); // Ignored while disabled.
        cart.Write(0x0000, 0x0A);
        Latch(cart);
        Assert.Equal(enabled ? 9 : 7, cart.Read(0xA000));
        cart.Write(0x4000, 0);
        Assert.Equal(enabled ? 9 : 42, cart.Read(0xA000));
    }

    [Fact]
    public void ClockRegistersAre08To0CAndTheRestMapNothing()
    {
        var cart = Load(0x10);
        cart.Write(0, 0x0A);
        cart.Write(0x4000, 0);
        cart.Write(0xA000, 42);
        SetClock(cart, new(0x3B, 0x2A, 0x17, 0xFE, 0x41));
        Latch(cart);
        Assert.Equal(new RtcRegisters(0x3B, 0x2A, 0x17, 0xFE, 0x41), Latched(cart));
        for (byte select = 0x0D; select <= 0x0F; select++)
        {
            cart.Write(0x4000, select);
            Assert.Equal(0xFF, cart.Read(0xA000));
            Assert.Equal(0xFF, cart.Read(0xBFFF));
            cart.Write(0xA000, 0);
        }
        cart.Write(0x4000, 0x18);
        Assert.Equal(0x3B, cart.Read(0xB123)); // Upper bits ignored.
        Assert.Equal(8, ((IBankedCartridge)cart).RamBank); // Clock register 08.
        cart.Write(0x4000, 0);
        Assert.Equal(42, cart.Read(0xA000));
        Assert.Equal(0, ((IBankedCartridge)cart).RamBank);
        Latch(cart);
        Assert.Equal(new RtcRegisters(0x3B, 0x2A, 0x17, 0xFE, 0x41), Latched(cart));
    }

    [Fact]
    public void WithoutTheClockTheRegisterSelectionsReadFfAndKeepTheRam()
    {
        var cart = Load();
        cart.Write(0, 0x0A);
        cart.Write(0xA000, 42);
        cart.Write(0x4000, 8);
        Assert.Equal(0xFF, cart.Read(0xA000));
        cart.Write(0xA000, 1);
        Latch(cart);
        cart.Write(0x4000, 0);
        Assert.Equal(42, cart.Read(0xA000));
        Assert.False(cart is IRealTimeClockCartridge);
    }

    [Fact]
    public void ConsoleResetClearsTheBankRegistersButKeepsRamAndTheLatchedClock()
    {
        var cart = CartridgeLoader.Load(TestRom.CreateMbc3(0x10, 2, 3, 0x18, 0xFE)).Cartridge;
        var system = new GameBoySystem();
        system.InsertCartridge(cart);
        cart.Write(0, 0x0A);
        cart.Write(0x4000, 2);
        cart.Write(0xA000, 42);
        cart.Write(0x2000, 5);
        SetClock(cart, new(1, 2, 3, 4, 0x40));
        Latch(cart);
        system.Reset();
        Assert.Equal(1, cart.Read(0x4000));
        Assert.Equal(0xFF, cart.Read(0xA000));
        cart.Write(0, 0x0A);
        Assert.Equal(0, cart.Read(0xA000));
        cart.Write(0x4000, 2);
        Assert.Equal(42, cart.Read(0xA000));
        Assert.Equal(new RtcRegisters(1, 2, 3, 4, 0x40), Latched(cart));
    }

    [Theory]
    [InlineData(0x0F, 0, 0, true, true, 0)]
    [InlineData(0x10, 1, 3, true, true, 32768)]
    [InlineData(0x11, 6, 0, false, false, 0)]
    [InlineData(0x12, 1, 2, false, false, 8192)]
    [InlineData(0x13, 7, 3, true, false, 32768)]
    [InlineData(0x10, 7, 5, true, true, 65536)]
    public void TypesSizesBatteryAndClockAreAccepted(byte type, byte romCode, byte ramCode, bool battery, bool clock, int ramBytes)
    {
        var image = TestRom.CreateMbc3(type, romCode, ramCode);
        var loaded = CartridgeLoader.Load(image);
        Array.Fill(image, (byte)0xFF);
        Assert.Equal(type, loaded.Info.TypeCode);
        Assert.Equal(0x8000 << romCode, loaded.Info.RomSizeBytes);
        Assert.Equal(ramBytes, loaded.Info.RamSizeBytes);
        Assert.Equal(battery, loaded.Info.HasBattery);
        Assert.Equal(battery, loaded.Cartridge is IBatteryBackedCartridge);
        Assert.Equal(clock, loaded.Cartridge is IRealTimeClockCartridge);
        Assert.Equal(type, ((IStatefulCartridge)loaded.Cartridge).TypeCode);
        Assert.Equal(1, loaded.Cartridge.Read(0x4000));
        if (battery)
        {
            Assert.Equal(ramBytes, ((IBatteryBackedCartridge)loaded.Cartridge).ExportRam().Length);
        }
    }

    [Theory]
    [InlineData(0x0F, 1, 2)]
    [InlineData(0x11, 1, 3)]
    [InlineData(0x13, 8, 3)]
    [InlineData(0x13, 1, 4)]
    [InlineData(0x13, 1, 1)]
    [InlineData(0x13, 1, 6)]
    [InlineData(0x0E, 1, 0)]
    [InlineData(0x14, 1, 0)]
    public void UnsupportedConfigurationsAreRejected(byte type, byte romCode, byte ramCode)
    {
        Assert.Throws<CartridgeLoadException>(() => CartridgeLoader.Load(TestRom.CreateMbc3(type, romCode, ramCode)));
    }

    // A RAM type with RAM size 0 runs without RAM; the battery then keeps only a clock.
    [Theory]
    [InlineData(0x10, 0x0F, true)]
    [InlineData(0x12, 0x11, false)]
    [InlineData(0x13, 0x11, false)]
    public void TypesNamingRamWithSizeZeroRunWithoutRam(byte type, byte runsAs, bool clock)
    {
        var loaded = CartridgeLoader.Load(TestRom.CreateMbc3(type, 1, 0));
        Assert.Contains(loaded.Warnings, warning => warning.Contains("no RAM is fitted", StringComparison.Ordinal));
        Assert.Equal(0, loaded.Info.RamSizeBytes);
        Assert.Equal(clock, loaded.Info.HasBattery);
        Assert.Equal(clock, loaded.Cartridge is IRealTimeClockCartridge);
        Assert.Equal(runsAs, ((IStatefulCartridge)loaded.Cartridge).TypeCode);
        loaded.Cartridge.Write(0, 0x0A);
        loaded.Cartridge.Write(0x4000, 0);
        Assert.Equal(0xFF, loaded.Cartridge.Read(0xA000));
    }

    [Fact]
    public void BatteryImportExportCopiesEveryBankWithoutChangingTheController()
    {
        var cart = (IBatteryBackedCartridge)Load();
        var data = new byte[32768];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i / 8192) + 10);
        }

        cart.Write(0, 0x0A);
        cart.Write(0x4000, 3);
        cart.ImportRam(data);
        Assert.Equal(13, cart.Read(0xA000));
        var exported = cart.ExportRam();
        Assert.Equal(10, exported[0]);
        Assert.Equal(13, exported[^1]);
        Array.Fill(exported, (byte)99);
        Assert.Equal(13, cart.Read(0xA000));
        Assert.Throws<ArgumentException>(() => cart.ImportRam(new byte[8192]));
        Assert.Equal(13, cart.Read(0xA000));
        var clockOnly = (IRealTimeClockCartridge)Load(0x0F, 0, 0);
        Assert.Empty(clockOnly.ExportRam());
        clockOnly.ImportRam([]);
        Assert.Throws<ArgumentException>(() => clockOnly.ImportRam(new byte[1]));
    }

    [Fact]
    public void CpuRunsFromBank7FOfATwoMebibyteRom()
    {
        // Selects bank 7F and jumps to its code at 4000.
        var image = TestRom.CreateMbc3(0x11, 6, 0, 0x3E, 0x7F, 0xEA, 0, 0x20, 0xC3, 0, 0x40);
        new byte[] { 0x3E, 42, 0xEA, 0, 0xC0, 0x76 }.CopyTo(image, 0x7F * 0x4000);
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        system.RunForTCycles(200);
        Assert.True(system.IsHalted);
        var memory = new byte[1];
        system.CopyMemory(0xC000, memory);
        Assert.Equal(42, memory[0]);
        Assert.Equal(0x7F, system.GetDebugSnapshot().RomBank1);
    }

    [Fact]
    public void StateKeepsTheBankRegistersRamAndClockAndRejectsInvalidFields()
    {
        var cart = CartridgeLoader.Load(TestRom.CreateMbc3(0x10, 6, 3, 0x18, 0xFE)).Cartridge;
        var system = new GameBoySystem();
        system.InsertCartridge(cart);
        cart.Write(0, 0x0A);
        for (var bank = 0; bank < 4; bank++)
        {
            cart.Write(0x4000, (byte)bank);
            cart.Write(0xBFFF, (byte)(bank + 31));
        }
        SetClock(cart, new(5, 6, 7, 8, 1));
        Latch(cart);
        cart.Write(0x4000, 0x1A);
        cart.Write(0x2000, 0xC5); // Unused bits are not kept.
        var saved = system.CaptureState();
        Assert.Equal(12, saved.FormatVersion);
        Assert.Equal((0x45, 0x0A, true), (saved.Cartridge.Bank, saved.Cartridge.RamBank, saved.Cartridge.RamEnabled));
        Assert.Equal(new RtcRegisters(5, 6, 7, 8, 1), saved.Cartridge.Rtc!.Latched);
        cart.Write(0x4000, 0x0A);
        cart.Write(0xA000, 20);
        cart.Write(0x4000, 0);
        cart.Write(0xBFFF, 99);
        cart.Write(0x2000, 2);
        Latch(cart);
        system.RunForTCycles(100_000);
        system.RestoreState(saved);
        Assert.Equal(0x45, cart.Read(0x4000));
        Assert.Equal(new RtcRegisters(5, 6, 7, 8, 1), Latched(cart));
        Assert.Equal(7, Now(cart).Hours); // Live clock restored too.
        for (var bank = 0; bank < 4; bank++)
        {
            cart.Write(0x4000, (byte)bank);
            Assert.Equal(bank + 31, cart.Read(0xBFFF));
        }
        cart.Write(0x4000, 3);
        var rtc = saved.Cartridge.Rtc!;
        foreach (var invalid in new[]
        {
            saved with { Cartridge = saved.Cartridge with { Bank = 0x80 } },
            saved with { Cartridge = saved.Cartridge with { RamBank = 16 } },
            saved with { Cartridge = saved.Cartridge with { Upper = 1 } },
            saved with { Cartridge = saved.Cartridge with { Mode = 1 } },
            saved with { Cartridge = saved.Cartridge with { Ram = new byte[8192] } },
            saved with { Cartridge = saved.Cartridge with { Rtc = null } },
            saved with { Cartridge = saved.Cartridge with { Rtc = rtc with { Current = rtc.Current with { Seconds = 64 } } } },
            saved with { Cartridge = saved.Cartridge with { Rtc = rtc with { Current = rtc.Current with { Hours = 32 } } } },
            saved with { Cartridge = saved.Cartridge with { Rtc = rtc with { Latched = rtc.Latched with { DayHigh = 0x02 } } } },
            saved with { Cartridge = saved.Cartridge with { Rtc = rtc with { SubSecond = GameBoySystem.CyclesPerSecond } } },
            saved with { Cartridge = saved.Cartridge with { Rtc = rtc with { SubSecond = -1 } } },
            saved with { FormatVersion = 6 }
        })
        {
            Assert.Throws<ArgumentException>(() => system.RestoreState(invalid));
            Assert.Equal(3, system.GetDebugSnapshot().RamBank); // The rejected state changed nothing.
        }
    }

    [Fact]
    public void ControllersWithoutTheClockRejectAClockField()
    {
        var mbc3 = new GameBoySystem();
        mbc3.InsertCartridge(Load());
        var mbc1 = new GameBoySystem();
        mbc1.InsertCartridge(CartridgeLoader.Load(TestRom.CreateMbc1()).Cartridge);
        var clock = new GameBoySystem();
        clock.InsertCartridge(Load(0x10));
        var rtc = clock.CaptureState().Cartridge.Rtc;
        foreach (var system in new[] { mbc3, mbc1, TestRom.Start(0x18, 0xFE) })
        {
            var saved = system.CaptureState();
            Assert.Null(saved.Cartridge.Rtc);
            Assert.Throws<ArgumentException>(() => system.RestoreState(saved with { Cartridge = saved.Cartridge with { Rtc = rtc } }));
            system.RestoreState(saved);
        }
    }

    internal static void Latch(ICartridge cart)
    {
        cart.Write(0x6000, 0);
        cart.Write(0x7FFF, 1);
    }

    internal static RtcRegisters Latched(ICartridge cart)
    {
        byte Read(int register)
        {
            cart.Write(0x4000, (byte)(8 + register));
            return cart.Read(0xA000);
        }
        return new(Read(0), Read(1), Read(2), Read(3), Read(4));
    }

    internal static RtcRegisters Now(ICartridge cart)
    {
        Latch(cart);
        return Latched(cart);
    }

    // Writes the clock registers seconds first and DH, with the halt flag, last.
    internal static void SetClock(ICartridge cart, RtcRegisters value)
    {
        byte[] registers = [value.Seconds, value.Minutes, value.Hours, value.DayLow, value.DayHigh];
        for (var register = 0; register < 5; register++)
        {
            cart.Write(0x4000, (byte)(8 + register));
            cart.Write(0xA000, registers[register]);
        }
    }
}
