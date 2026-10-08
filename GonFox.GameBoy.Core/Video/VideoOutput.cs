namespace GonFox.GameBoy.Core.Video;

using System.Runtime.InteropServices;

// Double-buffers the LCD image and publishes only completed frames, by copy.
public sealed class VideoOutput
{
    public const int Width = 160;
    public const int Height = 144;
    public const int StrideBytes = Width * 4;
    public const int BufferSize = StrideBytes * Height;
    private readonly uint[] palette;

    // Each shade's B, G, R, A bytes as one native-endian value, so a store writes them in that order.
    private readonly uint[] bgra = new uint[4];
    private byte[] drawing = new byte[BufferSize];
    private byte[] published = new byte[BufferSize];
    private bool lcdEnabled;

    // Set when the LCD is enabled: the first image it completes is not shown.
    private bool firstFrame;
    public ulong Sequence { get; private set; }
    public ulong CompletedFrameCount { get; private set; }

    internal sealed record State(byte[] Drawing, byte[] Published, uint[] Palette, bool LcdEnabled, bool FirstFrame);

    internal State CaptureState() => new(drawing.ToArray(), published.ToArray(), palette.ToArray(), lcdEnabled, firstFrame);

    internal void ValidateState(State? state)
    {
        StateValidation.Require(state is not null, "Video");
        StateValidation.Length(state.Drawing, BufferSize, "drawing image");
        StateValidation.Length(state.Published, BufferSize, "published image");
        StateValidation.Length(state.Palette, 4, "display palette");
        StateValidation.Require(state.Palette.AsSpan().SequenceEqual(palette) && Sequence < ulong.MaxValue &&
            (state.LcdEnabled || !state.FirstFrame), "video metadata");
        for (var i = 3; i < BufferSize; i += 4)
        {
            StateValidation.Require(state.Drawing[i] == 255 && state.Published[i] == 255, "opaque BGRA");
        }
    }

    internal void RestoreState(State state)
    {
        state.Drawing.CopyTo(drawing, 0);
        state.Published.CopyTo(published, 0);
        lcdEnabled = state.LcdEnabled;
        firstFrame = state.FirstFrame;
        Sequence++; // Never rewinds.
    }

    public VideoOutput()
        : this([0xFFFFFFu, 0xAAAAAAu, 0x555555u, 0x000000u])
    {
    }

    // Takes four 0xRRGGBB shade colors, fixed for this output's lifetime.
    internal VideoOutput(ReadOnlySpan<uint> rgbPalette)
    {
        if (rgbPalette.Length != 4 || rgbPalette.ContainsAnyExceptInRange(0u, 0xFFFFFFu))
        {
            throw new ArgumentException("Provide four 24-bit RGB colors.", nameof(rgbPalette));
        }

        palette = rgbPalette.ToArray();
        for (var shade = 0; shade < 4; shade++)
        {
            var rgb = palette[shade];
            bgra[shade] = MemoryMarshal.Read<uint>([(byte)rgb, (byte)(rgb >> 8), (byte)(rgb >> 16), 255]);
        }
        ClearBuffers();
    }

    public VideoFrameInfo CopyLatestFrame(Span<byte> destination)
    {
        if (destination.Length < BufferSize)
        {
            throw new ArgumentException($"A frame requires {BufferSize} bytes.", nameof(destination));
        }

        published.CopyTo(destination);
        return new(Width, Height, StrideBytes, Sequence, lcdEnabled);
    }

    // Stores one finished scan line of shades 0-3, checking all arguments before writing.
    internal void WriteLine(int y, ReadOnlySpan<byte> shades)
    {
        if ((uint)y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        if (shades.Length != Width)
        {
            throw new ArgumentException($"A line has {Width} shades.", nameof(shades));
        }

        if (shades.ContainsAnyExceptInRange((byte)0, (byte)3))
        {
            throw new ArgumentOutOfRangeException(nameof(shades));
        }

        if (!lcdEnabled)
        {
            return;
        }

        var row = MemoryMarshal.Cast<byte, uint>(drawing.AsSpan(y * StrideBytes, StrideBytes));
        ReadOnlySpan<uint> shadeColors = bgra;
        for (var x = 0; x < row.Length; x++)
        {
            row[x] = shadeColors[shades[x]];
        }
    }

    internal void CompleteFrame()
    {
        if (!lcdEnabled)
        {
            return;
        }

        CompletedFrameCount++; // Counted even when not shown.
        if (firstFrame)
        {
            firstFrame = false;
            return;
        }

        // Publishes by swapping buffers, so the PPU must write every pixel of each frame.
        (drawing, published) = (published, drawing);
        Sequence++;
    }

    // Turns the LCD on or off; unless showFirstFrame, the first image after enabling stays hidden.
    internal void SetLcdEnabled(bool enabled, bool showFirstFrame = false)
    {
        if (lcdEnabled == enabled)
        {
            return;
        }

        lcdEnabled = enabled;
        firstFrame = enabled && !showFirstFrame;
        if (!enabled)
        {
            ClearBuffers();
        }
        Sequence++;
    }

    internal void Reset()
    {
        lcdEnabled = firstFrame = false;
        ClearBuffers();
        Sequence++; // Never rewinds.
    }

    private void ClearBuffers()
    {
        MemoryMarshal.Cast<byte, uint>(drawing.AsSpan()).Fill(bgra[0]);
        MemoryMarshal.Cast<byte, uint>(published.AsSpan()).Fill(bgra[0]);
    }
}
