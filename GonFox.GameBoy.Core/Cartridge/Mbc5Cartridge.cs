namespace GonFox.GameBoy.Core.Cartridge;

// MBC5: a 9-bit ROM bank and up to 16 RAM banks; on rumble boards RAM bank bit 3 drives the motor.
internal class Mbc5Cartridge : IStatefulCartridge, IBankedCartridge, IRumbleCartridge
{
    private readonly byte[] rom;
    protected byte[] Ram { get; }

    private readonly bool rumble;
    private byte romLow = 1;
    private byte romHigh;
    private byte ramBank;

    public string RomSha256 { get; }
    public byte TypeCode => rumble
        ? this is IBatteryBackedCartridge ? (byte)0x1E : Ram.Length == 0 ? (byte)0x1C : (byte)0x1D
        : this is IBatteryBackedCartridge ? (byte)0x1B : Ram.Length == 0 ? (byte)0x19 : (byte)0x1A;
    public int LowerRomBank => 0;
    public int UpperRomBank => ((romHigh << 8) | romLow) & field;
    public int RamBank => ramBank & field;
    public bool RamEnabled { get; private set; }
    public byte BankingMode => 0;
    public bool MotorOn => rumble && (ramBank & 8) != 0;

    internal Mbc5Cartridge(ReadOnlySpan<byte> image, int ramSize, bool rumble = false)
    {
        rom = image.ToArray();
        Ram = new byte[ramSize];
        UpperRomBank = (image.Length / 0x4000) - 1;
        RamBank = ramSize == 0 ? 0 : (ramSize / 0x2000) - 1;
        this.rumble = rumble;
        RomSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(rom));
    }

    public CartridgeState CaptureState() => new(romLow, romHigh, 0, RamEnabled, Ram.ToArray(), ramBank);

    public void ValidateState(CartridgeState? state) => StateValidation.Require(state is not null &&
        state.Upper <= 1 && state.Mode == 0 && state.RamBank <= 15 && StateValidation.HasLength(state.Ram, Ram.Length) && state.HasNoExtras(), "MBC5");

    public void RestoreState(CartridgeState state)
    {
        romLow = state.Bank;
        romHigh = state.Upper;
        ramBank = state.RamBank;
        RamEnabled = state.RamEnabled;
        state.Ram.CopyTo(Ram, 0);
    }

    public byte Read(ushort address)
    {
        if (address <= 0x3FFF)
        {
            return rom[address];
        }

        if (address <= 0x7FFF)
        {
            return rom[(UpperRomBank * 0x4000) + (address & 0x3FFF)];
        }

        if (address is >= 0xA000 and <= 0xBFFF)
        {
            return RamEnabled && Ram.Length != 0 ? Ram[(RamBank * 0x2000) + address - 0xA000] : (byte)0xFF;
        }

        throw new ArgumentOutOfRangeException(nameof(address), "Address is outside the cartridge windows.");
    }

    public void Write(ushort address, byte value)
    {
        switch (address)
        {
            case <= 0x1FFF: RamEnabled = (value & 15) == 10; break;
            case <= 0x2FFF: romLow = value; break;
            case <= 0x3FFF: romHigh = (byte)(value & 1); break;
            case <= 0x5FFF: ramBank = (byte)(value & 15); break;
            case <= 0x7FFF: break; // No MBC5 register here.
            case >= 0xA000 and <= 0xBFFF:
                if (RamEnabled && Ram.Length != 0)
                {
                    Ram[(RamBank * 0x2000) + address - 0xA000] = value;
                }

                break;
            default: throw new ArgumentOutOfRangeException(nameof(address), "Address is outside the cartridge windows.");
        }
    }

    public void ResetController()
    {
        romLow = 1;
        romHigh = 0;
        ramBank = 0;
        RamEnabled = false;
    }
}

// MBC5 with a battery (types 1B and 1E) that keeps the RAM.
internal sealed class BatteryMbc5Cartridge(ReadOnlySpan<byte> image, int ramSize, bool rumble = false)
    : Mbc5Cartridge(image, ramSize, rumble), IBatteryBackedCartridge
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
