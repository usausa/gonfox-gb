namespace GonFox.GameBoy.Core.Video;

// Pixels are always opaque BGRA32, top-to-bottom with the origin at the top left.
public readonly record struct VideoFrameInfo(
    int Width, int Height, int StrideBytes, ulong Sequence, bool LcdEnabled);
