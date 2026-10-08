namespace GonFox.GameBoy.Core.Cartridge;

// HuC1 (type FF): ROM and RAM banking, with an infrared register in place of a RAM enable.
internal class HuC1Cartridge : IStatefulCartridge, IBankedCartridge, IInfraredCartridge
{
    private readonly byte[] rom;
    protected byte[] Ram { get; }

    private byte romBank = 1;
    private byte ramBank;
    private bool irMode;
    public string RomSha256 { get; }
    public byte TypeCode => 0xFF;
    public int LowerRomBank => 0;
    public int UpperRomBank => romBank & field;
    public int RamBank => ramBank & field;
    public bool RamEnabled => !irMode;
    public byte BankingMode => 0;
    public bool LedOn { get; private set; }

    internal HuC1Cartridge(ReadOnlySpan<byte> image, int ramSize)
    {
        rom = image.ToArray();
        Ram = new byte[ramSize];
        UpperRomBank = (image.Length / 0x4000) - 1;
        RamBank = ramSize == 0 ? 0 : (ramSize / 0x2000) - 1;
        RomSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(rom));
    }

    // Captures the state with the IR select as Mode and the LED as Upper.
    public CartridgeState CaptureState() => new(romBank, LedOn ? (byte)1 : (byte)0, irMode ? (byte)1 : (byte)0, false, Ram.ToArray(), ramBank);

    public void ValidateState(CartridgeState? state) => StateValidation.Require(state is not null && state.Bank <= 63 &&
        state.Upper <= 1 && state.Mode <= 1 && !state.RamEnabled && state.RamBank <= 3 && StateValidation.HasLength(state.Ram, Ram.Length) && state.HasNoExtras(), "HuC1");

    public void RestoreState(CartridgeState state)
    {
        romBank = state.Bank;
        ramBank = state.RamBank;
        irMode = state.Mode != 0;
        LedOn = state.Upper != 0;
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
            return irMode ? (byte)0xC0 : Ram.Length != 0 ? Ram[(RamBank * 0x2000) + address - 0xA000] : (byte)0xFF;
        }

        throw new ArgumentOutOfRangeException(nameof(address), "Address is outside the cartridge windows.");
    }

    public void Write(ushort address, byte value)
    {
        switch (address)
        {
            case <= 0x1FFF: irMode = value == 0x0E; break;
            case <= 0x3FFF: romBank = (byte)(value & 63); break;
            case <= 0x5FFF: ramBank = (byte)(value & 3); break;
            case <= 0x7FFF: break; // No register here.
            case >= 0xA000 and <= 0xBFFF:
                if (irMode)
                {
                    LedOn = (value & 1) != 0;
                }
                else if (Ram.Length != 0)
                {
                    Ram[(RamBank * 0x2000) + address - 0xA000] = value;
                }

                break;
            default: throw new ArgumentOutOfRangeException(nameof(address), "Address is outside the cartridge windows.");
        }
    }

    public void ResetController()
    {
        romBank = 1;
        ramBank = 0;
        irMode = false;
        LedOn = false;
    }
}

// A HuC1 whose battery keeps the RAM.
internal sealed class BatteryHuC1Cartridge(ReadOnlySpan<byte> image, int ramSize) : HuC1Cartridge(image, ramSize), IBatteryBackedCartridge
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
