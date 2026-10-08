namespace GonFox.GameBoy.Core.Cartridge;

// MMM01 (types 0B-0D): an MBC1 for multi-game cartridges, with lockable game-select bank bits.
internal class Mmm01Cartridge : ICartridge, IStatefulCartridge, IBankedCartridge
{
    private readonly byte[] rom;
    protected byte[] Ram { get; }
    private readonly int romMask;
    private readonly int ramMask;

    // The registers: bits 4-6 of 0000, then 2000, 4000 and 6000.
    private byte control;
    private byte romBank;
    private byte ramBank;
    private byte mode;

    // Offsets of the banks shown, set by every register write.
    private int rom0;
    private int rom1;
    private int ram;
    public string RomSha256 { get; }
    public byte TypeCode => this is IBatteryBackedCartridge ? (byte)0x0D : Ram.Length == 0 ? (byte)0x0B : (byte)0x0C;
    public int LowerRomBank => rom0 / 0x4000;
    public int UpperRomBank => rom1 / 0x4000;
    public int RamBank => ram / 0x2000;
    public bool RamEnabled { get; private set; }
    public byte BankingMode => (byte)(mode & 1);
    private bool Mapped => (control & 0x40) != 0;

    internal Mmm01Cartridge(ReadOnlySpan<byte> image, int ramSize)
    {
        rom = image.ToArray();
        Ram = new byte[ramSize];
        romMask = (image.Length / 0x4000) - 1;
        ramMask = ramSize == 0 ? 0 : (ramSize / 0x2000) - 1;
        RomSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(rom));
        Map();
    }

    // The registers as written: Bank = 2000, RamBank = 4000, Mode = 6000, Upper = bits 4-6 of 0000.
    public CartridgeState CaptureState() => new(romBank, control, mode, RamEnabled, Ram.ToArray(), ramBank);

    public void ValidateState(CartridgeState? state) => StateValidation.Require(state is not null && state.Bank <= 0x7F &&
        (state.Upper & ~0x70) == 0 && (state.Mode & ~0x7D) == 0 && state.RamBank <= 0x7F && StateValidation.HasLength(state.Ram, Ram.Length) &&
        state.HasNoExtras(), "MMM01");

    public void RestoreState(CartridgeState state)
    {
        romBank = state.Bank;
        control = state.Upper;
        mode = state.Mode;
        ramBank = state.RamBank;
        RamEnabled = state.RamEnabled;
        state.Ram.CopyTo(Ram, 0);
        Map();
    }

    public byte Read(ushort address)
    {
        if (address <= 0x3FFF)
        {
            return rom[rom0 + address];
        }

        if (address <= 0x7FFF)
        {
            return rom[rom1 + (address & 0x3FFF)];
        }

        if (address is >= 0xA000 and <= 0xBFFF)
        {
            return RamEnabled && Ram.Length != 0 ? Ram[ram + address - 0xA000] : (byte)0xFF;
        }

        throw new ArgumentOutOfRangeException(nameof(address), "Address is outside the cartridge windows.");
    }

    public void Write(ushort address, byte value)
    {
        switch (address)
        {
            case <= 0x1FFF:
                RamEnabled = (value & 15) == 10;
                if (!Mapped)
                {
                    control = (byte)(value & 0x70);
                }

                break;
            case <= 0x3FFF: romBank = Merge(romBank, value, (Mapped ? 0x60 : 0) | ((mode >> 1) & 0x1E)); break;
            case <= 0x5FFF: ramBank = Merge(ramBank, value, (Mapped ? 0x7C : 0) | ((control >> 4) & 3)); break;
            case <= 0x7FFF: mode = Merge(mode, value, (Mapped ? 0x7C : 0) | ((ramBank & 0x40) >> 6) | 2); break;
            case >= 0xA000 and <= 0xBFFF:
                if (RamEnabled && Ram.Length != 0)
                {
                    Ram[ram + address - 0xA000] = value;
                }

                return;
            default: throw new ArgumentOutOfRangeException(nameof(address), "Address is outside the cartridge windows.");
        }
        Map();
    }

    // The locked bits keep their value; the others take the written one.
    private static byte Merge(byte register, byte value, int locked) => (byte)((register & locked) | (value & 0x7F & ~locked));

    private void Map()
    {
        int ramLow = ramBank & 3, ramLocked = (control >> 4) & 3;
        bool multiplex = (mode & 0x40) != 0, mode0 = (mode & 1) == 0;
        int bank0 = romMask & ~1, bank1 = romMask; // Unmapped: the last 32 KiB.
        if (Mapped)
        {
            int low = romBank & 0x1F, romLocked = (mode >> 1) & 0x1E, high = (ramBank & 0x30) << 3;
            int mid = multiplex ? ramLow : romBank >> 5, mid0 = multiplex && mode0 ? ramLow & ramLocked : mid;
            bank0 = (high | (mid0 << 5) | (low & romLocked)) & romMask;
            bank1 = (high | (mid << 5) | ((low & ~romLocked) == 0 ? low | 1 : low)) & romMask;
        }
        var ramMid = multiplex ? romBank >> 5 : mode0 ? ramLow & ramLocked : ramLow;
        rom0 = bank0 * 0x4000;
        rom1 = bank1 * 0x4000;
        ram = (((ramBank & 0x0C) | ramMid) & ramMask) * 0x2000;
    }

    public void ResetController()
    {
        control = romBank = ramBank = mode = 0;
        RamEnabled = false;
        Map();
    }
}

// MMM01 with a battery (type 0D) that keeps the RAM.
internal sealed class BatteryMmm01Cartridge(ReadOnlySpan<byte> image, int ramSize) : Mmm01Cartridge(image, ramSize), IBatteryBackedCartridge
{
    public byte[] ExportRam() => Ram.ToArray();

    public void ImportRam(ReadOnlySpan<byte> data)
    {
        if (data.Length != Ram.Length)
        {
            throw new ArgumentException($"Expected {Ram.Length} RAM bytes, got {data.Length}.", nameof(data));
        }

        data.CopyTo(Ram);
    }
}
