namespace GonFox.GameBoy.Core.Video;

using System.Buffers.Binary;

// The Mode 3 pixel pipeline: a state machine run ahead to the line's end and re-run after writes.
internal sealed partial class Ppu
{
    // Pipeline state; Position runs from -16 (dropped pixels) through 0-159 (visible) to 160.
    internal record struct Pipeline(int Dot, byte Step, byte Fetcher, ushort TileAddress, ushort DataAddress,
        byte Tile, byte Low, byte High, byte BgLow, byte BgHigh, byte BgCount, byte ObjLow, byte ObjHigh,
        byte ObjPalette, byte ObjPriority, byte ObjCount, int Position, int LcdX, bool WindowActive,
        bool WindowFetching, bool InsertPixel, bool NoInsertionGlitch, byte WindowTileX, byte WindowY,
        int NextObject, bool ObjectFetch, bool Aborted, byte ObjectLow, int LastPixelDot, bool HoldWindow,
        bool LateWindowStart);

    // Steps; an object fetch waits for the fetcher, then takes 1 + 2 + 2 + 1 dots.
    internal const byte StepIteration = 0;
    internal const byte StepWindowStart = 1;
    internal const byte StepObjectWait = 2;
    internal const byte StepObjectOam = 3;
    internal const byte StepObjectLow = 4;
    internal const byte StepObjectHigh = 5;
    internal const byte StepObjectNext = 6;
    internal const byte StepDone = 7;
    private const int PipelineStart = 86;
    private const int LcdOnPipelineStart = 88;
    private const int LastDot = 455;
    private int StartDot => lcdOnLine ? LcdOnPipelineStart : PipelineStart;
    private static readonly byte[] Reversed = CreateReversed();

    // A pipeline that is not drawing: the state between lines and after the LCD is enabled.
    private static Pipeline Idle() => new() { Step = StepDone, Position = -16, WindowY = 0xFF };

    // Runs the steps that happen before `until` (the whole line for int.MaxValue).
    private void Run(ref Pipeline p, int until)
    {
        while (p.Dot < until && p.Step != StepDone)
        {
            if (p.Dot >= LastDot)
            {
                // A safety stop; the longest Mode 3 ends near Dot 375.
                EndLine(ref p);
                break;
            }

            switch (p.Step)
            {
                case StepIteration: Iterate(ref p); break;
                case StepWindowStart: StartWindow(ref p); Prepare(ref p); break;
                case StepObjectWait:
                    if (p.Aborted)
                    {
                        Draw(ref p);
                    }
                    else
                    {
                        WaitForFetcher(ref p);
                    }

                    break;
                case StepObjectOam: // Attributes come from the OAM scan.
                    if (p.Aborted)
                    {
                        Draw(ref p);
                        break;
                    }
                    AdvanceFetcher(ref p);
                    p.Step = StepObjectLow; p.Dot += 2;
                    break;
                case StepObjectLow:
                    if (p.Aborted)
                    {
                        Draw(ref p);
                        break;
                    }
                    p.ObjectLow = vram[ObjectRow(p.NextObject)];
                    p.Step = StepObjectHigh; p.Dot += 2;
                    break;
                case StepObjectHigh: // Last read, overlaid at once.
                    if (p.Aborted)
                    {
                        Draw(ref p);
                        break;
                    }
                    p.ObjectFetch = false;
                    var sprite = sprites[p.NextObject];
                    Overlay(ref p, p.ObjectLow, vram[ObjectRow(p.NextObject) + 1], sprite.Flags);
                    p.NextObject++;
                    p.Step = StepObjectNext; p.Dot++;
                    break;
                default: FetchObjectOrDraw(ref p); break; // StepObjectNext.
            }
        }
    }

    // One dot: the window may start, then waiting objects are fetched, then one pixel is drawn.
    private void Iterate(ref Pipeline p)
    {
        if (!p.WindowActive && windowYTriggered && ((lcdc & 0x20) != 0 || windowEnableHeld))
        {
            var position = p.Position;
            var start = false;
            if (wx == 0)
            {
                start = position == -7 || (position == -16 && (scx & 7) != 0) || position is >= -15 and <= -8;
            }
            else if (wx < 166)
            {
                if (wx == ((position + 7) & 0xFF))
                {
                    start = true;
                }
                else if (wx == ((position + 6) & 0xFF) && !wxJustChanged)
                {
                    start = true; // One pixel late; the LCD lags.
                    if (p.LcdX > 0)
                    {
                        p.LcdX--;
                    }
                }
            }
            if (start)
            {
                p.WindowY++;
                p.WindowTileX = 0;
                p.BgLow = p.BgHigh = p.BgCount = 0;
                if (wx == 0 && (scx & 7) != 0)
                {
                    p.Step = StepWindowStart;
                    p.Dot++;
                    return;
                }
                StartWindow(ref p);
            }
        }
        Prepare(ref p);
    }

    private static void StartWindow(ref Pipeline p)
    {
        p.WindowActive = true;
        p.Fetcher = 0;
        p.WindowFetching = true;
    }

    private void Prepare(ref Pipeline p)
    {
        // Reaching the window's position again after a WX change inserts one colour-0 pixel.
        if (wx == ((p.Position + 7) & 0xFF) && p.WindowActive && !p.WindowFetching && p.Fetcher == 0 && p.BgCount == 8)
        {
            p.InsertPixel = true;
        }

        var match = ObjectMatch(p.Position);
        while (p.NextObject < spriteCount && sprites[p.NextObject].X < match)
        {
            p.NextObject++;
        }

        p.ObjectFetch = true;
        FetchObjectOrDraw(ref p);
    }

    // Objects are matched by OAM X = Position + 8; all X=0 objects match before the first pixel.
    private static int ObjectMatch(int position)
    {
        var match = (position + 8) & 0xFF;
        return match > 240 ? 0 : match;
    }

    private void FetchObjectOrDraw(ref Pipeline p)
    {
        if (p.NextObject < spriteCount && (lcdc & 2) != 0 && sprites[p.NextObject].X == ObjectMatch(p.Position))
        {
            if (p.Position == 159 && p.LastPixelDot == 0)
            {
                p.LastPixelDot = p.Dot;
            }

            WaitForFetcher(ref p);
        }
        else
        {
            Draw(ref p);
        }
    }

    // The fetcher finishes its background tile, with a row waiting, before an object fetch.
    private void WaitForFetcher(ref Pipeline p)
    {
        var waiting = p.Fetcher < 5 || p.BgCount == 0;
        AdvanceFetcher(ref p);
        p.Step = waiting ? StepObjectWait : StepObjectOam;
        p.Dot++;
    }

    private void Draw(ref Pipeline p)
    {
        p.Aborted = false;
        p.ObjectFetch = false;
        DrawPixel(ref p);
        AdvanceFetcher(ref p);
        if (p.Position == 160)
        {
            EndLine(ref p);
        }
        else
        {
            p.Step = StepIteration;
            p.Dot++;
        }
    }

    private void DrawPixel(ref Pipeline p)
    {
        if (p.BgCount == 0)
        {
            return;
        }

        int color;
        if (p.InsertPixel)
        {
            p.InsertPixel = false;
            color = 0;
        }
        else
        {
            color = (p.BgLow >> 7) | ((p.BgHigh >> 7) << 1);
            p.BgLow = (byte)(p.BgLow << 1);
            p.BgHigh = (byte)(p.BgHigh << 1);
            p.BgCount--;
        }
        int objColor = 0, pixelFlags = 0;
        if (p.ObjCount != 0)
        {
            var popped = (p.ObjLow >> 7) | ((p.ObjHigh >> 7) << 1);
            var flags = ((p.ObjPalette >> 3) & 0x10) | (p.ObjPriority & 0x80);
            p.ObjLow = (byte)(p.ObjLow << 1);
            p.ObjHigh = (byte)(p.ObjHigh << 1);
            p.ObjPalette = (byte)(p.ObjPalette << 1);
            p.ObjPriority = (byte)(p.ObjPriority << 1);
            p.ObjCount--;
            if (popped != 0 && (lcdc & 2) != 0)
            {
                objColor = popped;
                pixelFlags = flags;
            }
        }
        var position = p.Position;

        // Fine scroll: the pixels before the SCX phase are dropped.
        if (position is >= -16 and <= -9)
        {
            if ((position & 7) == (scxFine & 7) || (p.WindowFetching && (position & 7) == 6 && (scxFine & 7) == 7))
            {
                position = -8;
            }
            else if (position == -9)
            {
                p.Position = -16;
                return;
            }
        }
        p.WindowFetching = false;
        if ((uint)position >= VideoOutput.Width)
        {
            p.Position = position + 1;
            return;
        }
        int shade;
        if ((lcdc & 1) == 0)
        {
            color = 0; // BG off: colour 0, drawn white.
        }

        if (objColor != 0 && (color == 0 || (pixelFlags & 0x80) == 0))
        {
            shade = (((pixelFlags & 0x10) != 0 ? obp1 : obp0) >> (objColor * 2)) & 3;
        }
        else
        {
            shade = (lcdc & 1) == 0 ? 0 : (bgp >> (color * 2)) & 3;
        }

        lineShades[p.LcdX] = (byte)shade;
        p.Position = position + 1;
        p.LcdX++;
    }

    private void AdvanceFetcher(ref Pipeline p)
    {
        switch (p.Fetcher)
        {
            case 0: // Tile number address.
            {
                // A disabled window returns to the background, without the pixel insertion if just started.
                if ((lcdc & 0x20) == 0)
                {
                    if (p.WindowFetching)
                    {
                        p.NoInsertionGlitch = true;
                    }
                    p.WindowActive = false;
                }
                var window = p.WindowActive;
                var map = (lcdc & (window ? 0x40 : 0x08)) != 0 ? 0x1C00 : 0x1800;
                var x = window ? p.WindowTileX
                    : p.Position is >= -16 and <= -9 ? scx >> 3 : ((scx + p.Position + 8) >> 3) & 31;
                p.TileAddress = (ushort)(map + x + ((FetcherY(p) >> 3) * 32));
                p.Fetcher = 1;
                break;
            }
            case 1: p.Tile = vram[p.TileAddress]; p.Fetcher = 2; break;
            case 2: p.DataAddress = (ushort)TileRow(p); p.Fetcher = 3; break; // SCY and LCDC.4 per byte.
            case 3: p.Low = vram[p.DataAddress]; p.Fetcher = 4; break;
            case 4: p.DataAddress = (ushort)(TileRow(p) + 1); p.Fetcher = 5; break;
            case 5:
                p.High = vram[p.DataAddress];
                if (p.WindowActive)
                {
                    p.WindowTileX = (byte)((p.WindowTileX + 1) & 31);
                }

                p.Fetcher = 6;
                goto default;
            default: // Push, retried every dot.
                if (p.BgCount != 0)
                {
                    break;
                }

                if (windowYTriggered && (lcdc & 0x20) == 0 && !p.NoInsertionGlitch)
                {
                    // A disabled window that would start exactly here inserts one colour-0 pixel.
                    var logical = (p.Position + 7) & 0xFF;
                    if (logical > 167)
                    {
                        logical = 0;
                    }

                    if (wx == logical)
                    {
                        p.BgLow = p.BgHigh = 0;
                        p.BgCount = 1;
                        break;
                    }
                }
                p.BgLow = p.Low; p.BgHigh = p.High; p.BgCount = 8; p.Fetcher = 0;
                break;
        }
    }

    private int FetcherY(in Pipeline p) => p.WindowActive ? p.WindowY : (line + scy) & 255;

    private int TileRow(in Pipeline p) =>
        ((lcdc & 0x10) != 0 ? p.Tile * 16 : 0x1000 + ((sbyte)p.Tile * 16)) + ((FetcherY(p) & 7) * 2);

    // The object's row address, using LCDC.2 as of each read.
    private int ObjectRow(int index)
    {
        var sprite = sprites[index];
        var tall = (lcdc & 4) != 0;
        int mask = tall ? 15 : 7, row = (line - sprite.Y) & mask;
        if ((sprite.Flags & 0x40) != 0)
        {
            row ^= mask;
        }

        return ((tall ? sprite.Tile & 0xFE : sprite.Tile) * 16) + (row * 2);
    }

    // Mixes an object row into the OBJ FIFO, filling only its transparent pixels.
    private static void Overlay(ref Pipeline p, byte low, byte high, byte flags)
    {
        if ((flags & 0x20) != 0)
        {
            low = Reversed[low];
            high = Reversed[high];
        }
        var taken = (low | high) & ~(p.ObjLow | p.ObjHigh) & 0xFF;
        p.ObjLow |= (byte)(low & taken);
        p.ObjHigh |= (byte)(high & taken);
        p.ObjPalette = (byte)((p.ObjPalette & ~taken) | ((flags & 0x10) != 0 ? taken : 0));
        p.ObjPriority = (byte)((p.ObjPriority & ~taken) | ((flags & 0x80) != 0 ? taken : 0));
        p.ObjCount = 8;
    }

    // Ends the line at p.Dot, where mode 0 begins.
    private void EndLine(ref Pipeline p)
    {
        // A desynchronized LCD repeats its last pixel to the edge.
        for (; p.LcdX < VideoOutput.Width; p.LcdX++)
        {
            lineShades[p.LcdX] = p.LcdX == 0 ? (byte)0 : lineShades[p.LcdX - 1];
        }

        p.Position = -16;
        if (p.LateWindowStart)
        {
            p.WindowY++;
            p.LateWindowStart = false;
        }
        if (line == 143)
        {
            p.WindowY = 0xFF;
        }

        // WX=166 starts the window at the end of the line, so it covers the next one.
        p.HoldWindow = windowYTriggered && wx == 166;
        if (p.HoldWindow && (lcdc & 0x20) != 0)
        {
            if (!p.WindowActive || line == 143)
            {
                p.WindowY++;
            }

            p.WindowActive = true;
            p.WindowTileX = 1;
        }
        else
        {
            p.WindowActive = false;
        }

        p.Step = StepDone;
    }

    // Tests turn this off to compare the whole-line drawing with the fetcher steps.
    internal bool WholeLines { get; set; } = true;

    // Draws a quirk-free line in one go, with Mode 3 lasting 167 + SCX mod 8 dots plus stalls.
    private bool TryDrawWholeLine(ref Pipeline p)
    {
        if (!WholeLines || p.Step != StepIteration || p.Dot != StartDot || p.WindowActive || p.InsertPixel || p.WindowFetching)
        {
            return false;
        }

        bool objects = spriteCount != 0 && (lcdc & 2) != 0, window = false;
        if (windowYTriggered)
        {
            if ((lcdc & 0x20) == 0)
            {
                if (((wx + scx) & 7) == 7 && wx <= 166)
                {
                    return false;
                }
            }
            else if (wx is 0 or 166 || (wx < 166 && objects))
            {
                return false;
            }
            else
            {
                window = wx < 166;
            }
        }
        Span<byte> lineColors = colors, shades = lineShades;
        if (window)
        {
            p.WindowY++;
        }

        if ((lcdc & 1) == 0)
        {
            lineColors.Clear(); // BG off: colour 0, drawn white.
        }
        else
        {
            var split = window ? Math.Max(wx - 7, 0) : VideoOutput.Width;
            DecodeTiles((lcdc & 8) != 0 ? 0x1C00 : 0x1800, scx, (line + scy) & 255, lineColors[..split]);

            // Window column 0 is at x = WX - 7; WX below 7 cuts off the first columns.
            if (window)
            {
                DecodeTiles((lcdc & 0x40) != 0 ? 0x1C00 : 0x1800, split - (wx - 7), p.WindowY, lineColors[split..]);
            }
        }
        var palette = (lcdc & 1) == 0 ? 0 : bgp;
        for (var x = 0; x < shades.Length; x++)
        {
            shades[x] = (byte)((palette >> (lineColors[x] * 2)) & 3);
        }

        int stalls = window ? 6 : 0, lastPixelStalls = 0;
        if (objects)
        {
            stalls += DrawObjects(lineColors, shades, out lastPixelStalls);
        }

        p.LcdX = VideoOutput.Width;
        p.Dot = StartDot + 167 + (scx & 7) + stalls;
        if (lastPixelStalls != 0)
        {
            p.LastPixelDot = p.Dot - lastPixelStalls; // The last pixel leaves after them.
        }

        EndLine(ref p);
        return true;
    }

    // Draws the line's objects over the background and returns their Mode 3 stalls.
    private int DrawObjects(ReadOnlySpan<byte> background, Span<byte> shades, out int lastPixelStalls)
    {
        Span<byte> lineObjColors = objColors, lineObjFlags = objFlags;
        lineObjColors.Clear();
        int stalls = 0, fine = scx & 7, waited = int.MinValue;
        lastPixelStalls = 0;
        for (var i = 0; i < spriteCount; i++)
        {
            var sprite = sprites[i];
            if (sprite.X >= 168)
            {
                break; // Never matched, like the ones after it.
            }

            int position = sprite.X - 8 + fine, tile = sprite.X == 0 ? -1 : position >> 3;
            int count = sprite.X == 0 ? 8 : 8 - (position & 7), stall = 6;
            if (tile != waited)
            {
                stall += Math.Max(0, count - 3);
                waited = tile;
            }
            stalls += stall;
            if (sprite.X == 167)
            {
                lastPixelStalls += stall;
            }

            var data = ObjectRow(i);
            var pixels = BitSpread[vram[data]] | (BitSpread[vram[data + 1]] << 1);
            if ((sprite.Flags & 0x20) != 0)
            {
                pixels = BinaryPrimitives.ReverseEndianness(pixels); // X flip.
            }

            for (var x = sprite.X - 8; pixels != 0; x++, pixels >>= 8)
            {
                var color = (byte)pixels;
                if (color == 0 || (uint)x >= VideoOutput.Width || lineObjColors[x] != 0)
                {
                    continue;
                }

                lineObjColors[x] = color;
                lineObjFlags[x] = sprite.Flags;
            }
        }
        int palette0 = obp0, palette1 = obp1;
        for (var x = 0; x < shades.Length; x++)
        {
            var color = lineObjColors[x];
            if (color == 0 || ((lineObjFlags[x] & 0x80) != 0 && background[x] != 0))
            {
                continue;
            }

            shades[x] = (byte)((((lineObjFlags[x] & 0x10) != 0 ? palette1 : palette0) >> (color * 2)) & 3);
        }
        return stalls;
    }

    // Writes the colours of map row y (0-255) from map column x onward.
    private void DecodeTiles(int map, int x, int y, Span<byte> destination)
    {
        int fine = x & 7, count = (fine + destination.Length + 7) >> 3;
        int row = map + ((y >> 3) * 32), column = x >> 3, tileLine = (y & 7) * 2;
        var unsignedTiles = (lcdc & 0x10) != 0;
        var tiles = tileRows.AsSpan(0, count * 8);
        for (var i = 0; i < count; i++)
        {
            var tile = vram[row + ((column + i) & 31)];
            var data = (unsignedTiles ? tile * 16 : 0x1000 + ((sbyte)tile * 16)) + tileLine;
            BinaryPrimitives.WriteUInt64LittleEndian(tiles[(i * 8)..], BitSpread[vram[data]] | (BitSpread[vram[data + 1]] << 1));
        }
        tiles.Slice(fine, destination.Length).CopyTo(destination);
    }

    // Spreads a tile byte's bits into eight bytes, leftmost pixel in the lowest byte.
    private static ulong[] CreateBitSpread()
    {
        var table = new ulong[256];
        for (var value = 0; value < 256; value++)
        {
            for (var pixel = 0; pixel < 8; pixel++)
            {
                table[value] |= (ulong)((value >> (7 - pixel)) & 1) << (8 * pixel);
            }
        }

        return table;
    }

    private static byte[] CreateReversed()
    {
        var table = new byte[256];
        for (var value = 0; value < 256; value++)
        {
            for (var bit = 0; bit < 8; bit++)
            {
                if ((value & (1 << bit)) != 0)
                {
                    table[value] |= (byte)(0x80 >> bit);
                }
            }
        }

        return table;
    }
}
