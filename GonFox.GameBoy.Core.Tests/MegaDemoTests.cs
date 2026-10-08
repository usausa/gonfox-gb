namespace GonFox.GameBoy.Core;

using System.Security.Cryptography;
using System.Text.Json;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Video;

// Plays the mega demo against its documented formulas, input rules and pinned reference frames.
[Trait("Category", "Rom")]
public sealed partial class MegaDemoTests(ITestOutputHelper output)
{
    private const string RomSha256 = "d1c2a4987088f97c051ecd8fe7bb008ebf9bb2c180556ee7e267a2809beea41f";
    private const byte Right = 1;
    private const byte Left = 2;
    private const byte Up = 4;
    private const byte Down = 8;
    private const byte A = 16;
    private const byte B = 32;
    private const byte Select = 64;
    private const byte Start = 128;
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "TestData", "MegaDemo");
    private static readonly Lazy<byte[]> Image = new(() =>
    {
        var rom = File.ReadAllBytes(Path.Combine(Root, "megademo.gb"));
        Assert.Equal(RomSha256, Convert.ToHexStringLower(SHA256.HashData(rom)));
        return rom;
    });

    // Label addresses from the rgblink .sym file, whose lines read "bank:address label".
    private static readonly Lazy<Dictionary<string, ushort>> Symbols = new(() => File.ReadAllLines(Path.Combine(Root, "megademo.sym"))
        .Where(line => line.Length > 0 && line[0] != ';').Select(line => line.Split(' '))
        .ToDictionary(parts => parts[1], parts => Convert.ToUInt16(parts[0][3..], 16)));

    [Fact]
    public void RomHeaderSymbolsAndObservationBlockArePinned()
    {
        var rom = Image.Value;
        Assert.Equal(0x8000, rom.Length);
        Assert.Equal("MEGADEMO", System.Text.Encoding.ASCII.GetString(rom, 0x134, 8));
        // ReSharper disable once UseUtf8StringLiteral
        Assert.Equal(new byte[] { 0, 0, 0 }, rom[0x147..0x14A]); // ROM only, 32 KiB, no RAM.
        Assert.All(rom[0x104..0x134], value => Assert.Equal(0, value)); // No Nintendo logo.
        byte header = 0;
        for (var i = 0x134; i <= 0x14C; i++)
        {
            header = unchecked((byte)(header - rom[i] - 1));
        }

        Assert.Equal(header, rom[0x14D]);
        ushort global = 0;
        for (var i = 0; i < rom.Length; i++)
        {
            if (i is not (0x14E or 0x14F))
            {
                global = unchecked((ushort)(global + rom[i]));
            }
        }

        Assert.Equal(global, (ushort)((rom[0x14E] << 8) | rom[0x14F]));
        var load = CartridgeLoader.Load(rom);
        Assert.IsType<RomOnlyCartridge>(load.Cartridge);
        string[] observed = ["wScene", "wSpeed", "wAmount", "wPattern", "wDirection", "wAuto", "wPhase", "wPhase", "wFrame", "wFrame",
            "wHeld", "wPressed", "wSteps", "wLagFrames", "wUpload", "wShownAngle"];
        for (var i = 0; i < observed.Length; i++)
        {
            if (i == 0 || observed[i] != observed[i - 1])
            {
                Assert.Equal(0xC000 + i, Symbols.Value[observed[i]]);
            }
        }

        Assert.Equal(0xC010, Symbols.Value["wSceneParams"]);
        Assert.Equal(0xC028, Symbols.Value["wStatusText"]);
        string[] music = ["wSongStep", "wTempoAcc", "wMusicOn", "wEffectFrames", "wInstrument", "wMelodyNote", "wHarmonyNote", "wBassNote",
            "wMelodyEnvelope", "wHarmonyDuty", "wHarmonyEnvelope", "wBassLevel"]; // Read by the music tests at C050.
        for (var i = 0; i < music.Length; i++)
        {
            Assert.Equal(0xC050 + i, Symbols.Value[music[i]]);
        }

        Assert.Equal(0x0048, Symbols.Value["StatIsr"]);
        foreach (var label in new[] { "StatIsr.scx", "StatIsr.bgp", "VBlankIsr", "VBlankIsr.return", "FontTiles", "StarTiles", "ObjectTiles" })
        {
            Assert.True(Symbols.Value.ContainsKey(label), label);
        }
    }

    [Fact]
    public void InputRulesFollowTheDocumentedSpecification()
    {
        var system = Boot();
        var o = Frame(system, 0);
        Assert.Equal(new byte[] { 0, 3, 4, 0, 0, 1 }, o[..6]);
        AssertStatus(system, 0, 3, 4, 0, 0, true);

        // Right steps on the press, repeats after 20 frames and then every 6, up to 7.
        for (var frame = 0; frame < 50; frame++)
        {
            var steps = 1 + (frame >= 20 ? 1 + ((frame - 20) / 6) : 0);
            Assert.Equal(Math.Min(7, 3 + steps), Frame(system, Right)[1]);
        }
        Frame(system, 0);
        for (var frame = 0; frame < 60; frame++)
        {
            Frame(system, Left);
        }

        Assert.Equal(0, Observe(system)[1]); // Minimum 0, held.
        for (var frame = 0; frame < 40; frame++)
        {
            Assert.Equal(0, Frame(system, Left | Right)[1]);
        }

        Frame(system, 0);
        for (var frame = 0; frame < 40; frame++)
        {
            Frame(system, Up);
        }

        Assert.Equal(7, Observe(system)[2]);
        for (var frame = 0; frame < 40; frame++)
        {
            Assert.Equal(7, Frame(system, Up | Down)[2]);
        }

        Frame(system, 0);
        for (var frame = 0; frame < 60; frame++)
        {
            Frame(system, Down);
        }

        Assert.Equal(0, Observe(system)[2]);
        Frame(system, 0);
        Assert.Equal(1, Frame(system, Down | Up | Right)[1]); // Right steps; the vertical pair cancels.
        Assert.Equal(0, Observe(system)[2]);

        // A, B and Start act on the press edge only.
        Frame(system, 0);
        for (var frame = 0; frame < 30; frame++)
        {
            Assert.Equal(1, Frame(system, A)[3]);
        }

        for (var press = 2; press <= 4; press++)
        {
            Frame(system, 0);
            Assert.Equal(press & 3, Frame(system, A)[3]);
        }
        Frame(system, 0);
        for (var frame = 0; frame < 30; frame++)
        {
            Assert.Equal(1, Frame(system, B)[4]);
        }

        // Reversed, the phase moves by -(speed + 1) * 128 per frame.
        Frame(system, 0);
        var before = Phase(Observe(system));
        var after = Phase(Frame(system, 0));
        Assert.Equal((before - 256) & 0xFFFF, after);
        Assert.Equal(0, Frame(system, Start)[5]);
        before = Phase(Frame(system, 0));
        Assert.Equal(2, Frame(system, Right)[1]); // Input works while stopped.
        Assert.Equal(before, Phase(Frame(system, 0)));
        Frame(system, 0);
        AssertStatus(system, 0, 2, 0, 0, 1, false);
        Assert.Equal(1, Frame(system, Start)[5]);
        Frame(system, 0);
        Assert.Equal((Phase(Observe(system)) - 384) & 0xFFFF, Phase(Frame(system, 0)));

        // Select switches scenes in order; each keeps its own parameters.
        o = Frame(system, Select);
        Assert.Equal(new byte[] { 1, 3, 6, 0, 0 }, o[..5]);
        Frame(system, 0);
        Frame(system, 0);
        AssertStatus(system, 1, 3, 6, 0, 0, true);
        o = Frame(system, Select | Down);
        Assert.Equal(new byte[] { 2, 3, 4, 0, 0 }, o[..5]); // Down lowers the new scene.
        Frame(system, 0);
        Frame(system, 0);
        AssertStatus(system, 2, 3, 4, 0, 0, true);
        o = Frame(system, Select);
        Assert.Equal(new byte[] { 0, 2, 0, 0, 1 }, o[..5]);
        Frame(system, 0);
        Frame(system, 0);
        AssertStatus(system, 0, 2, 0, 0, 1, true);
        Assert.Equal(0, Observe(system)[0x0D]); // No frame was missed.
    }

    [Fact]
    public void WaveFramesMatchSineRasterAndStatusBarFormulas()
    {
        var system = Boot();
        for (var frame = 0; frame < 12; frame++)
        {
            Frame(system, Up); // One step to 5, before the first repeat.
        }

        Frame(system, A);
        Frame(system, 0);
        for (var frame = 0; frame < 3; frame++)
        {
            var o = Frame(system, 0);
            Assert.Equal(new byte[] { 0, 3, 5, 1, 0, 1 }, o[..6]);
            AssertFrame(system, ExpectedWave(o, 5, 1), "wave");
        }
        for (var frame = 0; frame < 30; frame++)
        {
            Frame(system, Up);
        }

        Frame(system, B);
        Frame(system, A);
        Frame(system, 0);
        Frame(system, A);
        Frame(system, 0);
        var last = Frame(system, 0);
        Assert.Equal(new byte[] { 0, 3, 7, 3, 1 }, last[..5]);
        AssertFrame(system, ExpectedWave(last, 7, 3), "wave-max");
    }

    [Fact]
    public void PlasmaFramesMatchMapTexturePaletteAndScrollFormulas()
    {
        var system = Boot();
        Frame(system, Select);
        for (var frame = 0; frame < 4; frame++)
        {
            Frame(system, 0);
        }

        for (var frame = 0; frame < 2; frame++)
        {
            var o = Frame(system, 0);
            Assert.Equal(new byte[] { 1, 3, 6, 0, 0, 1 }, o[..6]);
            AssertFrame(system, ExpectedPlasma(o, 6, 0), "plasma");
        }

        // Pattern 2 arrives in four 64-byte chunks, one per VBlank, then the whole set matches.
        Frame(system, A);
        Frame(system, 0);
        Frame(system, A);
        int[] uploads = [Observe(system)[0x0E], Frame(system, Down)[0x0E], Frame(system, 0)[0x0E], Frame(system, 0)[0x0E], Frame(system, 0)[0x0E]];
        Assert.Equal([4, 3, 2, 1, 0], uploads);
        var last = Frame(system, 0);
        Assert.Equal(new byte[] { 1, 3, 5, 2, 0 }, last[..5]);
        AssertFrame(system, ExpectedPlasma(last, 5, 2), "plasma-texture");
    }

    [Fact]
    public void OrbitObjectsFollowRingFormulasAndTheTenObjectLimit()
    {
        var system = Boot();
        Frame(system, Select);
        Frame(system, 0);
        Frame(system, Select);
        for (var frame = 0; frame < 4; frame++)
        {
            Frame(system, 0);
        }

        for (var pattern = 0; pattern < 4; pattern++)
        {
            var o = Frame(system, 0);
            Assert.Equal(new byte[] { 2, 3, 5, (byte)pattern }, o[..4]);
            AssertOam(system, o[0x0F], 5, pattern);
            Frame(system, A);
            Frame(system, 0);
        }
        for (var frame = 0; frame < 60; frame++)
        {
            Frame(system, Down);
        }

        Frame(system, 0);
        for (var frame = 0; frame < 2; frame++)
        {
            var o = Frame(system, 0);
            Assert.Equal(0, o[2]);
            AssertOam(system, o[0x0F], 0, 0);
            AssertFrame(system, ExpectedOrbitAtCentre(o), "orbit-centre");
        }
    }

    [Fact]
    public void RasterWritesStayInHBlankAndHandlersFitTheirBudgets()
    {
        var system = Boot();
        ushort scx = Symbols.Value["StatIsr.scx"], bgp = Symbols.Value["StatIsr.bgp"];
        ushort vblank = Symbols.Value["VBlankIsr"], vblankReturn = Symbols.Value["VBlankIsr.return"];
        int minWrite = 456, maxWrite = 0, maxVblankEnd = 0, handlers = 0;

        // Each scene at its heaviest settings; the plasma part includes a tile upload.
        foreach (var inputs in new[] { new[] { Up, Right }, [Select, 0, 0, 0, Up, A], [Select, 0, 0, 0, Up] })
        {
            foreach (var input in inputs)
            {
                for (var frame = 0; frame < (input is Up or Right ? 40 : 1); frame++)
                {
                    Frame(system, input);
                }
            }

            ulong end = system.Video.CompletedFrameCount + 20, limit = system.TotalTCycles + (21 * 70_224);
            var writes = new int[145];
            var inVblank = false;
            while (system.Video.CompletedFrameCount < end)
            {
                Assert.True(system.TotalTCycles < limit);
                var pc = system.GetDebugSnapshot().PC;
                if (pc == vblank && !system.IsHalted)
                {
                    inVblank = true;
                }

                system.StepInstruction();
                var after = system.GetDebugSnapshot();
                if (pc == scx || pc == bgp)
                {
                    // The write lands four dots before the instruction ends; next-line dots below 84 count from 456.
                    int line = after.Ly, dot = after.PpuDot - 4;
                    if (dot < 84)
                    {
                        line--;
                        dot += 456;
                    }
                    Assert.InRange(line, 0, 143);
                    Assert.InRange(dot, 256, 539); // Not while drawing.
                    minWrite = Math.Min(minWrite, dot);
                    maxWrite = Math.Max(maxWrite, dot);
                    if (pc == scx)
                    {
                        writes[line + 1]++;
                    }
                }
                if (pc == vblankReturn && inVblank)
                {
                    // The handler must end before line 0's HBlank, whose STAT handler prepares line 1.
                    var position = after.Ly >= 144 ? ((after.Ly - 144) * 456) + after.PpuDot : 4560 + (after.Ly * 456) + after.PpuDot;
                    Assert.True(after.Ly >= 144 || after.PpuDot < 254, $"VBlank handler ended at LY {after.Ly} dot {after.PpuDot}");
                    maxVblankEnd = Math.Max(maxVblankEnd, position);
                    handlers++;
                    inVblank = false;
                }
            }
            Assert.All(writes[1..], count => Assert.True(count >= 19)); // Every line, every frame.
        }
        Assert.True(handlers >= 50);
        Assert.Equal(0, Observe(system)[0x0D]);

        // Worst case: HBlank starts by dot 373 and the BGP write follows within 150 dots.
        Assert.InRange(maxWrite, minWrite, 523);
        output.WriteLine($"raster writes at dots {minWrite}-{maxWrite}; latest VBlank handler end {maxVblankEnd} dots after line 144 began");
    }

    [Fact]
    public void TimedInputsReplayAcrossBudgetsAndRestoredMidHandlerState()
    {
        var inputs = ReferenceInputs().Where(input => input.Tick < 330UL * 70_224).ToArray();
        var stepped = Boot(0);
        var budgeted = Boot(0);
        var odd = Boot(0);
        var images = new HashSet<string>();
        foreach (var (tick, mask) in inputs)
        {
            while (stepped.TotalTCycles < tick)
            {
                Assert.True(stepped.StepInstruction().ExecutedTCycles > 0);
            }

            var time = stepped.TotalTCycles;
            AdvanceTo(budgeted, time, 4096);
            AdvanceTo(odd, time, 997);
            foreach (var system in new[] { stepped, budgeted, odd })
            {
                Hold(system, mask);
            }

            Compare(stepped, budgeted);
            Compare(stepped, odd);
            var pixels = new byte[VideoOutput.BufferSize];
            stepped.Video.CopyLatestFrame(pixels);
            images.Add(Convert.ToHexStringLower(SHA256.HashData(pixels)));
        }
        Assert.True(images.Count >= 8, "The replay must reach several different images.");

        // Save inside the STAT handler while a plasma tile upload is pending, then replay a scene change.
        var system2 = Boot();
        Frame(system2, Select);
        for (var frame = 0; frame < 5; frame++)
        {
            Frame(system2, 0);
        }

        Frame(system2, A);
        Frame(system2, 0);
        while (system2.GetDebugSnapshot().PC != Symbols.Value["StatIsr.bgp"])
        {
            system2.StepInstruction();
        }

        Assert.True(Observe(system2)[0x0E] > 0);
        StateTests.ReplayTwice(system2, s =>
        {
            s.RunForTCycles(100_000);
            Hold(s, Select);
            s.RunForTCycles(140_448);
            Hold(s, Up);
            s.RunForTCycles(300_000);
            Hold(s, 0);
            s.RunForTCycles(70_224);
        });
        Assert.Equal(2, Observe(system2)[0]);
    }

    [Fact]
    public void FramesMatchPinnedBinjgbReferences()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "reference.json")));
        var document = manifest.RootElement;
        Assert.Equal(RomSha256, document.GetProperty("rom").GetProperty("sha256").GetString());
        Assert.Equal("v0.1.11", document.GetProperty("renderer").GetProperty("version").GetString());
        var frameTicks = document.GetProperty("frameTicks").GetUInt64();
        var inputs = ReferenceInputs();
        var system = Boot(0);
        int next = 0, compared = 0;
        var closest = ulong.MaxValue;
        foreach (var frame in document.GetProperty("frames").EnumerateArray())
        {
            var number = frame.GetProperty("frame").GetInt32();
            var png = File.ReadAllBytes(Path.Combine(Root, frame.GetProperty("file").GetString()!));
            Assert.Equal(frame.GetProperty("pngSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(png)));
            var reference = DmgAcid2Tests.DecodeReference(png);
            var levels = new byte[160 * 144];
            for (var i = 0; i < levels.Length; i++)
            {
                levels[i] = (byte)(reference[i * 4] / 85);
            }

            Assert.Equal(frame.GetProperty("pixelSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(levels)));

            // Runs to the first frame that completes at or after the frame's selection time.
            var target = (ulong)number * frameTicks;
            while (system.TotalTCycles < target)
            {
                Assert.True(system.StepInstruction().ExecutedTCycles > 0);
                Apply(system, inputs, ref next);
            }
            var completed = system.Video.CompletedFrameCount;
            while (system.Video.CompletedFrameCount == completed)
            {
                Assert.True(system.TotalTCycles < target + (2 * frameTicks), $"frame {number} did not complete");
                system.StepInstruction();
                Apply(system, inputs, ref next);
            }
            Assert.True(system.TotalTCycles - target > 64, $"frame {number} completes too close to its selection time");
            closest = Math.Min(closest, system.TotalTCycles - target);
            var actual = new byte[VideoOutput.BufferSize];
            system.Video.CopyLatestFrame(actual);
            var different = 0;
            for (var i = 0; i < actual.Length; i++)
            {
                if (actual[i] != reference[i])
                {
                    different++;
                }
            }

            if (different != 0)
            {
                WriteDiagnostic($"megademo-{number}.bgra", actual);
            }

            Assert.True(different == 0, $"frame {number} ({frame.GetProperty("description").GetString()}): {different} bytes differ");
            compared++;
        }
        Assert.Equal(19, compared);
        Assert.Equal(0, Observe(system)[0x0D]);
        output.WriteLine($"19 frames match; the closest completes {closest} T after its selection time");
    }

    [Theory]
    [InlineData(Benchmark.ExecutionCase.MegaWave, 0)]
    [InlineData(Benchmark.ExecutionCase.MegaPlasma, 1)]
    [InlineData(Benchmark.ExecutionCase.MegaOrbit, 2)]
    public void PerformanceWorkloadsAnimateEachEffectAtMaximumAmount(Benchmark.ExecutionCase scenario, int scene)
    {
        var workload = new Benchmark.ExecutionWorkload(scenario);
        Assert.Equal(new byte[] { (byte)scene, 3, 7, 0, 0, 1 }, Observe(workload.System)[..6]);
        var frames = workload.System.Video.CompletedFrameCount;
        var phase = Phase(Observe(workload.System));
        workload.Run();
        var o = Observe(workload.System);
        Assert.Equal(scene, o[0]);
        Assert.Equal(0, o[0x0D]);
        Assert.NotEqual(phase, Phase(o));
        Assert.InRange(workload.System.Video.CompletedFrameCount - frames, 238UL, 240UL); // Four seconds, no LCD restart.
    }

    private static void Apply(GameBoySystem system, (ulong Tick, byte Mask)[] inputs, ref int next)
    {
        while (next < inputs.Length && system.TotalTCycles >= inputs[next].Tick)
        {
            Hold(system, inputs[next++].Mask);
        }
    }

    private static (ulong Tick, byte Mask)[] ReferenceInputs()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "reference.json")));
        return manifest.RootElement.GetProperty("inputs").EnumerateArray().Select(input =>
        {
            byte mask = 0;
            foreach (var button in input.GetProperty("buttons").EnumerateArray())
            {
                mask |= (byte)(1 << (int)Enum.Parse<JoypadButton>(button.GetString()!));
            }

            return (input.GetProperty("tick").GetUInt64(), mask);
        }).ToArray();
    }

    private static GameBoySystem Boot(int frames = 30)
    {
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(Image.Value).Cartridge);
        if (frames > 0)
        {
            RunFrames(system, frames);
        }

        return system;
    }

    private static void Hold(GameBoySystem system, byte mask)
    {
        for (var i = 0; i < 8; i++)
        {
            system.Joypad.SetButtonState((JoypadButton)i, ((mask >> i) & 1) != 0);
        }
    }

    // Holds the buttons for one frame and returns the observation block at its end.
    private static byte[] Frame(GameBoySystem system, byte mask)
    {
        Hold(system, mask);
        RunFrames(system, 1);
        return Observe(system);
    }

    // Stops right after the instruction that completes the frame, before its VBlank handler runs.
    private static void RunFrames(GameBoySystem system, int count)
    {
        ulong target = system.Video.CompletedFrameCount + (ulong)count, limit = system.TotalTCycles + ((ulong)count * 2 * 70_224);
        while (system.Video.CompletedFrameCount < target)
        {
            var result = system.Video.CompletedFrameCount + 1 < target ? system.RunForTCycles(4096)
                : system.GetDebugSnapshot().Ly < 140 ? system.RunForTCycles(228) : system.StepInstruction();
            Assert.True(result.ExecutedTCycles > 0 && system.TotalTCycles < limit, $"No frame by T={system.TotalTCycles}");
        }
    }

    private static byte[] Observe(GameBoySystem system)
    {
        var block = new byte[0x50];
        system.CopyMemory(0xC000, block);
        return block;
    }

    private static int Phase(byte[] observation) => observation[6] | (observation[7] << 8);

    private static void AdvanceTo(GameBoySystem system, ulong time, int chunk)
    {
        while (system.TotalTCycles < time)
        {
            Assert.True(system.RunForTCycles((int)Math.Min((ulong)chunk, time - system.TotalTCycles)).ExecutedTCycles > 0);
        }

        Assert.Equal(time, system.TotalTCycles);
    }

    private static void Compare(GameBoySystem left, GameBoySystem right)
    {
        // Compares the whole state, then the debug view, memory and latest frame.
        StateTests.EqualState(left.CaptureState(), right.CaptureState());
        Assert.Equal(left.GetDebugSnapshot(), right.GetDebugSnapshot());
        byte[] a = new byte[65536], b = new byte[65536];
        left.CopyMemory(0, a);
        right.CopyMemory(0, b);
        Assert.Equal(a, b);
        a = new byte[VideoOutput.BufferSize];
        b = new byte[VideoOutput.BufferSize];
        Assert.Equal(left.Video.CopyLatestFrame(a), right.Video.CopyLatestFrame(b));
        Assert.Equal(a, b);
    }

    private static void WriteDiagnostic(string name, byte[] bytes)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name), bytes);
    }

    private void AssertFrame(GameBoySystem system, byte[] shades, string name)
    {
        var actual = new byte[VideoOutput.BufferSize];
        system.Video.CopyLatestFrame(actual);
        int different = 0, first = -1;
        for (var i = 0; i < shades.Length; i++)
        {
            if (actual[i * 4] != 255 - (shades[i] * 85))
            {
                different++;
                if (first < 0)
                {
                    first = i;
                }
            }
        }

        if (different != 0)
        {
            WriteDiagnostic($"megademo-{name}.bgra", actual);
        }

        Assert.True(different == 0, $"{name}: {different} pixels differ, first ({first % 160},{first / 160})");
        output.WriteLine($"{name}: all 23040 pixels match the formulas");
    }

    private static void AssertStatus(GameBoySystem system, int scene, int speed, int amount, int pattern, int direction, bool running)
    {
        var rows = Spec.StatusRows(scene, speed, amount, pattern, direction, running);
        var map = new byte[52];
        system.CopyMemory(0x9C00, map);
        Assert.Equal(rows[0], System.Text.Encoding.ASCII.GetString(map, 0, 20));
        Assert.Equal(rows[1], System.Text.Encoding.ASCII.GetString(map, 32, 20));
    }

    private static void AssertOam(GameBoySystem system, int angle, int spread, int pattern)
    {
        var oam = new byte[160];
        system.CopyMemory(0xFE00, oam);
        for (var ring = 0; ring < 5; ring++)
        {
            for (var k = 0; k < 8; k++)
            {
                int index = ((ring * 8) + k) * 4, level = Spec.RingLevel(spread, ring), a = (((ring + 1) * angle) + (32 * k)) & 255;
                var flip = (k & 1) == 0 ? 0 : ((pattern & 1) != 0 ? 0x20 : 0) | ((pattern & 2) != 0 ? 0x40 : 0);
                byte[] expected = [(byte)(76 + Spec.Isin(a, 8 * level)), (byte)(84 + Spec.Isin((a + 64) & 255, 8 * level)),
                    (byte)(128 + ((ring + pattern) & 3)), (byte)(((ring & 1) != 0 ? 0x10 : 0) | ((ring == 2 ? 0x80 : 0) ^ flip))];
                Assert.Equal(expected, oam[index..(index + 4)]);
            }
        }
    }

    // The wave frame for the angle and status bar parameters in the observation block.
    private static byte[] ExpectedWave(byte[] o, int amount, int pattern)
    {
        int angle = o[0x0F];
        var palettes = new byte[128];
        var basePalette = Spec.WaveBasePalettes[pattern];
        Array.Fill(palettes, basePalette);
        for (var bar = 0; bar < 2; bar++)
        {
            var top = 64 + Spec.Isin(((2 * angle) + (128 * bar)) & 255, 48) - 7;
            for (var i = 0; i < 15; i++)
            {
                palettes[top + i] = Spec.BarPalette(basePalette, bar == 1, i);
            }
        }
        return Compose(o, (x, y) =>
        {
            var scx = (Spec.Isin(((2 * y) + angle) & 255, 8 * amount) >> 1) & 255;
            return Shade(palettes[y], Spec.WaveColor((x + scx) & 255, y));
        });
    }

    private static byte[] ExpectedPlasma(byte[] o, int strength, int pattern)
    {
        int angle = o[0x0F];
        var scy = Spec.Isin(angle, 24) & 255;
        var palette = Spec.PlasmaPalette(strength, (angle >> 5) & 3);
        return Compose(o, (x, y) =>
        {
            var scx = (angle + (Spec.Isin(((4 * y) + (2 * angle)) & 255, 16) >> 2)) & 255;
            int bx = (x + scx) & 255, by = (y + scy) & 255;
            var level = Spec.PlasmaLevel(bx >> 3, by >> 3);
            return Shade(palette, Spec.PlasmaTexture(pattern, level, bx & 7, by & 7));
        });
    }

    // Spread 0 stacks all 40 objects at the centre; only the first ten in OAM are drawn.
    private static byte[] ExpectedOrbitAtCentre(byte[] o) => Compose(o, (x, y) =>
    {
        int angle = o[0x0F];
        var scx = (angle * (((y >> 3) % 3) + 1)) & 255;
        var background = Spec.StarColor((x + scx) & 255, y);
        var shade = Shade(0xE4, background);
        if (x is < 76 or > 83 || y is < 60 or > 67)
        {
            return shade;
        }

        for (var index = 0; index < 10; index++)
        {
            var ring = index / 8;
            var color = Spec.TileColor(Symbols.Value["ObjectTiles"], ring, x - 76, y - 60);
            if (color != 0)
            {
                return Shade(ring == 0 ? (byte)0xE4 : (byte)0x6C, color);
            }
        }
        return shade;
    });

    private static byte[] Compose(byte[] o, Func<int, int, int> effect)
    {
        var rows = Spec.StatusRows(o[0], o[1], o[2], o[3], o[4], o[5] != 0);
        var shades = new byte[160 * 144];
        for (var y = 0; y < 144; y++)
        {
            for (var x = 0; x < 160; x++)
            {
                shades[(y * 160) + x] = (byte)(y < 128 ? effect(x, y) : Spec.StatusShade(x, y - 128, rows));
            }
        }

        return shades;
    }

    private static int Shade(byte palette, int color) => (palette >> (2 * color)) & 3;

    private static class Spec
    {
        internal static readonly byte[] WaveBasePalettes = [0xE4, 0x1B, 0x54, 0xFC];
        private const string TextRow3 = "  STUDY EMULATOR * MEGA DEMO *  ";
        private const string TextRow7 = " SM83 * WAVE SCROLL * RASTER *  ";

        // Bhaskara I's rational sine, 256 angle units per turn, rounded to the nearest integer.
        internal static int Isin(int angle, int amplitude)
        {
            int h = angle & 127, p = h * (128 - h), d = 81920 - (4 * p);
            var v = ((2 * amplitude * 16 * p) + d) / (2 * d);
            return (angle & 128) != 0 ? -v : v;
        }

        internal static byte BarPalette(byte basePalette, bool bright, int line)
        {
            int k = (7 - Math.Abs(line - 7)) >> 1, result = 0;
            for (var color = 0; color < 4; color++)
            {
                var shade = (basePalette >> (2 * color)) & 3;
                shade = bright ? Math.Max(0, shade - k) : Math.Min(3, shade + k);
                result |= shade << (2 * color);
            }
            return (byte)result;
        }

        internal static byte PlasmaPalette(int strength, int rotation)
        {
            var result = 0;
            for (var color = 0; color < 4; color++)
            {
                result |= ((21 + (((2 * ((color + rotation) & 3)) - 3) * strength) + 7) / 14) << (2 * color);
            }

            return (byte)result;
        }

        internal static int RingLevel(int spread, int ring) => Math.Min(7, ((spread * (ring + 2)) + 3) / 6);

        internal static int PlasmaLevel(int tx, int ty) =>
            ((Isin(tx * 16, 64) + Isin(ty * 24, 64) + Isin(((3 * tx) + (2 * ty)) * 8, 64) + Isin((tx - ty) * 16, 64) + 256) >> 5) & 15;

        internal static int PlasmaTexture(int set, int level, int x, int y)
        {
            var threshold = set switch { 0 => (2 * (x & 1)) ^ (3 * (y & 1)), 1 => y & 3, 2 => x & 3, _ => (x + y) & 3 };
            return threshold < (level & 3) ? ((level >> 2) + 1) & 3 : level >> 2;
        }

        internal static int WaveColor(int bx, int by)
        {
            int tx = bx >> 3, ty = by >> 3, x = bx & 7, y = by & 7;
            if (ty % 8 is 3 or 7)
            {
                return TileColor(Symbols.Value["FontTiles"], (ty % 8 == 3 ? TextRow3 : TextRow7)[tx] - 32, x, y);
            }

            var tile = ((tx + ty) & 1) + (2 * ((ty >> 2) & 1));
            if (tile == 0)
            {
                return ((x + y) >> 1) & 3;
            }

            if (tile == 1)
            {
                return ((x + 7 - y) >> 1) & 3;
            }

            var diamond = ((Math.Abs((2 * x) - 7) + Math.Abs((2 * y) - 7)) >> 2) & 3;
            return tile == 3 ? 3 - diamond : diamond;
        }

        internal static int StarColor(int bx, int by)
        {
            int tx = bx >> 3, ty = by >> 3;
            var hash = (((tx * 73) + (ty * 151)) ^ (tx * ty * 29) ^ (ty * 7)) & 63;
            return hash < 8 ? TileColor(Symbols.Value["StarTiles"], hash & 3, bx & 7, by & 7) : 0;
        }

        // Color of one pixel of a 2bpp tile stored in the ROM.
        internal static int TileColor(ushort address, int tile, int x, int y)
        {
            var offset = address + (tile * 16) + (y * 2);
            return ((Image.Value[offset] >> (7 - x)) & 1) | (((Image.Value[offset + 1] >> (7 - x)) & 1) << 1);
        }

        internal static string[] StatusRows(int scene, int speed, int amount, int pattern, int direction, bool running)
        {
            string[] names = ["WAVE", "PLASMA", "ORBIT"], amounts = ["AMP", "STR", "SPR"], patterns = ["PAL", "PAT", "PAT"];
            return [$"{scene + 1}:{names[scene]}".PadRight(15) + (running ? "RUN " : "STOP") + " ",
                $"SPD{speed} {amounts[scene]}{amount} {patterns[scene]}{pattern} DIR{(direction == 0 ? '+' : '-')} "];
        }

        // Window lines 0-15: white text (color 3) on a black bar through palette $1B.
        internal static int StatusShade(int x, int line, string[] rows) =>
            (0x1B >> (2 * TileColor(Symbols.Value["FontTiles"], rows[line >> 3][x >> 3] - 32, x & 7, line & 7))) & 3;
    }
}
