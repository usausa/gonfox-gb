namespace GonFox.GameBoy.Core.Cartridge;

// The camera sensor's image: 128 x 112 luminance bytes set by the host, or mid grey when cleared.
public interface ICameraCartridge : ICartridge
{
    const int ImageWidth = 128;
    const int ImageHeight = 112;

    void SetImage(ReadOnlySpan<byte> luminance);
    void ClearImage();
}
