namespace GonFox.GameBoy.Core.Cartridge;

internal sealed class RomOnlyCartridge : ICartridge, IStatefulCartridge
{
    internal const int RomSize = 0x8000;
    private readonly byte[] rom;
    public string RomSha256 { get; }
    public byte TypeCode => 0;

    // Only the validated loader constructs this cartridge.
    internal RomOnlyCartridge(ReadOnlySpan<byte> image)
    {
        rom = image.ToArray();
        RomSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(rom));
    }

    public CartridgeState CaptureState() => new(0, 0, 0, false, []);

    public void ValidateState(CartridgeState? state) => StateValidation.Require(state is not null &&
        state.Bank == 0 && state.Upper == 0 && state.Mode == 0 && state.RamBank == 0 && !state.RamEnabled && StateValidation.HasLength(state.Ram, 0) && state.HasNoExtras(), "ROM Only");

    public void RestoreState(CartridgeState state)
    {
    }

    public byte Read(ushort address) => address switch
    {
        <= 0x7FFF => rom[address],
        >= 0xA000 and <= 0xBFFF => 0xFF,
        _ => throw new ArgumentOutOfRangeException(nameof(address), "Address is outside the cartridge windows.")
    };

    public void Write(ushort address, byte value)
    {
        if (address > 0x7FFF && (address < 0xA000 || address > 0xBFFF))
        {
            throw new ArgumentOutOfRangeException(nameof(address), "Address is outside the cartridge windows.");
        }

        // ROM Only has neither writable ROM nor external RAM/control registers.
    }

    public void ResetController()
    {
        // This cartridge has no controller state.
    }
}
