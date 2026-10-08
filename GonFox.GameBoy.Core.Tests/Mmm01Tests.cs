namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;

// MMM01 (types 0B-0D): the menu, mapping a game, the bank masks and multiplexing.
[Trait("Category", "Unit")]
public sealed class Mmm01Tests
{
    // Builds an image whose banks hold their numbers, with the menu in the last 32 KiB.
    internal static byte[] Create(byte type = 0x0D, byte romCode = 4, byte ramCode = 3, params byte[] menuProgram)
    {
        var image = TestRom.CreateMbc5(0x01, romCode, 0);
        var header = TestRom.Create();
        "MMM01 MENU"u8.CopyTo(header.AsSpan(0x134));
        header[0x147] = type;
        header[0x148] = romCode;
        header[0x149] = ramCode;
        TestRom.UpdateHeaderChecksum(header);
        var menu = image.Length - 0x8000;
        header.AsSpan(0x100, 0x50).CopyTo(image.AsSpan(menu + 0x100));
        menuProgram.CopyTo(image, menu + 0x150);
        return image;
    }

    private static ICartridge Load(byte type = 0x0D, byte romCode = 4, byte ramCode = 3) =>
        CartridgeLoader.Load(Create(type, romCode, ramCode)).Cartridge;

    private static int Bank(ICartridge cart, ushort window) => cart.Read(window) | (cart.Read((ushort)(window + 1)) << 8);

    private static (int RomBank0, int RomBank1, int RamBank, bool RamEnabled, byte BankingMode) Banks(GameBoySystem system)
    {
        var s = system.GetDebugSnapshot();
        return (s.RomBank0, s.RomBank1, s.RamBank, s.RamEnabled, s.BankingMode);
    }

    // Fills RAM bank n with n.
    private static void NumberRamBanks(ICartridge cart)
    {
        var battery = (IBatteryBackedCartridge)cart;
        var data = new byte[battery.ExportRam().Length];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i / 0x2000);
        }

        battery.ImportRam(data);
    }

    // Unmapped, both ROM windows show the last 32 KiB, so the console boots the menu.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void StartsUnmappedShowingTheMenuWhateverTheRegistersHold(byte romCode)
    {
        var cart = Load(0x0B, romCode, 0);
        var banks = 2 << romCode;
        foreach (var value in new byte[] { 0x00, 0x01, 0x3F, 0x7F, 0xFF })
        {
            cart.Write(0x2000, value);
            cart.Write(0x4000, value);
            cart.Write(0x6000, value);
            cart.Write(0x1FFF, (byte)(value & ~0x40)); // All but the mapping enable.
            Assert.Equal(banks - 2, Bank(cart, 0x0000));
            Assert.Equal(banks - 1, Bank(cart, 0x4000));
            Assert.Equal((banks - 1) & 0xFF, cart.Read(0x7FFF));
            Assert.Equal(0x0B, cart.Read(0x0147));
            Assert.Equal((banks - 2, banks - 1), (((IBankedCartridge)cart).LowerRomBank, ((IBankedCartridge)cart).UpperRomBank));
        }
    }

    // ROM bank low $10 with mask $30 maps a 128 KiB game at banks 10h-17h.
    [Fact]
    public void MappingSelectsTheGameAndTheZeroRuleSeesOnlyUnlockedBits()
    {
        var cart = Load(0x0B, 4, 0);
        cart.Write(0x2000, 0x10);
        cart.Write(0x6000, 0x30);
        cart.Write(0x0000, 0x40);
        Assert.Equal(0x10, Bank(cart, 0x0000));
        Assert.Equal(0x11, Bank(cart, 0x4000));
        for (var value = 0; value < 0x80; value++)
        {
            cart.Write(value % 2 == 0 ? (ushort)0x2000 : (ushort)0x3FFF, (byte)value);
            Assert.Equal((value & 7) == 0 ? 0x11 : 0x10 | (value & 7), Bank(cart, 0x4000));
            Assert.Equal(0x10, Bank(cart, 0x0000));
        }
    }

    [Fact]
    public void MasksLockLowBankBitsAlsoWhileUnmapped()
    {
        var cart = Load(0x0B, 4, 0);
        cart.Write(0x2000, 0x08);
        cart.Write(0x6000, 0x30);
        cart.Write(0x2000, 0x17); // Low = 0Fh.
        cart.Write(0x0000, 0x40);
        Assert.Equal(0x08, Bank(cart, 0x0000));
        Assert.Equal(0x0F, Bank(cart, 0x4000));

        cart = Load(0x0B, 4, 0);
        cart.Write(0x2000, 0x07);
        cart.Write(0x6000, 0x3E);
        cart.Write(0x0000, 0x40);
        Assert.Equal(6, Bank(cart, 0x0000));
        Assert.Equal(7, Bank(cart, 0x4000));
        Assert.Equal(0x3C, ((IStatefulCartridge)cart).CaptureState().Mode);
        cart.Write(0x2000, 0x18);
        Assert.Equal(7, Bank(cart, 0x4000)); // Game bank 0 maps 1.
        Assert.Equal(6, Bank(cart, 0x0000));
    }

    // Once mapped, the game sees an MBC1 and only a power cycle returns to the menu.
    [Fact]
    public void ExtraBitsAreWritableOnlyWhileUnmapped()
    {
        var cart = Load(0x0B, 8, 0); // 8 MiB.
        cart.Write(0x2000, 0x45);
        cart.Write(0x4000, 0x30);
        cart.Write(0x0000, 0x40); // Mid 2, low 5, high 3.
        Assert.Equal(0x180 | 0x40, Bank(cart, 0x0000));
        Assert.Equal(0x180 | 0x40 | 5, Bank(cart, 0x4000));
        cart.Write(0x2000, 0x06);
        Assert.Equal(0x1C6, Bank(cart, 0x4000)); // Mid stays 2.
        cart.Write(0x4000, 0x0F);
        Assert.Equal(0x1C6, Bank(cart, 0x4000)); // High stays 3.
        cart.Write(0x6000, 0x7E); // Mask and multiplex stay.
        cart.Write(0x2000, 0x00);
        Assert.Equal(0x1C1, Bank(cart, 0x4000));
        Assert.Equal(0x1C0, Bank(cart, 0x0000));
        cart.Write(0x0000, 0x00);
        Assert.Equal(0x1C0, Bank(cart, 0x0000));
        Assert.Equal(0x1C1, Bank(cart, 0x4000));
    }

    // 4000 bit 6 locks the MBC1 mode, mapped or not.
    [Fact]
    public void ModeLockFreezesTheMbc1Mode()
    {
        var cart = Load();
        var banked = (IBankedCartridge)cart;
        cart.Write(0x6000, 0x01);
        Assert.Equal(1, banked.BankingMode);
        cart.Write(0x4000, 0x40);
        cart.Write(0x6000, 0x00);
        Assert.Equal(1, banked.BankingMode);
        cart.Write(0x0000, 0x40);
        cart.Write(0x6000, 0x00);
        Assert.Equal(1, banked.BankingMode);

        cart = Load();
        banked = (IBankedCartridge)cart;
        cart.Write(0x0000, 0x40);
        cart.Write(0x6000, 0x01);
        Assert.Equal(1, banked.BankingMode);
        cart.Write(0x7FFF, 0xFE);
        Assert.Equal(0, banked.BankingMode);
    }

    // The RAM bank is high:low, and in mode 0 its unlocked low bits count as 0.
    [Fact]
    public void RamBankFollowsTheModeAndTheRamMask()
    {
        var cart = Load(0x0D, 4, 4); // 128 KiB: 16 banks.
        NumberRamBanks(cart);
        cart.Write(0x4000, 0x0A);
        cart.Write(0x0000, 0x20); // Locks RAM bank low bit 1.
        cart.Write(0x4000, 0x09); // Low = 3.
        cart.Write(0x0000, 0x6A); // Map, RAM on.
        Assert.Equal(10, cart.Read(0xA000));
        Assert.Equal(10, ((IBankedCartridge)cart).RamBank);
        cart.Write(0x6000, 0x01);
        Assert.Equal(11, cart.Read(0xBFFF));
        cart.Write(0x4000, 0x00);
        Assert.Equal(10, cart.Read(0xA000)); // Only bit 0 changes.
        cart.Write(0x4000, 0x0F);
        Assert.Equal(11, cart.Read(0xA000)); // High stays 2.
        cart.Write(0xA000, 99);
        Assert.Equal(99, ((IBatteryBackedCartridge)cart).ExportRam()[11 * 0x2000]);
        cart.Write(0x0000, 0x00);
        Assert.Equal(0xFF, cart.Read(0xA000));
        cart.Write(0xA000, 1); // Disabled: ignored.
        cart.Write(0x0000, 0x0A);
        Assert.Equal(99, cart.Read(0xA000));

        cart = Load(); // 32 KiB: banks wrap.
        NumberRamBanks(cart);
        cart.Write(0x4000, 0x05);
        cart.Write(0x6000, 0x01);
        cart.Write(0x0000, 0x4A);
        Assert.Equal(1, cart.Read(0xA000));
    }

    // Multiplexed, RAM bank low selects ROM bits 5-6, as on an MBC1 with a large ROM.
    [Fact]
    public void MultiplexSwapsRamBankLowWithRomBankMid()
    {
        var cart = Load(0x0D, 7); // 4 MiB, 32 KiB RAM.
        NumberRamBanks(cart);
        cart.Write(0x2000, 0x20);
        cart.Write(0x6000, 0x40);
        cart.Write(0x4000, 0x10);
        cart.Write(0x0000, 0x4A); // Mid 1, high 1.
        Assert.Equal(0x80, Bank(cart, 0x0000));
        Assert.Equal(0x81, Bank(cart, 0x4000));
        Assert.Equal(1, cart.Read(0xA000));
        cart.Write(0x4000, 0x02);
        Assert.Equal(0x80, Bank(cart, 0x0000));
        Assert.Equal(0xC1, Bank(cart, 0x4000));
        Assert.Equal(1, cart.Read(0xA000));
        cart.Write(0x6000, 0x01);
        Assert.Equal(0xC0, Bank(cart, 0x0000));
        Assert.Equal(0xC1, Bank(cart, 0x4000));
        Assert.Equal(1, cart.Read(0xA000));
        cart.Write(0x2000, 0x20);
        Assert.Equal(0xC1, Bank(cart, 0x4000)); // Low 0 maps 1.
        cart.Write(0x2000, 0x03);
        Assert.Equal(0xC3, Bank(cart, 0x4000));

        // The RAM mask still locks RAM bank low, halving the game's ROM.
        cart = Load(0x0B, 7, 0);
        cart.Write(0x4000, 0x02);
        cart.Write(0x6000, 0x40);
        cart.Write(0x0000, 0x60);
        Assert.Equal(0x40, Bank(cart, 0x0000));
        Assert.Equal(0x41, Bank(cart, 0x4000));
        cart.Write(0x4000, 0x01);
        Assert.Equal(0x40, Bank(cart, 0x0000));
        Assert.Equal(0x61, Bank(cart, 0x4000));
        cart.Write(0x6000, 0x01);
        Assert.Equal(0x60, Bank(cart, 0x0000));
    }

    // Unmapped, RAM is banked by the same rules as when mapped.
    [Fact]
    public void RamIsReachableWhileUnmapped()
    {
        var cart = Load(0x0C);
        cart.Write(0xA000, 1);
        Assert.Equal(0xFF, cart.Read(0xA000)); // Disabled at power-on.
        cart.Write(0x0000, 0x0A);
        cart.Write(0xA000, 42);
        cart.Write(0x6000, 0x01);
        cart.Write(0x4000, 0x02);
        cart.Write(0xA000, 43);
        Assert.Equal((30, 31), (Bank(cart, 0x0000), Bank(cart, 0x4000))); // Still the menu.
        cart.Write(0x6000, 0x00);
        Assert.Equal(42, cart.Read(0xA000));
        cart.Write(0x0000, 0x4A);
        Assert.Equal(42, cart.Read(0xA000));
        cart.Write(0x6000, 0x01);
        Assert.Equal(43, cart.Read(0xA000));
    }

    // Reset clears every register and disables RAM.
    [Fact]
    public void ConsoleResetReturnsToTheMenuAndKeepsRam()
    {
        var cart = Load(0x0C);
        cart.Write(0x0000, 0x0A);
        cart.Write(0xA000, 42);
        cart.Write(0x2000, 0x08);
        cart.Write(0x6000, 0x31);
        cart.Write(0x4000, 0x40);
        cart.Write(0x0000, 0x7A);
        Assert.Equal((8, 9), (Bank(cart, 0x0000), Bank(cart, 0x4000)));
        var system = new GameBoySystem();
        system.InsertCartridge(cart);
        system.Reset();
        Assert.Equal((30, 31), (Bank(cart, 0x0000), Bank(cart, 0x4000)));
        Assert.Equal(0xFF, cart.Read(0xA000));
        cart.Write(0x2000, 0x1F);
        cart.Write(0x6000, 0x00);
        cart.Write(0x0000, 0x4A); // No mask, no mode lock any more.
        Assert.Equal((0, 31), (Bank(cart, 0x0000), Bank(cart, 0x4000)));
        Assert.Equal(0, ((IBankedCartridge)cart).BankingMode);
        Assert.Equal(42, cart.Read(0xA000));
    }

    [Theory]
    [InlineData(0x0B, 0, 0, 0, false)] // Only a menu.
    [InlineData(0x0B, 4, 0, 0, false)] // 512 KiB without RAM.
    [InlineData(0x0C, 3, 2, 8192, false)]
    [InlineData(0x0D, 5, 3, 32768, true)]
    [InlineData(0x0D, 6, 5, 65536, true)]
    [InlineData(0x0D, 8, 4, 131072, true)] // The largest.
    public void TypeSizesAndBatteryComeFromTheMenuHeader(byte type, byte romCode, byte ramCode, int ramBytes, bool battery)
    {
        var image = Create(type, romCode, ramCode);
        var loaded = CartridgeLoader.Load(image);
        Array.Fill(image, (byte)0xFF);
        Assert.Equal(new CartridgeInfo("MMM01 MENU", type, 0x8000 << romCode, ramBytes, battery), loaded.Info);
        Assert.Empty(loaded.Warnings);
        Assert.Equal(battery, loaded.Cartridge is IBatteryBackedCartridge);
        Assert.Equal(type, ((IStatefulCartridge)loaded.Cartridge).TypeCode);
        Assert.Equal((2 << romCode) - 1, Bank(loaded.Cartridge, 0x4000));
    }

    [Theory]
    [InlineData(0x0C)]
    [InlineData(0x0D)]
    public void TypesNamingRamWithSizeZeroRunWithoutRam(byte type)
    {
        var loaded = CartridgeLoader.Load(Create(type, 4, 0));
        Assert.Contains("no RAM is fitted", Assert.Single(loaded.Warnings), StringComparison.Ordinal);
        Assert.Equal(0, loaded.Info.RamSizeBytes);
        Assert.False(loaded.Info.HasBattery);
        Assert.False(loaded.Cartridge is IBatteryBackedCartridge);
        Assert.Equal(0x0B, ((IStatefulCartridge)loaded.Cartridge).TypeCode);
        loaded.Cartridge.Write(0, 0x0A);
        loaded.Cartridge.Write(0xA000, 1);
        Assert.Equal(0xFF, loaded.Cartridge.Read(0xA000));
    }

    [Theory]
    [InlineData(0x0B, 4, 2, 4)] // Type 0B has no RAM.
    [InlineData(0x0D, 4, 1, 4)] // 2 KiB RAM.
    [InlineData(0x0D, 9, 3, 9)] // 16 MiB.
    [InlineData(0x0D, 5, 3, 4)] // Image smaller than declared.
    public void UnsupportedConfigurationsAreRejected(byte type, byte romCode, byte ramCode, byte imageRomCode)
    {
        var image = Create(type, imageRomCode, ramCode);
        image[image.Length - 0x8000 + 0x148] = romCode;
        Assert.Throws<CartridgeLoadException>(() => CartridgeLoader.Load(image));
    }

    // Only the menu's header counts, so a second logo at bank 10h does not make it an MBC1M.
    [Fact]
    public void LoaderReadsTheMenuHeaderBeforeTheMulticartCheck()
    {
        var image = Create(0x0D, 5);
        image.AsSpan(0x104, 48).CopyTo(image.AsSpan(0x40104));
        image[0x143] = 0xC0;
        image[0x14D] ^= 0xFF;
        var loaded = CartridgeLoader.Load(image);
        Assert.Equal(0x0D, loaded.Info.TypeCode);
        Assert.Empty(loaded.Warnings);
        var menu = image.Length - 0x8000;
        image[menu + 0x14D] ^= 0xFF;
        Assert.Contains("checksum", Assert.Single(CartridgeLoader.Load(image).Warnings), StringComparison.Ordinal);
        image[menu + 0x143] = 0xC0;
        Assert.Throws<CartridgeLoadException>(() => CartridgeLoader.Load(image));
    }

    // Without its logo the menu header is ignored; an image with the menu first is rejected.
    [Fact]
    public void MenuHeaderNeedsTheLogoAndTheLastThirtyTwoKiB()
    {
        var image = Create();
        var menu = image.Length - 0x8000;
        byte[] menuFirst = [.. image[menu..], .. image[..menu]];
        Assert.Contains("last 32 KiB", Assert.Throws<CartridgeLoadException>(() => CartridgeLoader.Load(menuFirst)).Message, StringComparison.Ordinal);
        image[menu + 0x104] ^= 0xFF;
        var loaded = CartridgeLoader.Load(image);
        Assert.Equal(new CartridgeInfo("P01 TEST", 0x01, 0x80000, 0, false), loaded.Info);
    }

    [Fact]
    public void BatteryImportExportCopiesEveryBankWithoutChangingController()
    {
        var cart = (IBatteryBackedCartridge)Load(0x0D, 4, 4);
        var data = new byte[131072];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i / 8192) + 10);
        }

        cart.Write(0, 0x0A);
        cart.Write(0x6000, 1);
        cart.Write(0x4000, 0x0F);
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
        Assert.False(Load(0x0C, 4, 4) is IBatteryBackedCartridge); // Type 0C RAM has no battery.
    }

    [Fact]
    public void StateKeepsTheFourRegistersAndEveryRamBank()
    {
        var cart = CartridgeLoader.Load(Create(0x0D, 8, 4, 0x18, 0xFE)).Cartridge;
        var system = new GameBoySystem();
        system.InsertCartridge(cart);
        cart.Write(0, 0x0A);
        cart.Write(0x6000, 1);
        for (var bank = 0; bank < 16; bank++)
        {
            cart.Write(0x4000, (byte)bank);
            cart.Write(0xBFFF, (byte)(bank + 31));
        }
        cart.Write(0x2000, 0x8B);
        cart.Write(0x6000, 0xB3);
        cart.Write(0x4000, 0xE6);
        cart.Write(0x0000, 0xFA);
        var saved = system.CaptureState();
        Assert.Equal((0x0B, 0x70, 0x31, 0x66, true), (saved.Cartridge.Bank, saved.Cartridge.Upper, saved.Cartridge.Mode,
            saved.Cartridge.RamBank, saved.Cartridge.RamEnabled));
        Assert.Equal((0x108, 0x10B, 6, true, (byte)1), Banks(system));
        cart.Write(0xBFFF, 255);
        system.Reset();
        system.RunForTCycles(100_000); // The menu's JR -2.
        cart.Write(0x2000, 0x60);
        cart.Write(0x0000, 0x40);
        system.RestoreState(GameBoyState.Deserialize(saved.Serialize()));
        Assert.Equal((0x108, 0x10B, 6, true, (byte)1), Banks(system));
        Assert.Equal(0x10B, Bank(cart, 0x4000));
        Assert.Equal(37, cart.Read(0xBFFF));
        cart.Write(0x2000, 0x67);
        cart.Write(0x6000, 0); // Still mapped, masked and locked.
        Assert.Equal((0x108, 0x10F), (Bank(cart, 0x0000), Bank(cart, 0x4000)));
        Assert.Equal(1, ((IBankedCartridge)cart).BankingMode);
        foreach (var invalid in new[]
        {
            saved with { Cartridge = saved.Cartridge with { Bank = 0x80 } },
            saved with { Cartridge = saved.Cartridge with { RamBank = 0x80 } },
            saved with { Cartridge = saved.Cartridge with { Mode = 0x33 } },
            saved with { Cartridge = saved.Cartridge with { Mode = 0xB1 } },
            saved with { Cartridge = saved.Cartridge with { Upper = 0x7A } },
            saved with { Cartridge = saved.Cartridge with { Upper = 0xF0 } },
            saved with { Cartridge = saved.Cartridge with { Ram = new byte[32768] } },
            saved with { Cartridge = saved.Cartridge with { Rtc = new RtcState(default, default, 0, false) } },
            saved with { CartridgeType = 0x0C }
        })
        {
            Assert.Throws<ArgumentException>(() => system.RestoreState(invalid));
            Assert.Equal(0x10F, system.GetDebugSnapshot().RomBank1); // The rejected state changed nothing.
        }
    }

    // The menu maps game 0, and the CPU's next fetch, at 0155, comes from the game's bank 0.
    [Fact]
    public void CpuBootsTheMenuAndContinuesInTheMappedGame()
    {
        var image = Create(0x0B, 4, 0, 0x3E, 0x40, 0xEA, 0x00, 0x00);
        new byte[] { 0x3E, 42, 0xEA, 0, 0xC0, 0x76 }.CopyTo(image, 0x155);
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        var menu = system.GetDebugSnapshot();
        Assert.Equal((30, 31), (menu.RomBank0, menu.RomBank1));
        system.RunForTCycles(200);
        Assert.True(system.IsHalted);
        var memory = new byte[1];
        system.CopyMemory(0xC000, memory);
        Assert.Equal(42, memory[0]);
        var game = system.GetDebugSnapshot();
        Assert.Equal((0, 1), (game.RomBank0, game.RomBank1));
    }
}
