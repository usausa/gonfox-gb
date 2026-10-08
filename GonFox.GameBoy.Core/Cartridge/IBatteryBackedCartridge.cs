namespace GonFox.GameBoy.Core.Cartridge;

public interface IBatteryBackedCartridge : ICartridge
{
    // A detached copy of all RAM banks, independent of the current enable/bank registers.
    byte[] ExportRam();
    void ImportRam(ReadOnlySpan<byte> data);
}
