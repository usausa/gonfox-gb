namespace GonFox.GameBoy.Core.Cartridge;

public interface ICartridge
{
    // Accesses a console address; a read must not change emulated state.
    byte Read(ushort address);
    void Write(ushort address, byte value);

    // Reset mapper control state, preserving any cartridge RAM.
    void ResetController();
}
