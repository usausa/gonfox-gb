namespace Example.GameBoy.WpfHost.Rendering;

using GonFox.GameBoy.Core.Video;

// Writes a host-only diagnostic image that needs no cartridge.
internal static class TestFramePattern
{
    internal static VideoFrameInfo Write(byte[] pixels, bool grayscale, ulong sequence)
    {
        if (pixels.Length < VideoOutput.BufferSize)
        {
            throw new ArgumentException("Frame buffer too short.", nameof(pixels));
        }

        ReadOnlySpan<uint> colors = grayscale
            ? [0xFFFFFFu, 0xAAAAAAu, 0x555555u, 0x000000u]
            : [0xFFFFFFu, 0xFF0000u, 0x00FF00u, 0x0000FFu];
        for (var y = 0; y < VideoOutput.Height; y++)
        {
            for (var x = 0; x < VideoOutput.Width; x++)
            {
                var shade = y < 56 ? x / 40 : y < 96 ? (x % 2) * 3 : (y % 2) * 3;
                if (x == 0 || x == 159 || y == 0 || y == 143)
                {
                    shade = 0;
                }

                if (x < 8 && y < 8)
                {
                    shade = 1;
                }

                if (x >= 152 && y < 8)
                {
                    shade = 2;
                }

                if (x < 8 && y >= 136)
                {
                    shade = 3;
                }

                if (x >= 152 && y >= 136)
                {
                    shade = 0;
                }

                var rgb = colors[shade];
                var offset = (y * VideoOutput.StrideBytes) + (x * 4);
                pixels[offset] = (byte)rgb;
                pixels[offset + 1] = (byte)(rgb >> 8);
                pixels[offset + 2] = (byte)(rgb >> 16);
                pixels[offset + 3] = 255;
            }
        }

        return new(VideoOutput.Width, VideoOutput.Height, VideoOutput.StrideBytes, sequence, true);
    }
}
