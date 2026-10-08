namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;

[Trait("Category", "Unit")]
public sealed class CartridgeLoaderTests
{
    [Fact]
    public void ValidRomHasMetadataAndOwnsItsBytes()
    {
        var image = TestRom.Create(">B"u8.ToArray());
        image[0] = 0x12;
        image[0x7FFF] = 0x34;

        var result = CartridgeLoader.Load(image);
        Array.Fill(image, (byte)0xFF);

        Assert.Equal(new CartridgeInfo("P01 TEST", 0, 32768, 0, false), result.Info);
        Assert.Empty(result.Warnings);
        Assert.Equal(0x12, result.Cartridge.Read(0));
        Assert.Equal(0x34, result.Cartridge.Read(0x7FFF));
        Assert.Equal(0x3E, result.Cartridge.Read(TestRom.ProgramStart));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x14F)]
    [InlineData(0x150)]
    [InlineData(0x7FFF)]
    [InlineData(0x8001)]
    [InlineData(0x10000)]
    public void MissingTruncatedAndPaddedImagesAreRejected(int length)
    {
        var image = TestRom.Create();
        Array.Resize(ref image, length);
        Assert.Throws<CartridgeLoadException>(() => CartridgeLoader.Load(image));
    }

    [Theory]
    [InlineData(0x147, 0x20)] // MBC6.
    [InlineData(0x147, 0xFD)] // TAMA5.
    [InlineData(0x148, 0x01)] // 64 KiB ROM Only.
    [InlineData(0x148, 0xFF)]
    [InlineData(0x149, 0x02)] // RAM on ROM Only.
    [InlineData(0x143, 0xC0)] // CGB-only.
    public void UnsupportedCartridgeConfigurationsAreRejected(int address, byte value)
    {
        var image = TestRom.Create();
        image[address] = value;
        TestRom.UpdateHeaderChecksum(image);
        Assert.Throws<CartridgeLoadException>(() => CartridgeLoader.Load(image));
    }

    [Fact]
    public void DmgCompatibleCgbFlagIsAcceptedAndExcludedFromTitle()
    {
        var image = TestRom.Create();
        "ABCDEFGHIJKLMNO"u8.CopyTo(image.AsSpan(0x134));
        image[0x143] = 0x80;
        TestRom.UpdateHeaderChecksum(image);
        var result = CartridgeLoader.Load(image);
        Assert.Equal("ABCDEFGHIJKLMNO", result.Info.Title);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData(0x104, "logo")]
    [InlineData(0x14D, "checksum")]
    public void LogoAndHeaderChecksumMismatchAreWarnings(int address, string diagnostic)
    {
        var image = TestRom.Create(0x00);
        image[address] ^= 0xFF;
        var result = CartridgeLoader.Load(image);
        Assert.Contains(diagnostic, Assert.Single(result.Warnings), StringComparison.Ordinal);
        Assert.Equal(0x00, result.Cartridge.Read(TestRom.ProgramStart));
    }

    [Fact]
    public void RomOnlyIgnoresWritesAndHasNoExternalRam()
    {
        var cartridge = CartridgeLoader.Load(TestRom.Create()).Cartridge;
        cartridge.Write(0x100, 0xFF);
        cartridge.Write(0x7FFF, 0xFF);
        cartridge.Write(0xA000, 0x12);
        cartridge.Write(0xBFFF, 0x34);
        cartridge.ResetController();
        Assert.Equal(0xC3, cartridge.Read(0x100));
        Assert.Equal(0x00, cartridge.Read(0x7FFF));
        Assert.Equal(0xFF, cartridge.Read(0xA000));
        Assert.Equal(0xFF, cartridge.Read(0xBFFF));
    }

    [Theory]
    [InlineData(0x8000)]
    [InlineData(0x9FFF)]
    [InlineData(0xC000)]
    [InlineData(0xFFFF)]
    public void CartridgeRejectsAddressesOutsideItsWindows(ushort address)
    {
        var cartridge = CartridgeLoader.Load(TestRom.Create()).Cartridge;
        Assert.Throws<ArgumentOutOfRangeException>(() => cartridge.Read(address));
        Assert.Throws<ArgumentOutOfRangeException>(() => cartridge.Write(address, 0));
    }
}
