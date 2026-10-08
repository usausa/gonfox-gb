namespace GonFox.GameBoy.Core.Cartridge;

// MBC2: ROM banking and a built-in 512 x 4-bit RAM, with address bit 8 choosing the register.
internal class Mbc2Cartridge : ICartridge, IStatefulCartridge, IBankedCartridge
{
    internal const int RamSize = 512;
    private readonly byte[] rom;
    protected byte[] Ram { get; } = new byte[RamSize]; // One nibble per byte.

    private byte bank;

    public string RomSha256 { get; }
    public byte TypeCode => this is IBatteryBackedCartridge ? (byte)6 : (byte)5;
    public int LowerRomBank => 0;
    public int UpperRomBank => (bank == 0 ? 1 : bank) & field;
    public int RamBank => 0;
    public bool RamEnabled { get; private set; }
    public byte BankingMode => 0;

    internal Mbc2Cartridge(ReadOnlySpan<byte> image)
    {
        rom = image.ToArray();
        UpperRomBank = (image.Length / 0x4000) - 1;
        RomSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(rom));
    }

    public CartridgeState CaptureState() => new(bank, 0, 0, RamEnabled, Ram.ToArray());

    public void ValidateState(CartridgeState? state) => StateValidation.Require(state is not null && state.Bank <= 15 &&
        state.Upper == 0 && state.Mode == 0 && state.RamBank == 0 && StateValidation.HasLength(state.Ram, RamSize) &&
        state.Ram.AsSpan().IndexOfAnyExceptInRange((byte)0, (byte)15) < 0 && state.HasNoExtras(), "MBC2");

    public void RestoreState(CartridgeState state)
    {
        bank = state.Bank;
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
            return RamEnabled ? (byte)(0xF0 | Ram[address & 0x1FF]) : (byte)0xFF;
        }

        throw new ArgumentOutOfRangeException(nameof(address), "Address is outside the cartridge windows.");
    }

    public void Write(ushort address, byte value)
    {
        switch (address)
        {
            case <= 0x3FFF when (address & 0x100) == 0: RamEnabled = (value & 15) == 10; break;
            case <= 0x3FFF: bank = (byte)(value & 15); break;
            case <= 0x7FFF: break; // No MBC2 register here.
            case >= 0xA000 and <= 0xBFFF:
                if (RamEnabled)
                {
                    Ram[address & 0x1FF] = (byte)(value & 15);
                }

                break;
            default: throw new ArgumentOutOfRangeException(nameof(address), "Address is outside the cartridge windows.");
        }
    }

    public void ResetController()
    {
        bank = 0;
        RamEnabled = false;
    }
}

// MBC2 with a battery (type 06) that keeps the RAM; an import keeps the low nibble of each byte.
internal sealed class BatteryMbc2Cartridge(ReadOnlySpan<byte> image) : Mbc2Cartridge(image), IBatteryBackedCartridge
{
    public byte[] ExportRam() => Ram.ToArray();

    public void ImportRam(ReadOnlySpan<byte> data)
    {
        if (data.Length != Ram.Length)
        {
            throw new ArgumentException($"Expected {Ram.Length} RAM bytes, got {data.Length}.", nameof(data));
        }

        for (var i = 0; i < Ram.Length; i++)
        {
            Ram[i] = (byte)(data[i] & 15);
        }
    }
}
