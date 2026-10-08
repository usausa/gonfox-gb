namespace GonFox.GameBoy.Core;

using System.Security.Cryptography;
using System.Text.Json;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Video;

// Plays the RPG demo USAGI QUEST against its documented rules and pinned reference frames.
[Trait("Category", "Rom")]
public sealed partial class RpgDemoTests(ITestOutputHelper output)
{
    private const string RomSha256 = "f4273fef8d77505bf0d6ad81ad3dac0083e94a7e034331452c9274c6eea74644";
    private const byte Right = 1;
    private const byte Left = 2;
    private const byte Up = 4;
    private const byte Down = 8;
    private const byte A = 16;
    private const byte B = 32;
    private const byte Select = 64;
    private const byte Start = 128;

    // Offsets in the observation block at C100.
    private const int OScene = 0;
    private const int OPhase = 1;
    private const int OHero = 3;
    private const int OMode = 4;
    private const int OCursor = 5;
    private const int OEnemyHp = 6;
    private const int OEnemyMaxHp = 7;
    private const int OHeroHp = 8;
    private const int OMp = 0x10;
    private const int OTurn = 0x11;
    private const int ONumber = 0x12;
    private const int OGuard = 0x13;
    private const int OResult = 0x14;
    private const int OPresses = 0x15;
    private const int OKeyHeld = 0x17;
    private const int OSong = 0x18;
    private const int TitleIdle = 3;
    private const int TitleMenu = 4;
    private const int HeroMaxHp = 50;
    private const int HeroMaxMp = 3;
    private const int BgpNormal = 0xE4;
    private const int VBlankTicks = 10 * 456;
    private static readonly string[] HeroNames = ["SERA", "PINA", "SHIRO", "KURO", "YUKI", "AKANE", "YORU", "HANA"];

    // Per difficulty: enemy level, max HP, attack base and range, special every N turns (0 never), EXP.
    private static readonly int[][] Enemies = [[5, 30, 3, 4, 0, 40], [25, 60, 5, 5, 4, 120], [99, 99, 7, 6, 3, 250]];
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "TestData", "RpgDemo");
    private static readonly Lazy<byte[]> Image = new(() =>
    {
        var rom = File.ReadAllBytes(Path.Combine(Root, "rpgdemo.gb"));
        Assert.Equal(RomSha256, Convert.ToHexStringLower(SHA256.HashData(rom)));
        return rom;
    });

    // Label addresses from the rgblink .sym file, whose lines read "bank:address label".
    private static readonly Lazy<Dictionary<string, ushort>> Symbols = new(() => File.ReadAllLines(Path.Combine(Root, "rpgdemo.sym"))
        .Where(line => line.Length > 0 && line[0] != ';').Select(line => line.Split(' '))
        .ToDictionary(parts => parts[1], parts => Convert.ToUInt16(parts[0][3..], 16)));

    [Fact]
    public void RomHeaderSymbolsAndObservationBlockArePinned()
    {
        var rom = Image.Value;
        Assert.Equal(0x8000, rom.Length);
        Assert.Equal("USAGI QUEST", System.Text.Encoding.ASCII.GetString(rom, 0x134, 11));
        Assert.All(rom[0x13F..0x147], value => Assert.Equal(0, value)); // No CGB or SGB flag.
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
        string[] observed = ["wScene", "wPhase", "wFrame", "wHero", "wMode", "wCursor", "wEnemyHp", "wEnemyMaxHp",
            "wHeroHp", "wHeroHp", "wHeroHp", "wHeroHp", "wHeroHp", "wHeroHp", "wHeroHp", "wHeroHp",
            "wHeroMp", "wTurn", "wNumber", "wGuard", "wResult", "wPresses", "wPresses", "wKeyHeld", "wSongId", "wNextScene"];
        for (var i = 0; i < observed.Length; i++)
        {
            if (i == 0 || observed[i] != observed[i - 1])
            {
                Assert.Equal(0xC100 + i, Symbols.Value[observed[i]]);
            }
        }

        var isr = Symbols.Value["VBlankIsr"];
        Assert.Equal(new byte[] { 0xC3, (byte)isr, (byte)(isr >> 8) }, rom[0x40..0x43]); // JP VBlankIsr.
        Assert.Equal(Enemies.SelectMany(enemy => enemy.Select(value => (byte)value)), Rom("EnemyStats", 18));
        foreach (var label in new[] { "NoteTable", "Songs", "SfxTable", "HeroNames", "FontTiles", "hRandom", "hBGP" })
        {
            Assert.True(Symbols.Value.ContainsKey(label), label);
        }
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
        int next = 0, compared = 0, handlers = 0, latest = 0;
        var closest = ulong.MaxValue;
        var vramDone = Symbols.Value["VBlankIsr.vramDone"];
        void Step()
        {
            var cpu = system.GetDebugSnapshot();
            if (cpu.PC == vramDone)
            {
                handlers++;
                latest = Math.Max(latest, SinceVBlank(cpu));
            }
            Assert.True(system.StepInstruction().ExecutedTCycles > 0);
            Apply(system, inputs, ref next);
        }
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
                Step();
            }

            var completed = system.Video.CompletedFrameCount;
            while (system.Video.CompletedFrameCount == completed)
            {
                Assert.True(system.TotalTCycles < target + (2 * frameTicks), $"frame {number} did not complete");
                Step();
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
                WriteDiagnostic($"rpgdemo-{number}.bgra", actual);
            }

            Assert.True(different == 0, $"frame {number} ({frame.GetProperty("description").GetString()}): {different} bytes differ");
            compared++;
        }
        Assert.Equal(22, compared);
        Assert.InRange(handlers, 1_900, 2_000); // One per shown frame.
        Assert.InRange(latest, 0, VBlankTicks - 1);
        output.WriteLine($"22 frames match; the closest completes {closest} T after its selection time");
        output.WriteLine($"{handlers} VBlank handlers end their VRAM work by {latest} T of the {VBlankTicks} T of VBlank");
    }

    // Rapid select-screen taps give the most VRAM work per VBlank; it must still end inside VBlank.
    [Fact]
    public void VBlankWorkEndsInsideVBlankAndStreamedPicturesArriveWhole()
    {
        var system = OpenTitleMenu();
        Tap(system, A);
        WaitForScene(system, 1);
        var vramDone = Symbols.Value["VBlankIsr.vramDone"];
        int handlers = 0, latest = 0;
        byte[] taps = [Right, Down, Left, Up, Select, Down, Right, Right, Up, Left, Down, Down, Select, Right, Up];
        for (var round = 0; round < 3; round++)
        {
            for (var frame = 0; frame < 2 * taps.Length; frame++)
            {
                Hold(system, frame % 2 == 0 ? taps[((frame / 2) + (round * 4)) % taps.Length] : (byte)0);
                var completed = system.Video.CompletedFrameCount;
                while (system.Video.CompletedFrameCount == completed)
                {
                    var cpu = system.GetDebugSnapshot();
                    if (cpu.PC == vramDone)
                    {
                        handlers++;
                        latest = Math.Max(latest, SinceVBlank(cpu));
                    }
                    Assert.True(system.StepInstruction().ExecutedTCycles > 0);
                }
            }

            // Once the stream is done, the portrait and enemy pictures match the last choice.
            var o = WaitFor(system, o => StreamLeft(system) == 0 && Picture(system, 0x9000, 256).SequenceEqual(FaceTiles(o[OHero]))
                && Picture(system, 0x9100, 576).SequenceEqual(EnemyTiles(o[OMode])), 60);
            Assert.Equal(HeroNames[o[OHero]], MapText(system, 8, 2, HeroNames[o[OHero]].Length));
        }
        Assert.Equal(6 * taps.Length, handlers);
        Assert.InRange(latest, 0, VBlankTicks - 1);
        output.WriteLine($"select taps: the VRAM work ends by {latest} T of the {VBlankTicks} T of VBlank");
    }

    [Fact]
    public void TitleAndSelectScreensFollowTheDocumentedControls()
    {
        var system = Boot(0);
        var o = Observe(system);
        for (var frame = 0; frame < 100; frame++)
        {
            o = Frame(system, 0);
        }

        Assert.Equal((0, 0, 1), (o[OScene], o[OPhase], o[OSong])); // Still panning, with the title song.
        Tap(system, Start); // Skips the pan and the drop.
        WaitFor(system, observed => observed[OPhase] == TitleIdle, 10);
        Assert.Equal(112, Hram(system, "hSCY"));
        o = Tap(system, Start);
        Assert.Equal((TitleMenu, 0), (o[OPhase], o[OCursor]));
        Assert.Equal(1, Tap(system, Down)[OCursor]);
        Assert.Equal(0, Tap(system, Select)[OCursor]); // SELECT moves the cursor too.
        Assert.Equal(1, Tap(system, Up)[OCursor]);
        Assert.Equal(TitleIdle, Tap(system, B)[OPhase]); // B closes the menu.
        o = Tap(system, A); // A opens it too.
        Assert.Equal((TitleMenu, 0), (o[OPhase], o[OCursor]));
        Tap(system, A);
        o = WaitForScene(system, 1);
        Assert.Equal((0, 0, 2), (o[OHero], o[OMode], o[OSong]));
        Assert.Equal(7, Tap(system, Left)[OHero]); // The eight heroes wrap around.
        Assert.Equal(0, Tap(system, Select)[OHero]);
        Assert.Equal(1, Tap(system, Right)[OHero]);
        Assert.Equal(0, Tap(system, Up)[OMode]); // Stops at EASY.
        Assert.Equal(1, Tap(system, Down)[OMode]);
        Assert.Equal(2, Tap(system, Down)[OMode]);
        Assert.Equal(2, Tap(system, Down)[OMode]); // Stops at NIGHTMARE.
        for (var frame = 0; frame < 4; frame++)
        {
            Frame(system, 0); // Map rows reach VRAM.
        }

        Assert.Equal(">NIGHTMARE", MapText(system, 2, 14, 10));
        Assert.Equal("SHADOWCAT", MapText(system, 10, 15, 9));
        Assert.Equal(("PINA", "HEART BEAM", "HERO 2/8"), (MapText(system, 8, 2, 4), MapText(system, 8, 4, 10), MapText(system, 8, 5, 8)));
        Tap(system, B); // Back to the title.
        o = WaitFor(system, observed => observed[OScene] == 0, 60);
        Assert.Equal((0, 1, 1, 2), (o[OPhase], o[OSong], o[OHero], o[OMode]));
    }

    [Fact]
    public void KeyTestShowsHeldButtonsCountsEveryPressAndPlaysATonePerButton()
    {
        var system = OpenTitleMenu();
        Tap(system, Down);
        Tap(system, A);
        var o = WaitForScene(system, 3);
        Assert.Equal((0, 0, 0), (Presses(o), o[OKeyHeld], o[OSong])); // No song.
        Assert.Equal("COUNT: 00000", MapText(system, 1, 15, 12));
        Assert.Equal(". . . . . . .. ..", MapText(system, 1, 14, 17));

        // Per button: its CH2 tone as a MIDI note and the held-button row it shows.
        (byte Mask, int Note, string Held)[] keys =
        [
            (A, 84, ". . . . A . .. .."), (B, 81, ". . . . . B .. .."), (Select, 76, ". . . . . . SE .."), (Start, 79, ". . . . . . .. ST"),
            (Right, 86, ". . . R . . .. .."), (Left, 83, ". . L . . . .. .."), (Up, 89, "U . . . . . .. .."), (Down, 67, ". D . . . . .. ..")
        ];
        var count = 0;
        foreach (var (mask, note, held) in keys)
        {
            o = Frame(system, mask);
            Assert.Equal((++count, Swap(mask)), (Presses(o), o[OKeyHeld]));
            for (var frame = 0; frame < 4; frame++)
            {
                o = Frame(system, mask); // Held: no new count.
            }

            Assert.Equal(count, Presses(o));
            Assert.Equal(held, MapText(system, 1, 14, 17));
            Assert.Equal($"COUNT: {count:D5}", MapText(system, 1, 15, 12));
            var state = system.CaptureState();
            Assert.Equal(PulsePeriod(note), state.Apu.Pulse2.Period);
            Assert.Equal(0xC2, state.Apu.Pulse2.Envelope.Register);
            Frame(system, 0);
            Assert.Equal(0, Frame(system, 0)[OKeyHeld]);
        }

        // UP and A at once: two presses, the tone of A, both names.
        o = Frame(system, Up | A);
        Assert.Equal(count + 2, Presses(o));
        for (var frame = 0; frame < 4; frame++)
        {
            Frame(system, Up | A);
        }

        Assert.Equal(PulsePeriod(84), system.CaptureState().Apu.Pulse2.Period);
        Assert.Equal("U . . . A . .. ..", MapText(system, 1, 14, 17));
        Frame(system, 0);

        // START and SELECT together go back to the title.
        Frame(system, Start | Select);
        o = WaitFor(system, observed => observed[OScene] == 0, 60);
        Assert.Equal(1, o[OSong]);
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

    // Boots to the title menu, using START to skip the intro and open the menu.
    private static GameBoySystem OpenTitleMenu()
    {
        var system = Boot();
        Tap(system, Start);
        WaitFor(system, o => o[OPhase] == TitleIdle, 10);
        var o = Tap(system, Start);
        Assert.Equal((TitleMenu, 0), (o[OPhase], o[OCursor]));
        return system;
    }

    // Chooses GAME START, the hero and the difficulty, then starts the battle.
    private static GameBoySystem StartBattle(int hero, int mode)
    {
        var system = OpenTitleMenu();
        Tap(system, A);
        WaitForScene(system, 1);
        for (var i = 0; i < hero; i++)
        {
            Tap(system, Right);
        }

        for (var i = 0; i < mode; i++)
        {
            Tap(system, Down);
        }

        var o = Observe(system);
        Assert.Equal((hero, mode), (o[OHero], o[OMode]));
        Tap(system, A);
        return system;
    }

    // Waits for the scene's fade-in to end, after which input counts.
    private static byte[] WaitForScene(GameBoySystem system, int scene)
    {
        WaitFor(system, o => o[OScene] == scene && o[OPhase] == 1, 120);
        for (var frame = 0; frame < 16; frame++)
        {
            Frame(system, 0);
        }

        Assert.Equal(BgpNormal, Hram(system, "hBGP"));
        return Observe(system);
    }

    private static byte[] WaitFor(GameBoySystem system, Func<byte[], bool> done, int limit)
    {
        for (var frame = 0; frame < limit; frame++)
        {
            var o = Frame(system, 0);
            if (done(o))
            {
                return o;
            }
        }
        Assert.Fail($"not reached in {limit} frames");
        return [];
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

    // A press of one frame, then one frame released.
    private static byte[] Tap(GameBoySystem system, byte mask)
    {
        Frame(system, mask);
        return Frame(system, 0);
    }

    // Stops right after the instruction that completes the frame, before its VBlank handler runs.
    private static void RunFrames(GameBoySystem system, int count)
    {
        ulong target = system.Video.CompletedFrameCount + (ulong)count, limit = system.TotalTCycles + ((ulong)(count + 20) * 70_224);
        while (system.Video.CompletedFrameCount < target)
        {
            var result = system.Video.CompletedFrameCount + 1 < target ? system.RunForTCycles(4096)
                : system.GetDebugSnapshot().Ly < 140 ? system.RunForTCycles(228) : system.StepInstruction();
            Assert.True(result.ExecutedTCycles > 0 && system.TotalTCycles < limit, $"No frame by T={system.TotalTCycles}");
        }
    }

    private static byte[] Observe(GameBoySystem system)
    {
        var block = new byte[0x1A];
        system.CopyMemory(0xC100, block);
        return block;
    }

    private static byte Hram(GameBoySystem system, string label)
    {
        var value = new byte[1];
        system.CopyMemory(Symbols.Value[label], value);
        return value[0];
    }

    private static int Presses(byte[] o) => o[OPresses] | (o[OPresses + 1] << 8);

    // T-cycles since VBlank began, or int.MaxValue outside VBlank.
    private static int SinceVBlank(DebugSnapshot cpu)
    {
        var line = cpu.PpuMode == 1 && cpu.Ly < 144 ? 153 : cpu.Ly;
        return line >= 144 ? ((line - 144) * 456) + cpu.PpuDot : int.MaxValue;
    }

    private static int StreamLeft(GameBoySystem system)
    {
        var left = new byte[2];
        system.CopyMemory(Symbols.Value["wStreamLeft"], left);
        return left[0] | (left[1] << 8);
    }

    private static byte[] Picture(GameBoySystem system, ushort address, int length)
    {
        var bytes = new byte[length];
        system.CopyMemory(address, bytes);
        return bytes;
    }

    private static byte[] FaceTiles(int hero) => Rom("HeroFaceTiles", 8 * 256)[(hero * 256)..((hero * 256) + 256)];

    private static byte[] EnemyTiles(int mode) => Rom("EnemyTiles", 3 * 576)[(mode * 576)..((mode * 576) + 576)];

    // The ROM's pad byte keeps the buttons in the low nibble and the D-pad in the high one.
    private static byte Swap(byte mask) => (byte)((mask << 4) | (mask >> 4));

    // The characters on BG map row y from column x (font tiles are ASCII + 60h).
    private static string MapText(GameBoySystem system, int x, int y, int length)
    {
        var tiles = new byte[length];
        system.CopyMemory((ushort)(0x9800 + (y * 32) + x), tiles);
        return new string([.. tiles.Select(tile => tile is >= 0x80 and < 0xE0 ? (char)(tile - 0x60) : '?')]);
    }

    private static byte[] Rom(string label, int length) => Image.Value[Symbols.Value[label]..(Symbols.Value[label] + length)];

    // Equal temperament (A4 = 440 Hz): the pulse period register for a MIDI note.
    private static int PulsePeriod(int note) => (int)Math.Round(2048 - (131072 / (440 * Math.Pow(2, (note - 69) / 12.0))));

    private static void WriteDiagnostic(string name, byte[] bytes)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name), bytes);
    }
}
