namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;

internal static class TestRom
{
    internal const ushort ProgramStart = 0x0150;

    internal static byte[] Create(params byte[] program)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(program.Length, 0x8000 - ProgramStart);
        var image = new byte[0x8000];

        // Entry point: JP 0150 over the header.
        image[0x100] = 0xC3;
        image[0x101] = 0x50;
        image[0x102] = 0x01;
        Convert.FromHexString(
            "CEED6666CC0D000B03730083000C000D0008111F8889000EDCCC6EE6DDDDD999" +
            "BBBB67636E0EECCCDDDC999FBBB9333E").CopyTo(image, 0x104);
        "P01 TEST"u8.CopyTo(image.AsSpan(0x134));
        UpdateHeaderChecksum(image);
        program.CopyTo(image, ProgramStart);
        return image;
    }

    internal static void UpdateHeaderChecksum(byte[] image)
    {
        // Header checksum computed as a sum, independently of the loader.
        var sum = image.Skip(0x134).Take(0x19).Sum(value => value);
        image[0x14D] = unchecked((byte)(-sum - 0x19));
    }

    internal static byte[] CreateMbc1(byte type = 3, byte romCode = 1, byte ramCode = 3, params byte[] program)
    {
        var image = new byte[0x8000 << romCode];
        for (var bank = 0; bank < image.Length / 0x4000; bank++)
        {
            image.AsSpan(bank * 0x4000, 0x4000).Fill((byte)bank);
        }

        Create().AsSpan(0x100, 0x50).CopyTo(image.AsSpan(0x100));
        image[0x147] = type;
        image[0x148] = romCode;
        image[0x149] = ramCode;
        UpdateHeaderChecksum(image);
        program.CopyTo(image, ProgramStart);
        return image;
    }

    // Builds an MBC3 image; one byte still names each of its up to 256 banks.
    internal static byte[] CreateMbc3(byte type = 0x10, byte romCode = 1, byte ramCode = 3, params byte[] program) =>
        CreateMbc1(type, romCode, ramCode, program);

    // Builds an MBC5 image whose bank byte 1 also holds the high byte of the bank number.
    internal static byte[] CreateMbc5(byte type = 0x1B, byte romCode = 1, byte ramCode = 3, params byte[] program)
    {
        var image = CreateMbc1(type, romCode, ramCode, program);
        for (var bank = 1; bank < image.Length / 0x4000; bank++)
        {
            image[(bank * 0x4000) + 1] = (byte)(bank >> 8);
        }

        return image;
    }

    internal static GameBoySystem Start(params byte[] program)
    {
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(Create(program)).Cartridge);
        system.StepInstruction(); // Entry JP to ProgramStart.
        return system;
    }
}
