namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;

// MBC2 banking and its built-in 512 x 4-bit RAM.
[Trait("Category", "Unit")]
public sealed class Mbc2Tests
{
    private static ICartridge Load(byte type = 6, byte romCode = 3) =>
        CartridgeLoader.Load(TestRom.CreateMbc1(type, romCode, 0)).Cartridge;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RomBankIsTheLowFourBitsWithZeroAsOneThenMaskedByTheRomSize(byte romCode)
    {
        var cart = Load(5, romCode);
        var banks = 2 << romCode;
        Assert.Equal(1, cart.Read(0x4000)); // Bank 1 after power-on.
        for (var value = 0; value < 256; value++)
        {
            cart.Write(0x2100, (byte)value);
            var bank = (value & 15) == 0 ? 1 : value & 15;
            Assert.Equal(bank % banks, cart.Read(0x4000));
            Assert.Equal(bank % banks, cart.Read(0x7FFF));
            Assert.Equal(0, cart.Read(0x0000));
            Assert.Equal(0, cart.Read(0x3FFF));
        }
    }

    [Theory]
    [InlineData(0x0000, false)]
    [InlineData(0x00FF, false)]
    [InlineData(0x0200, false)]
    [InlineData(0x3EFF, false)]
    [InlineData(0x0100, true)]
    [InlineData(0x01FF, true)]
    [InlineData(0x2100, true)]
    [InlineData(0x3FFF, true)]
    public void AddressBit8ChoosesRamEnableOrRomBank(ushort address, bool romBank)
    {
        var cart = Load();
        cart.Write(address, 0x0A); // A RAM enable value, or bank 10.
        Assert.Equal(romBank ? 10 : 1, cart.Read(0x4000));
        Assert.Equal(romBank ? 0xFF : 0xF0, cart.Read(0xA000)); // RAM starts as zeros.
    }

    [Fact]
    public void WritesTo4000Through7FffChangeNothing()
    {
        var cart = Load();
        cart.Write(0x2100, 3);
        cart.Write(0x0000, 0x0A);
        cart.Write(0xA000, 5);
        foreach (var address in new ushort[] { 0x4000, 0x4100, 0x5FFF, 0x6000, 0x7FFF })
        {
            foreach (var value in new byte[] { 0x00, 0x01, 0x0A, 0xFF })
            {
                cart.Write(address, value);
            }
        }

        Assert.Equal(3, cart.Read(0x4000));
        Assert.Equal(0xF5, cart.Read(0xA000));
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
        cart.Write(0x0000, 0x0A);
        cart.Write(0xA000, 7);
        cart.Write(0x0000, value);
        Assert.Equal(enabled ? 0xF7 : 0xFF, cart.Read(0xA000));
        cart.Write(0xA000, 9); // Ignored while disabled.
        cart.Write(0x0000, 0x0A);
        Assert.Equal(enabled ? 0xF9 : 0xF7, cart.Read(0xA000));
    }

    // A nibble pattern that differs between i and i + 256, so a shorter RAM shows.
    private static byte Pattern(int i) => (byte)((i ^ (i >> 4) ^ (i >> 8)) & 15);

    [Fact]
    public void RamHas512NibblesRepeatedThroughA000ToBfff()
    {
        var cart = Load();
        cart.Write(0, 0x0A);
        for (var i = 0; i < 512; i++)
        {
            cart.Write((ushort)(0xA000 + i), (byte)(0xA0 | Pattern(i)));
        }

        for (var address = 0xA000; address <= 0xBFFF; address++)
        {
            Assert.Equal(0xF0 | Pattern(address & 0x1FF), cart.Read((ushort)address)); // Upper bits read as 1.
        }

        cart.Write(0xBFFF, 0x03);
        Assert.Equal(0xF3, cart.Read(0xA1FF));
        Assert.Equal(0xF3, cart.Read(0xA3FF));
    }

    [Theory]
    [InlineData(5, 0, false)]
    [InlineData(5, 3, false)]
    [InlineData(6, 0, true)]
    [InlineData(6, 3, true)]
    public void TypesSizesAndBatteryAreAcceptedAndLoaderOwnsRom(byte type, byte romCode, bool battery)
    {
        var image = TestRom.CreateMbc1(type, romCode, 0);
        var loaded = CartridgeLoader.Load(image);
        Array.Fill(image, (byte)0xFF);
        Assert.Equal(type, loaded.Info.TypeCode);
        Assert.Equal(0x8000 << romCode, loaded.Info.RomSizeBytes);
        Assert.Equal(512, loaded.Info.RamSizeBytes);
        Assert.Equal(battery, loaded.Info.HasBattery);
        Assert.Equal(battery, loaded.Cartridge is IBatteryBackedCartridge);
        Assert.Equal(1, loaded.Cartridge.Read(0x4000));
    }

    [Theory]
    [InlineData(5, 4, 0)]
    [InlineData(6, 4, 0)]
    [InlineData(5, 1, 1)]
    [InlineData(5, 1, 2)]
    [InlineData(6, 1, 3)]
    [InlineData(6, 1, 4)]
    public void LargerRomsAndDeclaredRamAreRejected(byte type, byte romCode, byte ramCode)
    {
        Assert.Throws<CartridgeLoadException>(() => CartridgeLoader.Load(TestRom.CreateMbc1(type, romCode, ramCode)));
    }

    [Fact]
    public void PowerOnAndConsoleResetSelectBankOneAndKeepRam()
    {
        var cart = Load(5);
        cart.Write(0, 0x0A);
        cart.Write(0xA123, 0x0C);
        cart.Write(0x2100, 9);
        var system = new GameBoySystem();
        system.InsertCartridge(cart);
        system.Reset();
        Assert.Equal(1, cart.Read(0x4000));
        Assert.Equal(0xFF, cart.Read(0xA123)); // RAM is disabled again.
        cart.Write(0, 0x0A);
        Assert.Equal(0xFC, cart.Read(0xA123));
    }

    [Fact]
    public void BatteryImportKeepsTheLowNibblesAndExportCopiesThem()
    {
        var cart = (IBatteryBackedCartridge)Load(6, 1);
        var data = new byte[512];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(0xF0 | (i % 16)); // Upper bits set.
        }

        cart.Write(0x2100, 2);
        cart.ImportRam(data);
        var exported = cart.ExportRam();
        Assert.Equal(512, exported.Length);
        Assert.Equal(0x0F, exported[15]);
        Assert.Equal(0x01, exported[17]);
        Array.Fill(exported, (byte)9);
        cart.Write(0, 0x0A);
        Assert.Equal(0xF1, cart.Read(0xA011));
        Assert.Equal(2, cart.Read(0x4000));
        var before = cart.ExportRam();
        Assert.Throws<ArgumentException>(() => cart.ImportRam(new byte[511]));
        Assert.Throws<ArgumentException>(() => cart.ImportRam(new byte[513]));
        Assert.Equal(before, cart.ExportRam());
    }

    [Fact]
    public void StateKeepsTheBankRegisterRamEnableAndEveryNibble()
    {
        var cart = CartridgeLoader.Load(TestRom.CreateMbc1(6, 3, 0, 0x18, 0xFE)).Cartridge;
        var system = new GameBoySystem();
        system.InsertCartridge(cart);
        cart.Write(0, 0x0A);
        for (var i = 0; i < 512; i++)
        {
            cart.Write((ushort)(0xA000 + i), (byte)(i * 7));
        }

        cart.Write(0x2100, 0x37); // Low four bits: bank 7.
        var saved = system.CaptureState();
        Assert.Equal((7, true, 512), (saved.Cartridge.Bank, saved.Cartridge.RamEnabled, saved.Cartridge.Ram.Length));
        Assert.Equal(5, saved.Cartridge.Ram[3]); // 21 & 15.
        cart.Write(0xA003, 0);
        cart.Write(0x2100, 5);
        cart.Write(0, 0);
        system.RunForTCycles(100_000);
        system.RestoreState(saved);
        var banks = system.GetDebugSnapshot();
        Assert.Equal((0, 7, 0, true, (byte)0), (banks.RomBank0, banks.RomBank1, banks.RamBank, banks.RamEnabled, banks.BankingMode));
        for (var i = 0; i < 512; i++)
        {
            Assert.Equal(0xF0 | ((i * 7) & 15), cart.Read((ushort)(0xA000 + i)));
        }

        cart.Write(0x2100, 4);
        foreach (var invalid in new[]
        {
            saved with { Cartridge = saved.Cartridge with { Bank = 16 } },
            saved with { Cartridge = saved.Cartridge with { Upper = 1 } },
            saved with { Cartridge = saved.Cartridge with { Mode = 1 } },
            saved with { Cartridge = saved.Cartridge with { RamBank = 1 } },
            saved with { Cartridge = saved.Cartridge with { Ram = new byte[511] } },
            saved with { Cartridge = saved.Cartridge with { Ram = [.. saved.Cartridge.Ram[..511], 0x10] } }
        })
        {
            Assert.Throws<ArgumentException>(() => system.RestoreState(invalid));
            Assert.Equal(4, system.GetDebugSnapshot().RomBank1); // The rejected state changed nothing.
        }
    }

    [Fact]
    public void CpuFetchesFromTheSelectedBank()
    {
        // Selects bank 3 and jumps to its code at 4000.
        var image = TestRom.CreateMbc1(5, 2, 0, 0x3E, 3, 0xEA, 0, 0x21, 0xC3, 0, 0x40);
        new byte[] { 0x3E, 42, 0xEA, 0, 0xC0, 0x76 }.CopyTo(image, 3 * 0x4000);
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        system.RunForTCycles(200);
        Assert.True(system.IsHalted);
        var memory = new byte[1];
        system.CopyMemory(0xC000, memory);
        Assert.Equal(42, memory[0]);
        Assert.Equal(3, system.GetDebugSnapshot().RomBank1);
    }
}
