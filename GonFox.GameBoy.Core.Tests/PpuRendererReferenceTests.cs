namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Video;

// Compares the FIFO PPU's frames with an independent per-pixel renderer for constant registers.
[Trait("Category", "Unit")]
public sealed class PpuRendererReferenceTests
{
    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 1; seed <= 48; seed++)
        {
            data.Add(seed);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void RandomFramesMatchThePerPixelReference(int seed)
    {
        var random = new Random(seed);
        var registers = WithoutWindowQuirks(new Registers((byte)(0x80 | random.Next(128)), (byte)random.Next(256),
            (byte)Pick(random, 0, 250, 251, 255), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256),
            (byte)Pick(random, 0, 1, 70, 143, 144, 200), (byte)Pick(random, 0, 1, 6, 7, 8, 80, 165, 166, 167, 200)));
        byte[] vram = new byte[0x2000], oam = new byte[160];
        random.NextBytes(vram);

        // Mostly visible objects, with edges and crowded lines.
        for (var i = 0; i < 40; i++)
        {
            oam[i * 4] = (byte)(random.Next(4) == 0 ? random.Next(256) : 16 + random.Next(150) - random.Next(16));
            oam[(i * 4) + 1] = (byte)(random.Next(4) == 0 ? Pick(random, 0, 1, 7, 8, 159, 160, 161, 167, 168) : random.Next(170));
            oam[(i * 4) + 2] = (byte)random.Next(256);
            oam[(i * 4) + 3] = (byte)random.Next(256);
        }
        AssertSameFrame(registers, vram, oam);
    }

    // Edge register values, avoiding the window quirks that the reference leaves out.
    [Theory]
    [InlineData(0x93, 0, 0, 7, 144)]   // Unsigned tiles, no scroll, no window.
    [InlineData(0x83, 255, 7, 7, 20)]  // Signed tiles, scroll wraps at 256.
    [InlineData(0xF3, 3, 248, 0, 20)]  // Window from WX 0, partly cut off.
    [InlineData(0xE1, 9, 133, 6, 20)]  // Window, signed tiles, OBJs off.
    [InlineData(0xB7, 1, 1, 165, 20)]  // Window with two columns, 8x16 OBJs.
    [InlineData(0x86, 0, 0, 7, 20)]    // BG and window off.
    public void EdgeRegistersMatchThePerPixelReference(byte lcdc, byte scy, byte scx, byte wx, byte wy)
    {
        var random = new Random((lcdc * 65_536) + (scx * 256) + wx);
        byte[] vram = new byte[0x2000], oam = new byte[160];
        random.NextBytes(vram);

        // Twenty objects share each of two lines; several share an X.
        for (var i = 0; i < 40; i++)
        {
            oam[i * 4] = (byte)(i < 20 ? 40 : 90);
            oam[(i * 4) + 1] = (byte)(i % 3 == 0 ? 50 : random.Next(176));
            oam[(i * 4) + 2] = (byte)random.Next(256);
            oam[(i * 4) + 3] = (byte)random.Next(256);
        }
        AssertSameFrame(new Registers(lcdc, scy, scx, 0xE4, 0xD2, 0x1B, wy, wx), vram, oam);
    }

    private sealed record Registers(byte Lcdc, byte Scy, byte Scx, byte Bgp, byte Obp0, byte Obp1, byte Wy, byte Wx);

    // Moves WY out of reach when the registers would show a window quirk the reference leaves out.
    private static Registers WithoutWindowQuirks(Registers r)
    {
        var quirk = (r.Lcdc & 0x20) != 0 ? (r.Wx == 0 && (r.Scx & 7) != 0) || r.Wx == 166 : ((r.Wx + r.Scx) & 7) == 7 && r.Wx <= 166;
        return r.Wy < 144 && quirk ? r with { Wy = 200 } : r;
    }

    private static int Pick(Random random, params int[] values) => values[random.Next(values.Length)];

    private static void AssertSameFrame(Registers registers, byte[] vram, byte[] oam)
    {
        var video = new VideoOutput();
        var ppu = new Ppu(new Interrupts(), video);
        ppu.Reset();
        ppu.WriteRegister(0xFF40, 0); // LCD off: VRAM and OAM open.
        for (var i = 0; i < vram.Length; i++)
        {
            ppu.WriteMemory((ushort)(0x8000 + i), vram[i]);
        }

        for (var i = 0; i < oam.Length; i++)
        {
            ppu.WriteMemory((ushort)(0xFE00 + i), oam[i]);
        }

        ppu.WriteRegister(0xFF42, registers.Scy);
        ppu.WriteRegister(0xFF43, registers.Scx);
        ppu.WriteRegister(0xFF47, registers.Bgp);
        ppu.WriteRegister(0xFF48, registers.Obp0);
        ppu.WriteRegister(0xFF49, registers.Obp1);
        ppu.WriteRegister(0xFF4A, registers.Wy);
        ppu.WriteRegister(0xFF4B, registers.Wx);
        ppu.WriteRegister(0xFF40, registers.Lcdc);
        while (video.CompletedFrameCount < 2)
        {
            ppu.Tick(); // The first frame is not shown.
        }

        var actual = new byte[VideoOutput.BufferSize];
        video.CopyLatestFrame(actual);
        var expected = Reference(registers, vram, oam);
        int different = 0, first = -1;
        for (var i = 0; i < expected.Length; i++)
        {
            if (expected[i] != actual[i])
            {
                different++;
                if (first < 0)
                {
                    first = i;
                }
            }
        }

        Assert.True(different == 0, $"{registers}: {different} bytes differ, first at pixel ({(first / 4) % 160},{first / 640})");
    }

    // The reference renderer, pixel by pixel, for one frame whose registers stay constant.
    private static byte[] Reference(Registers r, byte[] vram, byte[] oam)
    {
        var pixels = new byte[VideoOutput.BufferSize];
        byte[] gray = [255, 170, 85, 0];
        var triggered = false;
        int windowLine = 0, height = (r.Lcdc & 4) != 0 ? 16 : 8;
        for (var line = 0; line < 144; line++)
        {
            if (line == r.Wy)
            {
                triggered = true; // WY is compared at each line start.
            }

            var sprites = new List<(int X, int Tile, int Flags, int Row)>();
            for (var i = 0; i < 160 && sprites.Count < 10; i += 4)
            {
                var row = line + 16 - oam[i];
                if (row < 0 || row >= height)
                {
                    continue;
                }

                if ((oam[i + 3] & 0x40) != 0)
                {
                    row = height - 1 - row;
                }

                sprites.Add((oam[i + 1], oam[i + 2] & (height == 16 ? 0xFE : 0xFF), oam[i + 3], row));
            }
            int y = (line + r.Scy) & 255, windowX = r.Wx - 7;
            var window = (r.Lcdc & 0x21) == 0x21 && triggered && windowX < 160;
            for (var x = 0; x < 160; x++)
            {
                int shade = 0, bgColor = 0;
                if ((r.Lcdc & 1) != 0)
                {
                    bgColor = window && x >= windowX
                        ? MapColor(vram, r.Lcdc, (r.Lcdc & 0x40) != 0 ? 0x1C00 : 0x1800, x - windowX, windowLine)
                        : MapColor(vram, r.Lcdc, (r.Lcdc & 8) != 0 ? 0x1C00 : 0x1800, (x + r.Scx) & 255, y);
                    shade = (r.Bgp >> (bgColor * 2)) & 3;
                }
                if ((r.Lcdc & 2) != 0)
                {
                    int bestX = 256, color = 0, flags = 0;
                    foreach (var (x1, tile, flags1, row1) in sprites)
                    {
                        var column = x + 8 - x1;
                        if (column < 0 || column >= 8 || x1 >= bestX)
                        {
                            continue;
                        }

                        if ((flags1 & 0x20) != 0)
                        {
                            column = 7 - column;
                        }

                        var candidate = TileColor(vram, tile * 16, column, row1);
                        if (candidate == 0)
                        {
                            continue;
                        }

                        bestX = x1;
                        color = candidate;
                        flags = flags1;
                    }
                    if (color != 0 && ((flags & 0x80) == 0 || bgColor == 0))
                    {
                        shade = ((((flags & 0x10) != 0) ? r.Obp1 : r.Obp0) >> (color * 2)) & 3;
                    }
                }
                var offset = (line * 640) + (x * 4);
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = gray[shade];
                pixels[offset + 3] = 255;
            }
            if (window)
            {
                windowLine++;
            }
        }
        return pixels;
    }

    private static int MapColor(byte[] vram, byte lcdc, int map, int x, int y)
    {
        var tile = vram[map + ((y / 8) * 32) + (x / 8)];
        var data = (lcdc & 0x10) != 0 ? tile * 16 : 0x1000 + ((sbyte)tile * 16);
        return TileColor(vram, data, x & 7, y & 7);
    }

    private static int TileColor(byte[] vram, int data, int x, int row)
    {
        data += row * 2;
        var bit = 7 - x;
        return ((vram[data] >> bit) & 1) | (((vram[data + 1] >> bit) & 1) << 1);
    }
}
