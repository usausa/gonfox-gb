namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;

// HuC1 (type FF) banking, IR register and LED.
[Trait("Category", "Unit")]
public sealed class HuC1Tests
{
    // Loads a HuC1 image whose banks are filled with their own numbers.
    private static ICartridge Load(byte romCode = 5, byte ramCode = 3) =>
        CartridgeLoader.Load(TestRom.CreateMbc1(0xFF, romCode, ramCode)).Cartridge;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RomBankIncludesBankZeroAndWrapsAtTheRomSize(byte romCode)
    {
        var cart = Load(romCode, 0);
        var banks = 2 << romCode;
        for (var value = 0; value < 256; value++)
        {
            cart.Write(value % 2 == 0 ? (ushort)0x2000 : (ushort)0x3FFF, (byte)value);
            Assert.Equal(value % banks, cart.Read(0x4000));
            Assert.Equal(value % banks, cart.Read(0x7FFF));
            Assert.Equal(0, cart.Read(0x0000));
            Assert.Equal(0, cart.Read(0x3FFF));
        }
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 4)]
    public void RamBankWrapsAtTheRamSize(byte ramCode, int banks)
    {
        var cart = Load(1, ramCode);
        for (var bank = 0; bank < 4; bank++)
        {
            cart.Write(0x5FFF, (byte)(0xFC | bank)); // No RAM enable needed.
            cart.Write(0xA000, (byte)(bank + 1));
            cart.Write(0xBFFF, (byte)(bank + 101));
        }
        for (var bank = 0; bank < 4; bank++)
        {
            cart.Write(0x4000, (byte)bank);
            var last = (bank % banks) + 4 - banks; // Last write to this bank.
            Assert.Equal(last + 1, cart.Read(0xA000));
            Assert.Equal(last + 101, cart.Read(0xBFFF));
        }
    }

    [Theory]
    [InlineData(0x0E, true)]
    [InlineData(0x0A, false)]
    [InlineData(0x00, false)]
    [InlineData(0x0F, false)]
    [InlineData(0x1E, false)]
    [InlineData(0xEE, false)]
    public void ZeroEhSelectsTheIrRegisterAndAnyOtherValueTheRam(byte value, bool ir)
    {
        var cart = Load();
        cart.Write(0xA000, 42);
        cart.Write(0xBFFF, 43); // The RAM is there from power-on.
        cart.Write(0x1FFF, value);
        Assert.Equal(ir ? 0xC0 : 42, cart.Read(0xA000));
        Assert.Equal(ir ? 0xC0 : 43, cart.Read(0xBFFF));
        Assert.Equal(!ir, ((IBankedCartridge)cart).RamEnabled);
        cart.Write(0xA000, 0x01); // LED in IR mode, else RAM.
        Assert.Equal(ir, ((IInfraredCartridge)cart).LedOn);
        cart.Write(0x0000, 0x00);
        Assert.Equal(ir ? 42 : 1, cart.Read(0xA000));
        Assert.Equal(43, cart.Read(0xBFFF));
    }

    [Fact]
    public void IrRegisterSeesNoLightAndBitZeroDrivesTheLed()
    {
        var cart = (IInfraredCartridge)Load();
        cart.Write(0, 0x0E);
        Assert.False(cart.LedOn);
        Assert.Equal(0xC0, cart.Read(0xA000));
        cart.Write(0xA000, 0x01);
        Assert.True(cart.LedOn);
        Assert.Equal(0xC0, cart.Read(0xA000));
        Assert.Equal(0xC0, cart.Read(0xB123));
        cart.Write(0xBFFF, 0x00);
        Assert.False(cart.LedOn);
        cart.Write(0xB000, 0xFF);
        Assert.True(cart.LedOn);
        cart.Write(0xA000, 0xFE);
        Assert.False(cart.LedOn);
    }

    [Fact]
    public void RegisterRangesAreDecodedAndSixThousandToSevenFffHasNoRegister()
    {
        var cart = Load();
        cart.Write(0x3FFF, 0x25);
        cart.Write(0x5FFF, 0x02);
        cart.Write(0xA000, 42);
        cart.Write(0x6000, 0x01);
        cart.Write(0x7FFF, 0xFF);
        Assert.Equal(0x25, cart.Read(0x4000));
        Assert.Equal(0, cart.Read(0x0000));
        Assert.Equal(42, cart.Read(0xA000));
        cart.Write(0x4000, 0);
        Assert.NotEqual(42, cart.Read(0xA000));
        cart.Write(0x1FFF, 0x0E);
        Assert.Equal(0xC0, cart.Read(0xA000));
        foreach (var address in new ushort[] { 0x8000, 0x9FFF, 0xC000, 0xFFFF })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => cart.Read(address));
            Assert.Throws<ArgumentOutOfRangeException>(() => cart.Write(address, 0));
        }
    }

    [Fact]
    public void PowerOnAndConsoleResetSelectBankOneTheRamAndTheLedOffAndKeepRam()
    {
        var cart = Load();
        Assert.Equal(1, cart.Read(0x4000));
        Assert.False(((IInfraredCartridge)cart).LedOn);
        cart.Write(0x4000, 2);
        cart.Write(0xA000, 42);
        cart.Write(0x2000, 9);
        cart.Write(0, 0x0E);
        cart.Write(0xA000, 1);
        var system = new GameBoySystem();
        system.InsertCartridge(cart);
        system.Reset();
        Assert.Equal(1, cart.Read(0x4000));
        Assert.False(((IInfraredCartridge)cart).LedOn);
        Assert.Equal(0, cart.Read(0xA000)); // RAM mode, RAM bank 0.
        cart.Write(0x4000, 2);
        Assert.Equal(42, cart.Read(0xA000));
    }

    [Theory]
    [InlineData(0, 0, 0, false)]
    [InlineData(5, 2, 8192, true)]
    [InlineData(3, 3, 32768, true)]
    public void SizesAndBatteryAreAcceptedAndLoaderOwnsRom(byte romCode, byte ramCode, int ramBytes, bool battery)
    {
        var image = TestRom.CreateMbc1(0xFF, romCode, ramCode);
        var loaded = CartridgeLoader.Load(image);
        Array.Fill(image, (byte)0xFF);
        Assert.Equal(new CartridgeInfo("P01 TEST", 0xFF, 0x8000 << romCode, ramBytes, battery), loaded.Info);
        Assert.Equal(battery, loaded.Cartridge is IBatteryBackedCartridge);
        Assert.Equal(0xFF, ((IStatefulCartridge)loaded.Cartridge).TypeCode);
        Assert.Equal(ramBytes == 0, loaded.Warnings.Any(warning => warning.Contains("no RAM is fitted", StringComparison.Ordinal)));
        Assert.Equal(1, loaded.Cartridge.Read(0x4000));
        Assert.Equal(ramBytes == 0 ? 0xFF : 0, loaded.Cartridge.Read(0xA000));
    }

    // The known bank register widths are 6 bits for ROM and 2 bits for RAM.
    [Theory]
    [InlineData(6, 3)]
    [InlineData(7, 0)]
    [InlineData(1, 1)]
    [InlineData(1, 4)]
    [InlineData(1, 5)]
    public void SizesBeyondTheKnownRegisterWidthsAreRejected(byte romCode, byte ramCode)
    {
        Assert.Throws<CartridgeLoadException>(() => CartridgeLoader.Load(TestRom.CreateMbc1(0xFF, romCode, ramCode)));
    }

    [Fact]
    public void BatteryImportExportCopiesEveryBankWithoutChangingController()
    {
        var cart = (IBatteryBackedCartridge)Load();
        var data = new byte[32768];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i / 8192) + 10);
        }

        cart.Write(0x4000, 3);
        cart.ImportRam(data);
        Assert.Equal(13, cart.Read(0xA000));
        var exported = cart.ExportRam();
        Assert.Equal(10, exported[0]);
        Assert.Equal(13, exported[^1]);
        Array.Fill(exported, (byte)99);
        Assert.Equal(13, cart.Read(0xA000));
        var before = cart.ExportRam();
        Assert.Throws<ArgumentException>(() => cart.ImportRam(new byte[32767]));
        Assert.Throws<ArgumentException>(() => cart.ImportRam(new byte[8192]));
        Assert.Equal(before, cart.ExportRam());
        Assert.Equal(13, cart.Read(0xA000));
    }

    [Fact]
    public void StateKeepsTheBanksTheIrModeTheLedAndEveryRamBank()
    {
        var cart = CartridgeLoader.Load(TestRom.CreateMbc1(0xFF, 5, 3, 0x18, 0xFE)).Cartridge;
        var system = new GameBoySystem();
        system.InsertCartridge(cart);
        for (var bank = 0; bank < 4; bank++)
        {
            cart.Write(0x4000, (byte)bank);
            cart.Write(0xBFFF, (byte)(bank + 31));
        }
        cart.Write(0x2000, 0xE5);
        cart.Write(0x4000, 0xFE);
        cart.Write(0, 0x0E);
        cart.Write(0xA000, 1); // Unused bits are not kept.
        var saved = system.CaptureState();

        // Mode holds the IR mode and Upper the LED; a HuC1 has no RAM enable.
        Assert.Equal((0x25, 2, 1, 1, false), (saved.Cartridge.Bank, saved.Cartridge.RamBank, saved.Cartridge.Mode,
            saved.Cartridge.Upper, saved.Cartridge.RamEnabled));
        cart.Write(0xA000, 0);
        cart.Write(0, 0);
        cart.Write(0xBFFF, 255);
        cart.Write(0x2000, 3);
        system.RunForTCycles(100_000);
        system.RestoreState(GameBoyState.Deserialize(saved.Serialize()));
        var banks = system.GetDebugSnapshot();
        Assert.Equal((0, 0x25, 2, false, (byte)0), (banks.RomBank0, banks.RomBank1, banks.RamBank, banks.RamEnabled, banks.BankingMode));
        Assert.True(((IInfraredCartridge)cart).LedOn);
        Assert.Equal(0xC0, cart.Read(0xBFFF));
        Assert.Equal(0x25, cart.Read(0x4000));
        cart.Write(0, 0x0A);
        for (var bank = 0; bank < 4; bank++)
        {
            cart.Write(0x4000, (byte)bank);
            Assert.Equal(bank + 31, cart.Read(0xBFFF));
        }
        cart.Write(0x4000, 3);
        foreach (var invalid in new[]
        {
            saved with { Cartridge = saved.Cartridge with { Bank = 64 } },
            saved with { Cartridge = saved.Cartridge with { RamBank = 4 } },
            saved with { Cartridge = saved.Cartridge with { Mode = 2 } },
            saved with { Cartridge = saved.Cartridge with { Upper = 2 } },
            saved with { Cartridge = saved.Cartridge with { RamEnabled = true } },
            saved with { Cartridge = saved.Cartridge with { Ram = new byte[8192] } },
            saved with { Cartridge = saved.Cartridge with { Rtc = new RtcState(default, default, 0, false) } },
            saved with { CartridgeType = 0x1B }
        })
        {
            Assert.Throws<ArgumentException>(() => system.RestoreState(invalid));
            Assert.Equal((3, true), (system.GetDebugSnapshot().RamBank, system.GetDebugSnapshot().RamEnabled)); // Nothing changed.
        }
    }

    // Maps bank 0 at 4000-7FFF and jumps there to run the code at 0160.
    [Fact]
    public void CpuFetchesBankZeroThroughTheSwitchableWindow()
    {
        var image = TestRom.CreateMbc1(0xFF, 2, 0, 0x3E, 0, 0xEA, 0, 0x20, 0xC3, 0x60, 0x41);
        new byte[] { 0x3E, 42, 0xEA, 0, 0xC0, 0x76 }.CopyTo(image, 0x160);
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        Assert.Equal(1, system.GetDebugSnapshot().RomBank1);
        system.RunForTCycles(200);
        Assert.True(system.IsHalted);
        var memory = new byte[1];
        system.CopyMemory(0xC000, memory);
        Assert.Equal(42, memory[0]);
        Assert.Equal(0, system.GetDebugSnapshot().RomBank1);
        Assert.Equal(0x41, system.GetDebugSnapshot().PC >> 8);
    }
}
