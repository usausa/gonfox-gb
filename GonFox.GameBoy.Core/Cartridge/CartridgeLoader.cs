namespace GonFox.GameBoy.Core.Cartridge;

using System.Text;

public static class CartridgeLoader
{
    public const int MaxRomSizeBytes = 8 * 1024 * 1024; // MBC5's limit.
    private static ReadOnlySpan<byte> NintendoLogo =>
    [
        0xCE, 0xED, 0x66, 0x66, 0xCC, 0x0D, 0x00, 0x0B,
        0x03, 0x73, 0x00, 0x83, 0x00, 0x0C, 0x00, 0x0D,
        0x00, 0x08, 0x11, 0x1F, 0x88, 0x89, 0x00, 0x0E,
        0xDC, 0xCC, 0x6E, 0xE6, 0xDD, 0xDD, 0xD9, 0x99,
        0xBB, 0xBB, 0x67, 0x63, 0x6E, 0x0E, 0xEC, 0xCC,
        0xDD, 0xDC, 0x99, 0x9F, 0xBB, 0xB9, 0x33, 0x3E
    ];

    public static CartridgeLoadResult Load(ReadOnlySpan<byte> image)
    {
        if (image.Length < 0x150)
        {
            throw new CartridgeLoadException("The ROM is shorter than the 0x150-byte cartridge header.");
        }

        // An MMM01 image keeps the cartridge's header in its last 32 KiB, the menu the console starts in.
        var menu = image.Length - 0x8000;
        var mmm01 = menu >= 0 && image[menu + 0x147] is >= 0x0B and <= 0x0D &&
            (menu == 0 || image.Slice(menu + 0x104, NintendoLogo.Length).SequenceEqual(NintendoLogo));
        var header = mmm01 ? image[menu..] : image;
        if ((header[0x143] & 0xC0) == 0xC0)
        {
            throw new CartridgeLoadException("CGB-only cartridges are not supported by the DMG model.");
        }

        var typeCode = header[0x147];
        bool mbc1 = typeCode is >= 0x01 and <= 0x03, mbc2 = typeCode is 0x05 or 0x06, mbc3 = typeCode is >= 0x0F and <= 0x13,
            mbc5 = typeCode is >= 0x19 and <= 0x1E, rumble = typeCode is >= 0x1C and <= 0x1E, huc1 = typeCode == 0xFF,
            camera = typeCode == 0xFC, huc3 = typeCode == 0xFE;
        if (typeCode is >= 0x0B and <= 0x0D && !mmm01)
        {
            throw new CartridgeLoadException("An MMM01 cartridge boots its menu from the last 32 KiB, so its header with the logo belongs there (Pan Docs); this image has it only at 0100.");
        }

        if (typeCode != 0 && !mbc1 && !mbc2 && !mbc3 && !mbc5 && !mmm01 && !huc1 && !camera && !huc3)
        {
            throw new CartridgeLoadException($"Cartridge type 0x{typeCode:X2} is not implemented. Supported: ROM Only (00), MBC1 (01/02/03), MBC2 (05/06), MMM01 (0B-0D), MBC3 (0F-13), MBC5 (19-1E), Game Boy Camera (FC), HuC3 (FE), HuC1 (FF).");
        }

        byte romCode = header[0x148], ramCode = header[0x149];
        if (camera && (romCode > 5 || ramCode is < 2 or > 5))
        {
            throw new CartridgeLoadException("Supported Game Boy Camera sizes are 32 KiB through 1 MiB of ROM and 8, 32, 64 or 128 KiB of RAM.");
        }

        if (mbc5 && romCode > 8)
        {
            throw new CartridgeLoadException("Supported MBC5 ROM sizes are powers of two from 32 KiB through 8 MiB.");
        }

        if (mbc2 && romCode > 3)
        {
            throw new CartridgeLoadException("Supported MBC2 ROM sizes are powers of two from 32 KiB through 256 KiB.");
        }

        if (mbc3 && romCode > 7)
        {
            throw new CartridgeLoadException("Supported MBC3 ROM sizes are powers of two from 32 KiB through 2 MiB (MBC30: 4 MiB).");
        }

        if (mmm01 && romCode > 8)
        {
            throw new CartridgeLoadException("Supported MMM01 ROM sizes are powers of two from 32 KiB through 8 MiB.");
        }

        if (huc1 && romCode > 5)
        {
            throw new CartridgeLoadException("Supported HuC1 ROM sizes are powers of two from 32 KiB through 1 MiB.");
        }

        if (!mbc5 && !mbc3 && !mmm01 && romCode > 6)
        {
            throw new CartridgeLoadException("Supported ROM sizes are powers of two from 32 KiB through 2 MiB.");
        }

        if (mbc2 && ramCode != 0)
        {
            throw new CartridgeLoadException("MBC2 has built-in RAM and requires RAM size code 0x00.");
        }

        var romSize = 0x8000 << romCode;
        var ramSize = mbc2 ? Mbc2Cartridge.RamSize : ramCode switch
        {
            0 => 0,
            2 => 0x2000,
            3 => 0x8000,
            4 when (mbc5 && !rumble) || mmm01 || camera => 0x20000,
            5 when mbc5 || mbc3 || mmm01 || camera => 0x10000,
            _ => throw new CartridgeLoadException(rumble ? "Supported RAM sizes with the rumble motor are 0, 8, 32 and 64 KiB."
                : mbc5 ? "Supported MBC5 RAM sizes are 0, 8, 32, 64 and 128 KiB."
                : mmm01 ? "Supported MMM01 RAM sizes are 0, 8, 32, 64 and 128 KiB."
                : mbc3 ? "Supported MBC3 RAM sizes are 0, 8 and 32 KiB (MBC30: 64 KiB)." : "Supported RAM sizes are 0, 8 KiB and 32 KiB.")
        };
        if (typeCode == 0 && (romCode != 0 || ramCode != 0))
        {
            throw new CartridgeLoadException("ROM Only requires ROM size code 0x00 (32 KiB) and RAM size code 0x00 (no RAM).");
        }
        if (typeCode is 0x01 or 0x0B or 0x0F or 0x11 or 0x19 or 0x1C && ramSize != 0)
        {
            throw new CartridgeLoadException($"Cartridge type {typeCode:X2} has no RAM, but its header declares {ramSize / 1024} KiB.");
        }

        List<string> warnings = [];
        if (typeCode is 0x02 or 0x03 or 0x0C or 0x0D or 0x10 or 0x12 or 0x13 or 0x1A or 0x1B or 0x1D or 0x1E or 0xFF && ramSize == 0)
        {
            warnings.Add($"Cartridge type {typeCode:X2} names RAM, but the RAM size code is 0: no RAM is fitted.");
        }

        var battery = typeCode is 0x06 or 0x0F or 0x10 or 0xFC or 0xFE || (typeCode is 0x03 or 0x0D or 0x13 or 0x1B or 0x1E or 0xFF && ramSize != 0);
        if (mbc1 && romSize > 0x80000 && ramSize > 0x2000)
        {
            throw new CartridgeLoadException("Standard MBC1 supports either up to 512 KiB ROM + 32 KiB RAM, or up to 2 MiB ROM + 8 KiB RAM.");
        }

        if (image.Length != romSize)
        {
            throw new CartridgeLoadException($"The header declares {romSize} ROM bytes, but the image contains {image.Length} bytes.");
        }
        var mbc30 = mbc3 && (romSize > 0x200000 || ramSize > 0x8000);
        var multicart = mbc1 && romSize == 0x100000 && image.Slice(0x40104, NintendoLogo.Length).SequenceEqual(NintendoLogo);
        if (multicart)
        {
            warnings.Add("MBC1M multicart wiring (a second header with the logo at bank 10h).");
        }

        if (!header.Slice(0x104, NintendoLogo.Length).SequenceEqual(NintendoLogo))
        {
            warnings.Add("The Nintendo logo in the cartridge header does not match.");
        }

        byte checksum = 0;
        for (var address = 0x134; address <= 0x14C; address++)
        {
            checksum = unchecked((byte)(checksum - header[address] - 1));
        }

        if (checksum != header[0x14D])
        {
            warnings.Add($"Header checksum mismatch: expected 0x{checksum:X2}, found 0x{header[0x14D]:X2}.");
        }

        var titleLength = (header[0x143] & 0x80) != 0 ? 15 : 16;
        var titleBytes = header.Slice(0x134, titleLength);
        var terminator = titleBytes.IndexOf((byte)0);
        if (terminator >= 0)
        {
            titleBytes = titleBytes[..terminator];
        }

        var info = new CartridgeInfo(
            Encoding.ASCII.GetString(titleBytes).TrimEnd(' '),
            typeCode, image.Length, ramSize, HasBattery: battery, HasRumble: rumble);

        ICartridge cartridge = typeCode switch
        {
            0 => new RomOnlyCartridge(image),
            1 or 2 or 3 => battery ? new BatteryMbc1Cartridge(image, ramSize, multicart) : new Mbc1Cartridge(image, ramSize, multicart),
            5 => new Mbc2Cartridge(image),
            6 => new BatteryMbc2Cartridge(image),
            0x0B or 0x0C or 0x0D => battery ? new BatteryMmm01Cartridge(image, ramSize) : new Mmm01Cartridge(image, ramSize),
            0x0F or 0x10 => new ClockMbc3Cartridge(image, ramSize, mbc30),
            0x11 or 0x12 or 0x13 => battery ? new BatteryMbc3Cartridge(image, ramSize, mbc30) : new Mbc3Cartridge(image, ramSize, mbc30),
            0xFC => new CameraCartridge(image, ramSize),
            0xFE => new Huc3Cartridge(image, ramSize),
            0xFF => battery ? new BatteryHuC1Cartridge(image, ramSize) : new HuC1Cartridge(image, ramSize),
            _ => battery ? new BatteryMbc5Cartridge(image, ramSize, rumble) : new Mbc5Cartridge(image, ramSize, rumble)
        };
        return new CartridgeLoadResult(cartridge, info, warnings.AsReadOnly());
    }
}
