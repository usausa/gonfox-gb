namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Video;

// The pixel pipeline: the length of Mode 3, and the state of a line that is being drawn.
[Trait("Category", "Unit")]
public sealed class PpuPipelineTests
{
    private readonly Interrupts irq = new();
    private readonly VideoOutput video = new();
    private readonly Ppu ppu;

    public PpuPipelineTests()
    {
        ppu = new(irq, video);
        ppu.Reset();
        ppu.WriteRegister(0xFF40, 0);
    }

    private void Tick() => ppu.Tick();

    private void TickTo(int line, int dot)
    {
        while (ppu.Ly != line || ppu.Dot != dot)
        {
            ppu.Tick();
        }
    }

    // The dots at which mode 0 begins on a line and the HBlank source raises the STAT interrupt.
    private (int Mode0, int Interrupt) Measure(int line)
    {
        TickTo(line, 0);
        irq.WriteFlags(0);
        int mode0 = -1, interrupt = -1;
        var drawing = false;
        while (ppu.Dot != 455)
        {
            Tick();
            drawing |= ppu.Mode == 3;
            if (mode0 < 0 && drawing && ppu.Mode == 0)
            {
                mode0 = ppu.Dot;
            }

            if (interrupt < 0 && (irq.Flags & 2) != 0)
            {
                interrupt = ppu.Dot;
            }
        }
        return (mode0, interrupt);
    }

    private void Objects(int line, params int[] xs)
    {
        for (var i = 0; i < xs.Length; i++)
        {
            ppu.WriteMemory((ushort)(0xFE00 + (i * 4)), (byte)(line + 16));
            ppu.WriteMemory((ushort)(0xFE01 + (i * 4)), (byte)xs[i]);
        }
    }

    // Mode 0 starts at Dot 253 plus SCX mod 8, 6 for the window, and the objects' fetch stalls.
    [Theory]
    [InlineData(0, "", 0, 0)]
    [InlineData(3, "", 0, 3)]
    [InlineData(7, "", 0, 7)]
    [InlineData(0, "0", 0, 11)]
    [InlineData(0, "4", 0, 7)]
    [InlineData(0, "7", 0, 6)]
    [InlineData(0, "8", 0, 11)]
    [InlineData(0, "8 10", 0, 17)]     // Second object in the same tile.
    [InlineData(0, "8 16", 0, 22)]
    [InlineData(3, "8", 0, 11)]        // Scrolled: 3 + 2 + 6.
    [InlineData(0, "0 0 0 0 0 0 0 0 0 0", 0, 65)]
    [InlineData(0, "166", 0, 6)]       // Two pixels at x=158-159.
    [InlineData(0, "168", 0, 0)]       // Off screen: never fetched.
    [InlineData(0, "", 7, 6)]          // The window from x=0.
    [InlineData(5, "", 87, 11)]
    public void ModeZeroFollowsScrollWindowAndObjects(byte scx, string xs, byte wx, int extra)
    {
        ppu.WriteRegister(0xFF43, scx);
        ppu.WriteRegister(0xFF41, 0x08);
        if (wx != 0)
        {
            ppu.WriteRegister(0xFF4A, 0);
            ppu.WriteRegister(0xFF4B, wx);
        }
        Objects(1, xs.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray());
        ppu.WriteRegister(0xFF40, (byte)(wx != 0 ? 0xF3 : 0x93));
        Assert.Equal((253 + extra, 254 + extra), Measure(1));
    }

    // Objects at X=167 are fetched after the last pixel: mode 0 waits, the HBlank source does not.
    [Theory]
    [InlineData(1, 0, 6)]
    [InlineData(1, 3, 9)]               // Includes the wait before the fetch.
    [InlineData(10, 0, 60)]
    public void ObjectsAtTheLastPixelDelayModeZeroButNotTheHBlankSource(int count, byte scx, int stall)
    {
        foreach (var wholeLines in new[] { true, false })
        {
            var variant = new Ppu(irq, video) { WholeLines = wholeLines };
            variant.Reset();
            variant.WriteRegister(0xFF40, 0);
            for (var i = 0; i < count; i++)
            {
                variant.WriteMemory((ushort)(0xFE00 + (i * 4)), 17);
                variant.WriteMemory((ushort)(0xFE01 + (i * 4)), 167);
            }
            variant.WriteRegister(0xFF43, scx);
            variant.WriteRegister(0xFF41, 0x08);
            variant.WriteRegister(0xFF40, 0x93);
            while (variant.Ly != 1 || variant.Dot != 0)
            {
                variant.Tick();
            }

            irq.WriteFlags(0);
            int mode0 = -1, interrupt = -1;
            var drawing = false;
            while (variant.Dot != 455)
            {
                variant.Tick();
                drawing |= variant.Mode == 3;
                if (mode0 < 0 && drawing && variant.Mode == 0)
                {
                    mode0 = variant.Dot;
                }

                if (interrupt < 0 && (irq.Flags & 2) != 0)
                {
                    interrupt = variant.Dot;
                }
            }
            Assert.Equal((253 + scx + stall, 254 + scx), (mode0, interrupt));
        }
    }

    // Validation rejects a bad OAM scan index, last-pixel dot or late window start.
    [Theory]
    [InlineData("valid")]
    [InlineData("scan-index")]
    [InlineData("last-pixel-dot")]
    [InlineData("late-window")]
    public void BrokenOamScanAndLastPixelFieldsAreRejected(string kind)
    {
        ppu.WriteRegister(0xFF40, 0x93);
        TickTo(1, 40);
        var scanning = ppu.CaptureState(); // Mode 2 of line 1.
        TickTo(1, 300);
        var drawn = ppu.CaptureState(); // Mode 0, drawn by Dot 253.
        Ppu.State[] bad = kind switch
        {
            "valid" => [scanning, drawn, drawn with { Pipe = drawn.Pipe with { LastPixelDot = 253 } }],
            "scan-index" => [scanning with { ScanIndex = 41 }, scanning with { ScanIndex = -1 }],
            "last-pixel-dot" => [drawn with { Pipe = drawn.Pipe with { LastPixelDot = 254 } }, drawn with { Pipe = drawn.Pipe with { LastPixelDot = 85 } }],
            _ => [scanning with { Pipe = scanning.Pipe with { LateWindowStart = true } }]
        };
        foreach (var state in bad)
        {
            if (kind == "valid")
            {
                ppu.ValidateState(state);
            }
            else
            {
                Assert.Throws<ArgumentException>(() => ppu.ValidateState(state));
            }
        }
    }

    // A state saved while last-pixel objects are fetched, with STAT held high in mode 3, is valid.
    [Fact]
    public void AStateWhileObjectsAtTheLastPixelAreFetchedIsValid()
    {
        ppu.WriteRegister(0xFF41, 0x08);
        Objects(1, 167);
        ppu.WriteRegister(0xFF40, 0x93);
        TickTo(1, 256);
        var state = ppu.CaptureState();
        Assert.Equal((byte)3, state.Mode);
        Assert.True(state.StatLine);
        ppu.ValidateState(state);
    }

    // The LCD-on line has no OAM scan; mode 0 starts at Dot 255 plus SCX mod 8.
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    public void TheLcdOnLineStartsDrawingTwoDotsLater(byte scx)
    {
        ppu.WriteRegister(0xFF43, scx);
        ppu.WriteRegister(0xFF41, 0x08);
        ppu.WriteRegister(0xFF40, 0x91);
        irq.WriteFlags(0);
        int mode0 = -1, interrupt = -1;
        while (ppu.Dot != 455)
        {
            Tick();
            if (mode0 < 0 && ppu.Mode == 0 && ppu.Dot > 84)
            {
                mode0 = ppu.Dot;
            }

            if (interrupt < 0 && (irq.Flags & 2) != 0)
            {
                interrupt = ppu.Dot;
            }
        }
        Assert.Equal((255 + scx, 256 + scx), (mode0, interrupt));
    }

    // Objects passed while OBJ display is off are skipped, so later ones still show once it is on.
    [Fact]
    public void ObjectsPassedWithObjDisplayOffDoNotHoldBackLaterOnes()
    {
        for (var row = 0; row < 16; row++)
        {
            ppu.WriteMemory((ushort)(0x8010 + row), 0xFF); // Tile 1: colour 3.
        }

        ppu.WriteRegister(0xFF47, 0xE4);
        ppu.WriteRegister(0xFF48, 0xE4);
        Objects(3, 8, 88);
        ppu.WriteMemory(0xFE02, 1);
        ppu.WriteMemory(0xFE06, 1);
        ppu.WriteRegister(0xFF40, 0x91);
        for (var i = 0; i < 70224; i++)
        {
            Tick(); // The first frame is not shown.
        }

        TickTo(3, 120);
        ppu.WriteRegister(0xFF40, 0x93); // Between the two objects.
        while (video.CompletedFrameCount < 2)
        {
            Tick();
        }

        var pixels = new byte[VideoOutput.BufferSize];
        video.CopyLatestFrame(pixels);
        Assert.Equal(255, pixels[3 * 640]);
        Assert.Equal(0, pixels[(3 * 640) + (80 * 4)]);
        Assert.Equal(0, pixels[(3 * 640) + (87 * 4)]);
        Assert.Equal(255, pixels[(3 * 640) + (88 * 4)]);
    }

    // PPU register writes made at given dots while the written line is drawn.
    private static readonly (int Dot, ushort Address, byte Value)[] Writes =
    [
        (96, 0xFF47, 0x1B), (108, 0xFF43, 7), (120, 0xFF40, 0xFB), (132, 0xFF4B, 60), (144, 0xFF42, 9),
        (160, 0xFF40, 0xF9), (176, 0xFF47, 0xE4), (200, 0xFF40, 0xF3), (212, 0xFF48, 0x00)
    ];
    private const int WrittenLine = 5;

    private static (Ppu Ppu, VideoOutput Video) Scene()
    {
        var video = new VideoOutput();
        var ppu = new Ppu(new Interrupts(), video);
        ppu.Reset();
        ppu.WriteRegister(0xFF40, 0);
        var random = new Random(23);
        for (var i = 0; i < 0x2000; i++)
        {
            ppu.WriteMemory((ushort)(0x8000 + i), (byte)random.Next(256));
        }

        int[] xs = [0, 13, 13, 40, 90, 120];
        for (var i = 0; i < 40; i++)
        {
            ppu.WriteMemory((ushort)(0xFE00 + (i * 4)), (byte)(i < xs.Length ? WrittenLine + 16 - i : 0));
            ppu.WriteMemory((ushort)(0xFE01 + (i * 4)), (byte)(i < xs.Length ? xs[i] : 0));
            ppu.WriteMemory((ushort)(0xFE02 + (i * 4)), (byte)random.Next(256));
            ppu.WriteMemory((ushort)(0xFE03 + (i * 4)), (byte)random.Next(256));
        }
        ppu.WriteRegister(0xFF43, 3);
        ppu.WriteRegister(0xFF47, 0xE4);
        ppu.WriteRegister(0xFF48, 0xD2);
        ppu.WriteRegister(0xFF4A, 2);
        ppu.WriteRegister(0xFF4B, 50);
        ppu.WriteRegister(0xFF40, 0xF3);
        return (ppu, video);
    }

    // Runs to the end of the frame from a dot of the written line, doing the writes from there.
    private static byte[] Finish(Ppu ppu, VideoOutput video, int fromDot)
    {
        foreach (var (dot, address, value) in Writes.Where(write => write.Dot >= fromDot))
        {
            while (ppu.Dot != dot)
            {
                ppu.Tick();
            }

            ppu.WriteRegister(address, value);
        }
        var frames = video.CompletedFrameCount;
        while (video.CompletedFrameCount == frames)
        {
            ppu.Tick();
        }

        var pixels = new byte[VideoOutput.BufferSize];
        video.CopyLatestFrame(pixels);
        return pixels;
    }

    [Fact]
    public void LineWithWritesRestoresFromEveryDotOfItsDrawing()
    {
        var (reference, referenceVideo) = Scene();
        while (reference.Ly != WrittenLine || reference.Dot != 0)
        {
            reference.Tick();
        }

        var expected = Finish(reference, referenceVideo, 0);
        for (var dot = 80; dot <= 330; dot++)
        {
            var (scenePpu, sceneVideo) = Scene();
            while (scenePpu.Ly != WrittenLine || scenePpu.Dot != 0)
            {
                scenePpu.Tick();
            }

            foreach (var (at, address, value) in Writes.Where(write => write.Dot < dot))
            {
                while (scenePpu.Dot != at)
                {
                    scenePpu.Tick();
                }

                scenePpu.WriteRegister(address, value);
            }
            while (scenePpu.Dot != dot)
            {
                scenePpu.Tick();
            }

            var state = scenePpu.CaptureState();
            var image = sceneVideo.CaptureState();
            scenePpu.ValidateState(state);
            sceneVideo.ValidateState(image);
            var restoredVideo = new VideoOutput();
            var restored = new Ppu(new Interrupts(), restoredVideo);
            restored.RestoreState(state);
            restoredVideo.RestoreState(image);
            byte[] original = Finish(scenePpu, sceneVideo, dot), copy = Finish(restored, restoredVideo, dot);
            Assert.True(expected.AsSpan().SequenceEqual(original), $"capture at dot {dot} changed the run");
            Assert.True(expected.AsSpan().SequenceEqual(copy), $"restored at dot {dot}");
            Ppu.State a = scenePpu.CaptureState(), b = restored.CaptureState();
            Assert.Equal(a.LineShades, b.LineShades);
            Assert.Equal(a, b with { Vram = a.Vram, Oam = a.Oam, Sprites = a.Sprites, LineShades = a.LineShades });
        }
    }

    // A state drawn halfway through the written line, then broken one way at a time.
    [Theory]
    [InlineData("valid")]
    [InlineData("step")]
    [InlineData("fetcher")]
    [InlineData("bg-fifo")]
    [InlineData("obj-fifo")]
    [InlineData("next-object")]
    [InlineData("ahead")]
    [InlineData("idle-while-drawing")]
    [InlineData("running-before-start")]
    [InlineData("mode0-while-drawing")]
    [InlineData("lcd-on-line")]
    [InlineData("sprite-order")]
    [InlineData("shade")]
    [InlineData("tile-address")]
    [InlineData("position")]
    [InlineData("lcd-x")]
    [InlineData("last-pixel-dot")]
    public void BrokenPipelineStatesAreRejected(string kind)
    {
        var (scenePpu, _) = Scene();
        while (scenePpu.Ly != WrittenLine || scenePpu.Dot != 0)
        {
            scenePpu.Tick();
        }

        foreach (var (at, address, value) in Writes.Where(write => write.Dot <= 120))
        {
            while (scenePpu.Dot != at)
            {
                scenePpu.Tick();
            }

            scenePpu.WriteRegister(address, value);
        }
        while (scenePpu.Dot != 150)
        {
            scenePpu.Tick();
        }

        var state = scenePpu.CaptureState();
        Assert.Equal(3, state.Mode);
        Assert.NotEqual(Ppu.StepDone, state.Pipe.Step);
        var bad = kind switch
        {
            "valid" => state,
            "step" => state with { Pipe = state.Pipe with { Step = 9 } },
            "fetcher" => state with { Pipe = state.Pipe with { Fetcher = 7 } },
            "bg-fifo" => state with { Pipe = state.Pipe with { BgCount = 9 } },
            "obj-fifo" => state with { Pipe = state.Pipe with { ObjCount = 9 } },
            "next-object" => state with { Pipe = state.Pipe with { NextObject = state.SpriteCount + 1 } },
            "ahead" => state with { Pipe = state.Pipe with { Dot = state.Dot + 3 } },
            "idle-while-drawing" => state with { Pipe = new Ppu.Pipeline { Step = Ppu.StepDone, Position = -16, WindowY = 0xFF } },
            "running-before-start" => state with { Dot = 80, Mode = 2 },
            "mode0-while-drawing" => state with { Mode = 0 },
            "lcd-on-line" => state with { LcdOnLine = true },
            "sprite-order" => state with { Sprites = [state.Sprites[1], state.Sprites[0], .. state.Sprites[2..]] },
            "shade" => state with { LineShades = [4, .. state.LineShades[1..]] },
            "tile-address" => state with { Pipe = state.Pipe with { TileAddress = 0x2000 } },
            "position" => state with { Pipe = state.Pipe with { Position = 160 } },
            "last-pixel-dot" => state with { Pipe = state.Pipe with { LastPixelDot = 120 } }, // Not at the last pixel yet.
            _ => state with { Pipe = state.Pipe with { LcdX = Math.Max(state.Pipe.Position, 0) + 1 } }
        };
        if (kind == "valid")
        {
            scenePpu.ValidateState(bad);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => scenePpu.ValidateState(bad));
        }
    }
}
