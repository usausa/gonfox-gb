namespace GonFox.GameBoy.Core.Cartridge;

// The MBC5 rumble motor, on while bit 3 of the RAM bank register is set.
public interface IRumbleCartridge : ICartridge
{
    bool MotorOn { get; }
}
