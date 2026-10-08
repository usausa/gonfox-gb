namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;

[Trait("Category", "Unit")]
public sealed class Mbc1Tests
{
    private static ICartridge Load(byte type = 3, byte romCode = 1, byte ramCode = 3) =>
        CartridgeLoader.Load(TestRom.CreateMbc1(type, romCode, ramCode)).Cartridge;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void RomWindowsUseConnectedAddressLinesInBothModes(byte romCode)
    {
        var cart = Load(1, romCode, 0);
        var bankCount = 2 << romCode;
        for (var mode = 0; mode < 2; mode++)
        {
            for (var bank = 0; bank < 128; bank++)
            {
                cart.Write(0x6000, (byte)mode);
                cart.Write(0x4000, (byte)(bank / 32));
                cart.Write(0x2000, (byte)(bank % 32));
                var lower = mode == 0 ? 0 : (bank / 32 * 32) % bankCount;
                var upper = (bank % 32 == 0 ? bank + 1 : bank) % bankCount;
                Assert.Equal(lower, cart.Read(0));
                Assert.Equal(lower, cart.Read(0x3FFF));
                Assert.Equal(upper, cart.Read(0x4000));
                Assert.Equal(upper, cart.Read(0x7FFF));
            }
        }
    }

    [Fact]
    public void SmallRomCanSelectBankZeroThroughAnUnconnectedNonzeroBit()
    {
        var cart = Load(1, 3, 0); // 256 KiB: bit 4 unconnected.
        cart.Write(0x2000, 0);
        Assert.Equal(1, cart.Read(0x4000));
        cart.Write(0x2000, 0x10);
        Assert.Equal(0, cart.Read(0x4000));
        cart.Write(0x2000, 0xE0);
        Assert.Equal(1, cart.Read(0x4000));
    }

    [Fact]
    public void RegisterRangesAndUnusedBitsAreDecoded()
    {
        var cart = Load(1, 6, 0);
        cart.Write(0x3FFF, 0xE2);
        cart.Write(0x5FFF, 0xFE);
        cart.Write(0x7FFF, 0xFF);
        Assert.Equal(64, cart.Read(0));
        Assert.Equal(66, cart.Read(0x4000));
        cart.Write(0x6000, 0xFE);
        Assert.Equal(0, cart.Read(0));
        Assert.Equal(66, cart.Read(0x4000));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void RamEnableModesBanksBoundariesAndConsoleResetPreserveRam(byte type)
    {
        var cart = Load(type);
        Assert.Equal(0xFF, cart.Read(0xA000));
        cart.Write(0xA000, 99);
        cart.Write(0x1FFF, 0xFA);
        Assert.Equal(0, cart.Read(0xA000));
        cart.Write(0x6000, 1);
        for (byte bank = 0; bank < 4; bank++)
        {
            cart.Write(0x4000, bank);
            cart.Write(0xA000, (byte)(10 + bank));
            cart.Write(0xBFFF, (byte)(20 + bank));
        }
        cart.Write(0x6000, 0);
        Assert.Equal(10, cart.Read(0xA000));
        cart.Write(0xA000, 42);
        cart.Write(0, 0);
        cart.Write(0xA000, 99);
        Assert.Equal(0xFF, cart.Read(0xA000));
        cart.Write(0, 0x0A);
        Assert.Equal(42, cart.Read(0xA000));
        cart.Write(0x6000, 1);
        cart.Write(0x2000, 2);
        var system = new GameBoySystem();
        system.InsertCartridge(cart);
        system.Reset();
        Assert.Equal(1, cart.Read(0x4000));
        Assert.Equal(0xFF, cart.Read(0xA000));
        cart.Write(0, 0x0A);
        Assert.Equal(42, cart.Read(0xA000));
        cart.Write(0x6000, 1);
        for (byte bank = 1; bank < 4; bank++)
        {
            cart.Write(0x4000, bank);
            Assert.Equal(10 + bank, cart.Read(0xA000));
            Assert.Equal(20 + bank, cart.Read(0xBFFF));
        }
    }

    [Fact]
    public void EightKiBRamIgnoresUpperBankInLargeRomWiring()
    {
        var cart = Load(3, 6, 2);
        cart.Write(0, 10);
        cart.Write(0xA000, 42);
        cart.Write(0x6000, 1);
        cart.Write(0x4000, 3);
        Assert.Equal(42, cart.Read(0xA000));
        Assert.Equal(96, cart.Read(0));
        Assert.Equal(97, cart.Read(0x4000));
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

        cart.Write(0, 10);
        cart.Write(0x6000, 1);
        cart.Write(0x4000, 2);
        cart.ImportRam(data);
        Assert.Equal(12, cart.Read(0xA000));
        Array.Fill(data, (byte)99);
        var exported = cart.ExportRam();
        Assert.Equal(10, exported[0]);
        Assert.Equal(13, exported[^1]);
        Array.Fill(exported, (byte)99);
        Assert.Equal(12, cart.Read(0xA000));
        var before = cart.ExportRam();
        Assert.Throws<ArgumentException>(() => cart.ImportRam(new byte[32767]));
        Assert.Throws<ArgumentException>(() => cart.ImportRam(new byte[32769]));
        Assert.Equal(before, cart.ExportRam());
        Assert.Equal(12, cart.Read(0xA000));
    }

    [Theory]
    [InlineData(1, 0, false)]
    [InlineData(2, 2, false)]
    [InlineData(3, 2, true)]
    [InlineData(3, 3, true)]
    public void OnlyBatteryTypeExposesPersistenceAndLoaderOwnsRom(byte type, byte ramCode, bool battery)
    {
        var image = TestRom.CreateMbc1(type, 1, ramCode);
        var loaded = CartridgeLoader.Load(image);
        Array.Fill(image, (byte)0xFF);
        Assert.Equal(battery, loaded.Info.HasBattery);
        Assert.Equal(battery, loaded.Cartridge is IBatteryBackedCartridge);
        Assert.Equal(ramCode == 0 ? 0 : ramCode == 2 ? 8192 : 32768, loaded.Info.RamSizeBytes);
        Assert.Equal(1, loaded.Cartridge.Read(0x4000));
    }

    [Theory]
    [InlineData(1, 1, 2)]
    [InlineData(3, 5, 3)]
    [InlineData(3, 6, 3)]
    [InlineData(3, 1, 1)]
    [InlineData(3, 1, 4)]
    [InlineData(1, 7, 0)]
    public void UnsupportedCapacityAndWiringCombinationsAreRejected(byte type, byte romCode, byte ramCode)
    {
        Assert.Throws<CartridgeLoadException>(() => CartridgeLoader.Load(TestRom.CreateMbc1(type, romCode, ramCode)));
    }

    // The header's RAM size, not its type, decides whether RAM is fitted.
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void TypesNamingRamWithSizeZeroRunWithoutRam(byte type)
    {
        var loaded = CartridgeLoader.Load(TestRom.CreateMbc1(type, 1, 0));
        Assert.Contains(loaded.Warnings, warning => warning.Contains("no RAM is fitted", StringComparison.Ordinal));
        Assert.Equal(type, loaded.Info.TypeCode);
        Assert.Equal(0, loaded.Info.RamSizeBytes);
        Assert.False(loaded.Info.HasBattery);
        Assert.False(loaded.Cartridge is IBatteryBackedCartridge);
        Assert.Equal(1, ((IStatefulCartridge)loaded.Cartridge).TypeCode); // Runs as plain MBC1.
        loaded.Cartridge.Write(0, 0x0A);
        loaded.Cartridge.Write(0xA000, 0x12);
        Assert.Equal(0xFF, loaded.Cartridge.Read(0xA000));
    }

    // MBC1M is detected by a second logo at bank 10h; the upper register picks a 256 KiB game.
    [Fact]
    public void MulticartWiringShiftsTheUpperRegisterAndDropsBitFourOfTheLower()
    {
        var image = TestRom.CreateMbc1(1, 5, 0);
        image.AsSpan(0x104, 48).CopyTo(image.AsSpan(0x40104));
        var loaded = CartridgeLoader.Load(image);
        Assert.Contains(loaded.Warnings, warning => warning.Contains("MBC1M", StringComparison.Ordinal));
        var cart = loaded.Cartridge;
        for (var upper = 0; upper < 4; upper++)
        {
            for (var lower = 0; lower < 32; lower++)
            {
                cart.Write(0x4000, (byte)upper);
                cart.Write(0x2000, (byte)lower);
                cart.Write(0x6000, 0);
                Assert.Equal(0, cart.Read(0));
                Assert.Equal((upper << 4) | ((lower == 0 ? 1 : lower) & 15), cart.Read(0x4000));
                cart.Write(0x6000, 1);
                Assert.Equal(upper << 4, cart.Read(0));
                Assert.Equal((upper << 4) | ((lower == 0 ? 1 : lower) & 15), cart.Read(0x4000));
            }
        }
    }

    [Theory]
    [InlineData(0x8000)]
    [InlineData(0x9FFF)]
    [InlineData(0xC000)]
    [InlineData(0xFFFF)]
    public void InvalidWindowsAreRejected(ushort address)
    {
        var cart = Load();
        Assert.Throws<ArgumentOutOfRangeException>(() => cart.Read(address));
        Assert.Throws<ArgumentOutOfRangeException>(() => cart.Write(address, 0));
    }

    [Fact]
    public void CpuFetchesFromSelectedBankAndDebugCopyUsesSameMapping()
    {
        var image = TestRom.CreateMbc1(1, 1, 0, 0x3E, 2, 0xEA, 0, 0x20, 0xC3, 0, 0x40);
        new byte[] { 0x3E, 42, 0xEA, 0, 0xC0, 0x76 }.CopyTo(image, 0x8000);
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        system.RunForTCycles(200);
        Assert.True(system.IsHalted);
        var memory = new byte[1];
        system.CopyMemory(0xC000, memory);
        Assert.Equal(42, memory[0]);
        var before = system.GetDebugSnapshot();
        system.CopyMemory(0x4000, memory);
        Assert.Equal(0x3E, memory[0]);
        Assert.Equal(before, system.GetDebugSnapshot());
    }
}
