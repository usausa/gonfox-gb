namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Video;

// Lines drawn in one go must match the dot-by-dot fetcher in pixels and in each line's mode 0 dot.
[Trait("Category", "Unit")]
public sealed class PpuWholeLineTests
{
    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 1; seed <= 160; seed++)
        {
            data.Add(seed);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void WholeLinesMatchTheFetcherSteps(int seed)
    {
        var random = new Random(seed);
        byte[] vram = new byte[0x2000], oam = new byte[160];
        random.NextBytes(vram);
        var crowd = random.Next(4); // Spread out or crowded objects.
        for (var i = 0; i < 40; i++)
        {
            oam[i * 4] = (byte)(16 + random.Next(152) - random.Next(16));
            oam[(i * 4) + 1] = (byte)(crowd == 0 ? random.Next(176) : crowd == 1 ? random.Next(17) : Pick(random, 0, 1, 2, 7, 8, 9, 15, 16, 159, 160, 167, 168));
            oam[(i * 4) + 2] = (byte)random.Next(256);
            oam[(i * 4) + 3] = (byte)random.Next(256);
        }
        byte[] registers =
        [
            (byte)(0x80 | random.Next(128)), (byte)random.Next(256), (byte)random.Next(256), // LCDC, SCY, SCX
            (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), // BGP, OBP0, OBP1
            (byte)Pick(random, 0, 1, 50, 143, 144, 255), (byte)random.Next(170) // WY, WX
        ];
        var (fetched, fetchedEnds) = Frame(vram, oam, registers, wholeLines: false);
        var (whole, wholeEnds) = Frame(vram, oam, registers, wholeLines: true);
        Assert.Equal(fetchedEnds, wholeEnds);
        Assert.Equal(fetched, whole);
    }

    private static int Pick(Random random, params int[] values) => values[random.Next(values.Length)];

    // Draws a frame; returns its pixels and the dot where each line's mode 0 began.
    private static (byte[] Pixels, int[] Mode0) Frame(byte[] vram, byte[] oam, byte[] r, bool wholeLines)
    {
        var video = new VideoOutput();
        var ppu = new Ppu(new Interrupts(), video) { WholeLines = wholeLines };
        ppu.Reset();
        ppu.WriteRegister(0xFF40, 0);
        for (var i = 0; i < vram.Length; i++)
        {
            ppu.WriteMemory((ushort)(0x8000 + i), vram[i]);
        }

        for (var i = 0; i < oam.Length; i++)
        {
            ppu.WriteMemory((ushort)(0xFE00 + i), oam[i]);
        }

        ushort[] addresses = [0xFF42, 0xFF43, 0xFF47, 0xFF48, 0xFF49, 0xFF4A, 0xFF4B];
        for (var i = 0; i < addresses.Length; i++)
        {
            ppu.WriteRegister(addresses[i], r[i + 1]);
        }

        ppu.WriteRegister(0xFF40, r[0]);
        var mode0 = new int[144];
        while (video.CompletedFrameCount == 0)
        {
            var mode = ppu.Mode;
            ppu.Tick();
            if (mode == 3 && ppu.Mode == 0)
            {
                mode0[ppu.Ly] = ppu.Dot;
            }
        }
        var pixels = new byte[VideoOutput.BufferSize];
        video.CopyLatestFrame(pixels);
        return (pixels, mode0);
    }
}
