namespace GonFox.GameBoy.Core.Cartridge;

// A cartridge that can save machine state; custom ICartridge implementations cannot.
internal interface IStatefulCartridge
{
    string RomSha256 { get; }
    byte TypeCode { get; }
    CartridgeState CaptureState();
    void ValidateState(CartridgeState? state);
    void RestoreState(CartridgeState state);
}

// The mapper's raw registers and all of its RAM; fields a mapper does not use are 0 or null.
internal sealed record CartridgeState(byte Bank, byte Upper, byte Mode, bool RamEnabled, byte[] Ram, byte RamBank = 0,
    RtcState? Rtc = null, CameraState? Camera = null, Huc3State? Huc3 = null)
{
    // Checks that the parts of other mappers are absent.
    internal bool HasNoClock() => Rtc is null && Huc3 is null;
    internal bool HasNoExtras() => HasNoClock() && Camera is null;
}

// The ROM and RAM banks and bank settings a debugger shows.
internal interface IBankedCartridge
{
    int LowerRomBank { get; }
    int UpperRomBank { get; }
    int RamBank { get; }
    bool RamEnabled { get; }
    byte BankingMode { get; }
}
