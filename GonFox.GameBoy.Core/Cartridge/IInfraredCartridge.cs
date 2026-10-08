namespace GonFox.GameBoy.Core.Cartridge;

// The HuC1 infrared LED, for a debugger or a status line; nothing receives the light.
public interface IInfraredCartridge : ICartridge
{
    bool LedOn { get; }
}
