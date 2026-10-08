namespace GonFox.GameBoy.Core.Cartridge;

public sealed record CartridgeInfo(
    string Title,
    byte TypeCode,
    int RomSizeBytes,
    int RamSizeBytes,
    bool HasBattery,
    bool HasRumble = false);

public sealed record CartridgeLoadResult(
    ICartridge Cartridge,
    CartridgeInfo Info,
    IReadOnlyList<string> Warnings);
