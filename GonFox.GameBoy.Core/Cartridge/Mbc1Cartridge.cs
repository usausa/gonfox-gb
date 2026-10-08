namespace GonFox.GameBoy.Core.Cartridge;

// MBC1 in standard wiring, or in MBC1M multicart wiring where the upper register drives A18-A19.
internal class Mbc1Cartridge : ICartridge, IStatefulCartridge, IBankedCartridge
{
    private readonly byte[] rom;
    protected byte[] Ram { get; }
    private readonly int romMask;
    private readonly int upperShift;
    private readonly int lowerMask;
    private byte bank;
    private byte upper;

    public string RomSha256 { get; }
    public byte TypeCode => this is IBatteryBackedCartridge ? (byte)3 : Ram.Length == 0 ? (byte)1 : (byte)2;
    public int LowerRomBank => (BankingMode == 0 ? 0 : upper << upperShift) & romMask;
    public int UpperRomBank => ((upper << upperShift) | ((bank == 0 ? 1 : bank) & lowerMask)) & romMask;
    public int RamBank => Ram.Length == 0x8000 && BankingMode != 0 ? upper : 0;
    public bool RamEnabled { get; private set; }
    public byte BankingMode { get; private set; }

    internal Mbc1Cartridge(ReadOnlySpan<byte> image, int ramSize, bool multicart = false)
    {
        rom = image.ToArray();
        Ram = new byte[ramSize];
        romMask = (image.Length / 0x4000) - 1;
        (upperShift, lowerMask) = multicart ? (4, 0x0F) : (5, 0x1F);
        RomSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(rom));
    }

    public CartridgeState CaptureState() => new(bank, upper, BankingMode, RamEnabled, Ram.ToArray());

    public void ValidateState(CartridgeState? state) => StateValidation.Require(state is not null &&
        state.Bank <= 31 && state.Upper <= 3 && state.Mode <= 1 && state.RamBank == 0 && StateValidation.HasLength(state.Ram, Ram.Length) && state.HasNoExtras(), "MBC1");

    public void RestoreState(CartridgeState state)
    {
        bank = state.Bank;
        upper = state.Upper;
        BankingMode = state.Mode;
        RamEnabled = state.RamEnabled;
        state.Ram.CopyTo(Ram, 0);
    }

    public byte Read(ushort address)
    {
        if (address <= 0x7FFF)
        {
            // Translate zero before masking for the cartridge's connected address lines.
            var mapped = address < 0x4000 ? LowerRomBank : UpperRomBank;
            return rom[(mapped * 0x4000) + (address & 0x3FFF)];
        }
        if (address is >= 0xA000 and <= 0xBFFF)
        {
            return RamEnabled && Ram.Length != 0 ? Ram[RamOffset(address)] : (byte)0xFF;
        }

        throw new ArgumentOutOfRangeException(nameof(address), "Address is outside the cartridge windows.");
    }

    public void Write(ushort address, byte value)
    {
        switch (address)
        {
            case <= 0x1FFF: RamEnabled = (value & 15) == 10; break;
            case <= 0x3FFF: bank = (byte)(value & 31); break;
            case <= 0x5FFF: upper = (byte)(value & 3); break;
            case <= 0x7FFF: BankingMode = (byte)(value & 1); break;
            case >= 0xA000 and <= 0xBFFF:
                if (RamEnabled && Ram.Length != 0)
                {
                    Ram[RamOffset(address)] = value;
                }

                break;
            default: throw new ArgumentOutOfRangeException(nameof(address), "Address is outside the cartridge windows.");
        }
    }

    private int RamOffset(ushort address) =>
        (Ram.Length == 0x8000 && BankingMode != 0 ? upper * 0x2000 : 0) + address - 0xA000;

    public void ResetController()
    {
        bank = upper = BankingMode = 0;
        RamEnabled = false;
    }
}

// MBC1 with a battery (type 03) that keeps the RAM.
internal sealed class BatteryMbc1Cartridge(ReadOnlySpan<byte> image, int ramSize, bool multicart = false)
    : Mbc1Cartridge(image, ramSize, multicart), IBatteryBackedCartridge
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
