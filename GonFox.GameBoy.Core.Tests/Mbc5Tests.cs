namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;

// MBC5 banking, RAM and rumble motor.
[Trait("Category", "Unit")]
public sealed class Mbc5Tests
{
    private static ICartridge Load(byte type = 0x1B, byte romCode = 1, byte ramCode = 3) =>
        CartridgeLoader.Load(TestRom.CreateMbc5(type, romCode, ramCode)).Cartridge;

    // Reads the 9-bit bank number stored in the first two bytes of the mapped bank.
    private static int Bank(ICartridge cart, ushort window) => cart.Read(window) | (cart.Read((ushort)(window + 1)) << 8);

    private static void Select(ICartridge cart, int bank)
    {
        cart.Write(0x2000, (byte)bank);
        cart.Write(0x3000, (byte)(bank >> 8));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void RomBankIsNineBitsIncludingBankZeroAndWrapsAtTheRomSize(byte romCode)
    {
        var cart = Load(0x19, romCode, 0);
        var banks = 2 << romCode;
        for (var bank = 0; bank < 512; bank++)
        {
            Select(cart, bank);
            Assert.Equal(bank % banks, Bank(cart, 0x4000));
            Assert.Equal((bank % banks) & 0xFF, cart.Read(0x7FFF));
            Assert.Equal(0, Bank(cart, 0x0000));
            Assert.Equal(0, cart.Read(0x3FFF));
        }
    }

    [Fact]
    public void RegisterRangesAndUnusedBitsAreDecoded()
    {
        var cart = Load(0x19, 8, 0); // 8 MiB: nine bits connected.
        cart.Write(0x2FFF, 0x34);
        cart.Write(0x3FFF, 0xFF);
        Assert.Equal(0x134, Bank(cart, 0x4000));
        cart.Write(0x3000, 0xFE); // Only bit 0 counts.
        Assert.Equal(0x34, Bank(cart, 0x4000));
        cart.Write(0x2000, 0); // Bank 0 is selectable.
        Assert.Equal(0, Bank(cart, 0x4000));
        cart.Write(0x6000, 0x01);
        cart.Write(0x7FFF, 0xFF); // No register at 6000-7FFF.
        Assert.Equal(0, Bank(cart, 0x4000));
        Assert.Equal(0, Bank(cart, 0x0000));
    }

    [Fact]
    public void PowerOnAndConsoleResetSelectBankOneAndKeepRam()
    {
        var cart = Load(0x1A, 8); // 8 MiB, so ROMB1 is visible.
        Assert.Equal(1, Bank(cart, 0x4000));
        Assert.Equal(0xFF, cart.Read(0xA000));
        cart.Write(0, 0x0A);
        cart.Write(0x4000, 2);
        cart.Write(0xA000, 42);
        Select(cart, 0x105);
        var system = new GameBoySystem();
        system.InsertCartridge(cart);
        system.Reset();
        Assert.Equal(1, Bank(cart, 0x4000));
        Assert.Equal(0xFF, cart.Read(0xA000)); // RAM is disabled again.
        cart.Write(0, 0x0A);
        Assert.Equal(0, cart.Read(0xA000)); // RAM bank 0 again.
        cart.Write(0x4000, 2);
        Assert.Equal(42, cart.Read(0xA000));
    }

    [Theory]
    [InlineData(0x0A, true)]
    [InlineData(0x1A, true)]
    [InlineData(0xFA, true)]
    [InlineData(0x00, false)]
    [InlineData(0x0B, false)]
    [InlineData(0xA0, false)]
    public void RamIsEnabledByALowNibbleOfA(byte value, bool enabled)
    {
        var cart = Load();
        cart.Write(0x1FFF, 0x0A);
        cart.Write(0xA000, 42);
        cart.Write(0x0000, value);
        Assert.Equal(enabled ? 42 : 0xFF, cart.Read(0xA000));
        cart.Write(0xA000, 7); // Ignored while disabled.
        cart.Write(0x0000, 0x0A);
        Assert.Equal(enabled ? 7 : 42, cart.Read(0xA000));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 4)]
    [InlineData(5, 8)]
    [InlineData(4, 16)]
    public void RamBankIsFourBitsAndWrapsAtTheRamSize(byte ramCode, int banks)
    {
        var cart = Load(0x1A, 1, ramCode);
        cart.Write(0, 0x0A);
        for (var bank = 0; bank < 16; bank++)
        {
            cart.Write(0x5FFF, (byte)(0xF0 | bank)); // Upper four bits ignored.
            cart.Write(0xA000, (byte)(bank + 1));
            cart.Write(0xBFFF, (byte)(bank + 101));
        }
        for (var bank = 0; bank < 16; bank++)
        {
            cart.Write(0x4000, (byte)bank);
            var last = (bank % banks) + 16 - banks; // Last write to this bank.
            Assert.Equal(last + 1, cart.Read(0xA000));
            Assert.Equal(last + 101, cart.Read(0xBFFF));
        }
    }

    [Theory]
    [InlineData(0x19, 8, 0, false, 0)]
    [InlineData(0x1A, 0, 2, false, 8192)]
    [InlineData(0x1B, 3, 4, true, 131072)]
    [InlineData(0x1B, 1, 5, true, 65536)]
    [InlineData(0x1C, 8, 0, false, 0)]
    [InlineData(0x1D, 1, 3, false, 32768)]
    [InlineData(0x1E, 1, 5, true, 65536)] // Rumble.
    public void TypesSizesAndBatteryAreAcceptedAndLoaderOwnsRom(byte type, byte romCode, byte ramCode, bool battery, int ramBytes)
    {
        var image = TestRom.CreateMbc5(type, romCode, ramCode);
        var loaded = CartridgeLoader.Load(image);
        Array.Fill(image, (byte)0xFF);
        Assert.Equal(type, loaded.Info.TypeCode);
        Assert.Equal(0x8000 << romCode, loaded.Info.RomSizeBytes);
        Assert.Equal(ramBytes, loaded.Info.RamSizeBytes);
        Assert.Equal(battery, loaded.Info.HasBattery);
        Assert.Equal(battery, loaded.Cartridge is IBatteryBackedCartridge);
        Assert.Equal(1, loaded.Cartridge.Read(0x4000));
    }

    [Theory]
    [InlineData(0x19, 1, 2)]
    [InlineData(0x1A, 1, 1)]
    [InlineData(0x1A, 1, 6)]
    [InlineData(0x19, 9, 0)]
    [InlineData(0x1C, 1, 2)]
    [InlineData(0x1D, 1, 4)]
    [InlineData(0x1E, 1, 4)] // Rumble: three bank bits.
    [InlineData(0x1F, 1, 0)]
    [InlineData(0x03, 1, 4)]
    [InlineData(0x01, 7, 0)] // The larger sizes stay MBC5-only.
    public void UnsupportedConfigurationsAreRejected(byte type, byte romCode, byte ramCode)
    {
        Assert.Throws<CartridgeLoadException>(() => CartridgeLoader.Load(TestRom.CreateMbc5(type, romCode, ramCode)));
    }

    [Theory]
    [InlineData(0x1A, 0x19)]
    [InlineData(0x1B, 0x19)]
    [InlineData(0x1D, 0x1C)]
    [InlineData(0x1E, 0x1C)]
    public void TypesNamingRamWithSizeZeroRunWithoutRam(byte type, byte runsAs)
    {
        var loaded = CartridgeLoader.Load(TestRom.CreateMbc5(type, 1, 0));
        Assert.Contains(loaded.Warnings, warning => warning.Contains("no RAM is fitted", StringComparison.Ordinal));
        Assert.Equal(0, loaded.Info.RamSizeBytes);
        Assert.False(loaded.Info.HasBattery);
        Assert.False(loaded.Cartridge is IBatteryBackedCartridge);
        Assert.Equal(runsAs, ((IStatefulCartridge)loaded.Cartridge).TypeCode);
        loaded.Cartridge.Write(0, 0x0A);
        Assert.Equal(0xFF, loaded.Cartridge.Read(0xA000));
    }

    // On rumble boards bits 0-2 select the RAM bank; other boards use all four bits.
    [Fact]
    public void RumbleMotorIsBitThreeOfTheRamBankRegister()
    {
        var loaded = CartridgeLoader.Load(TestRom.CreateMbc5(0x1E, 1, 5));
        Assert.True(loaded.Info.HasRumble);
        var cart = (IRumbleCartridge)loaded.Cartridge;
        cart.Write(0, 0x0A);
        for (var bank = 0; bank < 8; bank++)
        {
            cart.Write(0x4000, (byte)bank);
            cart.Write(0xA000, (byte)(bank + 1));
        }
        cart.Write(0x4000, 0x09);
        Assert.True(cart.MotorOn);
        Assert.Equal(2, cart.Read(0xA000));
        cart.Write(0x4000, 0x0F);
        Assert.True(cart.MotorOn);
        Assert.Equal(8, cart.Read(0xA000));
        var state = ((IStatefulCartridge)cart).CaptureState();
        cart.Write(0x4000, 0x07);
        Assert.False(cart.MotorOn);
        Assert.Equal(8, cart.Read(0xA000));
        ((IStatefulCartridge)cart).RestoreState(state);
        Assert.True(cart.MotorOn);
        cart.ResetController();
        Assert.False(cart.MotorOn);

        var plain = CartridgeLoader.Load(TestRom.CreateMbc5(0x1B, 1, 4));
        Assert.False(plain.Info.HasRumble);
        var other = (IRumbleCartridge)plain.Cartridge;
        other.Write(0, 0x0A);
        other.Write(0x4000, 0x09);
        other.Write(0xA000, 77);
        Assert.False(other.MotorOn);
        other.Write(0x4000, 0x01);
        Assert.NotEqual(77, other.Read(0xA000));
    }

    [Fact]
    public void MulticartLogoCheckAndMbc1CapacityRuleDoNotApply()
    {
        var image = TestRom.CreateMbc5(0x1B, 5);
        image.AsSpan(0x104, 48).CopyTo(image.AsSpan(0x40104));
        var cart = CartridgeLoader.Load(image).Cartridge; // 1 MiB with a second logo.
        Select(cart, 0x10);
        Assert.Equal(0x10, Bank(cart, 0x4000));
    }

    [Fact]
    public void BatteryImportExportCopiesEveryBankWithoutChangingController()
    {
        var cart = (IBatteryBackedCartridge)Load(0x1B, 1, 4);
        var data = new byte[131072];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i / 8192) + 10);
        }

        cart.Write(0, 0x0A);
        cart.Write(0x4000, 15);
        cart.ImportRam(data);
        Assert.Equal(25, cart.Read(0xA000));
        var exported = cart.ExportRam();
        Assert.Equal(10, exported[0]);
        Assert.Equal(25, exported[^1]);
        Array.Fill(exported, (byte)99);
        Assert.Equal(25, cart.Read(0xA000));
        var before = cart.ExportRam();
        Assert.Throws<ArgumentException>(() => cart.ImportRam(new byte[131071]));
        Assert.Throws<ArgumentException>(() => cart.ImportRam(new byte[32768]));
        Assert.Equal(before, cart.ExportRam());
        Assert.Equal(25, cart.Read(0xA000));
    }

    [Fact]
    public void StateKeepsBothRomBankRegistersTheRamBankAndEveryRamBank()
    {
        var cart = CartridgeLoader.Load(TestRom.CreateMbc5(0x1B, 8, 4, 0x18, 0xFE)).Cartridge;
        var system = new GameBoySystem();
        system.InsertCartridge(cart);
        cart.Write(0, 0x0A);
        for (var bank = 0; bank < 16; bank++)
        {
            cart.Write(0x4000, (byte)bank);
            cart.Write(0xBFFF, (byte)(bank + 31));
        }
        cart.Write(0x4000, 0xF9);
        cart.Write(0x2000, 0x23);
        cart.Write(0x3000, 0xFF); // Unused bits are not kept.
        var saved = system.CaptureState();
        Assert.Equal(12, saved.FormatVersion);
        Assert.Equal((0x23, 1, 9, true), (saved.Cartridge.Bank, saved.Cartridge.Upper, saved.Cartridge.RamBank, saved.Cartridge.RamEnabled));
        cart.Write(0xBFFF, 255);
        Select(cart, 2);
        cart.Write(0x4000, 0);
        cart.Write(0, 0);
        system.RunForTCycles(100_000);
        system.RestoreState(saved);
        var banks = system.GetDebugSnapshot();
        Assert.Equal((0, 0x123, 9, true, (byte)0), (banks.RomBank0, banks.RomBank1, banks.RamBank, banks.RamEnabled, banks.BankingMode));
        Assert.Equal(0x123, Bank(cart, 0x4000));
        Assert.Equal(40, cart.Read(0xBFFF));
        for (var bank = 0; bank < 16; bank++)
        {
            cart.Write(0x4000, (byte)bank);
            Assert.Equal(bank + 31, cart.Read(0xBFFF));
        }
        cart.Write(0x4000, 3);
        foreach (var invalid in new[]
        {
            saved with { Cartridge = saved.Cartridge with { Upper = 2 } },
            saved with { Cartridge = saved.Cartridge with { RamBank = 16 } },
            saved with { Cartridge = saved.Cartridge with { Mode = 1 } },
            saved with { Cartridge = saved.Cartridge with { Ram = new byte[32768] } },
            saved with { FormatVersion = 6 }
        })
        {
            Assert.Throws<ArgumentException>(() => system.RestoreState(invalid));
            Assert.Equal(3, system.GetDebugSnapshot().RamBank); // The rejected state changed nothing.
        }
    }

    [Fact]
    public void OtherControllersRejectARamBankField()
    {
        var mbc1 = new GameBoySystem();
        mbc1.InsertCartridge(CartridgeLoader.Load(TestRom.CreateMbc1()).Cartridge);
        foreach (var system in new[] { mbc1, TestRom.Start(0x18, 0xFE) })
        {
            var saved = system.CaptureState();
            Assert.Equal(0, saved.Cartridge.RamBank);
            Assert.Throws<ArgumentException>(() => system.RestoreState(saved with { Cartridge = saved.Cartridge with { RamBank = 1 } }));
            system.RestoreState(saved);
        }
    }

    [Fact]
    public void CpuFetchesFromABankAboveTwoHundredFiftyFive()
    {
        // Selects bank 101h and jumps to its code at 4000.
        var image = TestRom.CreateMbc5(0x19, 8, 0, 0x3E, 1, 0xEA, 0, 0x30, 0xEA, 0, 0x20, 0xC3, 0, 0x40);
        new byte[] { 0x3E, 42, 0xEA, 0, 0xC0, 0x76 }.CopyTo(image, 0x101 * 0x4000);
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        system.RunForTCycles(200);
        Assert.True(system.IsHalted);
        var memory = new byte[1];
        system.CopyMemory(0xC000, memory);
        Assert.Equal(42, memory[0]);
        var before = system.GetDebugSnapshot();
        Assert.Equal(0x101, before.RomBank1);
        system.CopyMemory(0x4000, memory);
        Assert.Equal(0x3E, memory[0]);
        Assert.Equal(before, system.GetDebugSnapshot());
    }
}
