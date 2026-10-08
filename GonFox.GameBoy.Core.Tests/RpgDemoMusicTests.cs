namespace GonFox.GameBoy.Core;

// RPG demo music: a model of the sequencer predicts every row, note and drum the ROM plays.
public sealed partial class RpgDemoTests
{
    private const int SongTitle = 1;
    private const int SongSelect = 2;

    // A song header: tempo, loop, channel instruments and drum kit, then its order of 16-row patterns.
    private sealed record Song(int Tempo, bool Loop, byte[] Instruments, byte[] Kit, ushort[] Patterns);

    private static int Word(byte[] bytes, int address) => bytes[address] | (bytes[address + 1] << 8);

    private static Song ReadSong(int id)
    {
        var rom = Image.Value;
        int header = Word(rom, Symbols.Value["Songs"] + (id * 2)), kit = Word(rom, header + 8);
        var patterns = new List<ushort>();
        for (var order = header + 10; Word(rom, order) != 0; order += 2)
        {
            patterns.Add((ushort)Word(rom, order));
        }

        return new Song(rom[header], rom[header + 1] != 0, rom[(header + 2)..(header + 8)], rom[kit..(kit + 6)], [.. patterns]);
    }

    [Fact]
    public void NoteTableFollowsEqualTemperament()
    {
        var table = Rom("NoteTable", 61 * 2);
        for (var note = 36; note <= 96; note++)
        {
            Assert.Equal(PulsePeriod(note), Word(table, (note - 36) * 2));
        }
    }

    // Each song's tempo in BPM (rows are sixteenth notes), its scale, pattern count and whether it loops.
    [Theory]
    [InlineData(1, 119, true, 8, new[] { 0, 2, 4, 5, 7, 9, 11 })]
    [InlineData(2, 94, true, 4, new[] { 5, 7, 9, 10, 0, 2, 4 })]
    [InlineData(3, 150, true, 8, new[] { 9, 11, 0, 2, 4, 5, 7, 8 })]
    [InlineData(4, 140, false, 2, new[] { 0, 2, 4, 5, 7, 9, 11 })]
    [InlineData(5, 77, false, 2, new[] { 9, 11, 0, 2, 4, 5, 7, 8 })]
    public void SongsHaveTheDocumentedTempoKeyAndLoop(int id, int bpm, bool loop, int patterns, int[] scale)
    {
        var song = ReadSong(id);
        Assert.Equal(bpm, (int)Math.Round(4_194_304.0 / 70_224 * song.Tempo / 256 * 15));
        Assert.Equal((loop, patterns), (song.Loop, song.Patterns.Length));
        foreach (var pattern in song.Patterns)
        {
            var sounds = new bool[3];
            for (var row = 0; row < 16; row++)
            {
                var step = Image.Value[(pattern + (row * 4))..(pattern + (row * 4) + 4)];
                Assert.InRange(step[3], 0, 3); // No drum, kick, snare, hat.
                for (var channel = 0; channel < 3; channel++)
                {
                    if (step[channel] < 2)
                    {
                        continue; // Hold, or silence.
                    }

                    Assert.InRange(step[channel] + 34, 36, 96); // In the note table.
                    Assert.Contains((step[channel] + 34) % 12, scale);
                    sounds[channel] = true;
                }
            }
            Assert.All(sounds, Assert.True); // Every channel plays.
        }
    }

    // Models the sequencer: each VBlank adds the tempo to a fraction whose carry plays the next row.
    private sealed class Sequencer(Song song, int order, int row, int fraction)
    {
        internal int Order { get; private set; } = order;
        internal int Row { get; private set; } = row;
        internal int Fraction { get; private set; } = fraction;
        internal int Drum { get; private set; }
        internal bool Active { get; private set; } = true;
        internal int[] Notes { get; } = new int[3]; // Melody, harmony, bass.

        // One VBlank. Returns the row played (melody, harmony, bass, drum), or null.
        internal byte[]? Frame()
        {
            Drum = 0;
            if (!Active)
            {
                return null;
            }

            Fraction += song.Tempo;
            if (Fraction < 256)
            {
                return null;
            }

            Fraction -= 256;
            Row = (Row + 1) & 15;
            if (Row == 0 && ++Order == song.Patterns.Length)
            {
                Order = 0;
                if (!song.Loop)
                {
                    Active = false;
                    return null;
                }
            }
            var step = Image.Value[(song.Patterns[Order] + (Row * 4))..(song.Patterns[Order] + (Row * 4) + 4)];
            for (var channel = 0; channel < 3; channel++)
            {
                if (step[channel] != 0)
                {
                    Notes[channel] = step[channel];
                }
            }

            Drum = step[3];
            return step;
        }
    }

    private static Sequencer FollowSong(GameBoySystem system, int id, out Song song)
    {
        song = ReadSong(id);
        Assert.Equal(id, Observe(system)[OSong]);
        int start = Word(Picture(system, Symbols.Value["wOrderStart"], 2), 0), next = Word(Picture(system, Symbols.Value["wOrderPtr"], 2), 0);
        return new Sequencer(song, ((next - start) / 2) - 1, Picture(system, Symbols.Value["wRow"], 1)[0], Picture(system, Symbols.Value["wTempoAcc"], 1)[0]);
    }

    // Checks the ROM's sequencer and APU against the model; harmony and drums unless effects use them.
    private static void AssertSong(GameBoySystem system, Sequencer model, Song song, bool harmonyAndDrums)
    {
        Assert.Equal((model.Row, model.Fraction, model.Active ? 1 : 0),
            (Picture(system, Symbols.Value["wRow"], 1)[0], Picture(system, Symbols.Value["wTempoAcc"], 1)[0], Picture(system, Symbols.Value["wSongActive"], 1)[0]));
        if (model.Active)
        {
            Assert.Equal(song.Patterns[model.Order], Word(Picture(system, Symbols.Value["wPattern"], 2), 0));
        }

        var state = system.CaptureState();
        var instruments = song.Instruments;
        if (model.Notes[0] >= 2)
        {
            Assert.Equal(PulsePeriod(model.Notes[0] + 34), state.Apu.Pulse1.Period);
            Assert.Equal((instruments[0] >> 6, instruments[1]), (state.Apu.Pulse1.Duty, state.Apu.Pulse1.Envelope.Register));
        }
        else if (model.Notes[0] == 1)
        {
            Assert.Equal(0, state.Apu.Pulse1.Envelope.Register);
        }

        if (model.Notes[2] >= 2)
        {
            Assert.Equal(PulsePeriod(model.Notes[2] + 34), state.Apu.Wave.Period);
            Assert.Equal((instruments[4] >> 5) & 3, (state.Apu.Registers[0x0C] >> 5) & 3); // NR32: 1 full, 2 half, 3 quarter.
            Assert.Equal(Rom("WaveTables", 48)[(instruments[5] * 16)..((instruments[5] * 16) + 16)], state.Apu.WaveRam);
        }
        else if (model.Notes[2] == 1)
        {
            Assert.False(state.Apu.Wave.DacOn);
        }

        Assert.Equal((0x77, 0xFF), (state.Apu.Registers[0x14], state.Apu.Registers[0x15]));
        if (!harmonyAndDrums)
        {
            return;
        }

        if (model.Notes[1] >= 2)
        {
            Assert.Equal(PulsePeriod(model.Notes[1] + 34), state.Apu.Pulse2.Period);
            Assert.Equal((instruments[2] >> 6, instruments[3]), (state.Apu.Pulse2.Duty, state.Apu.Pulse2.Envelope.Register));
        }
        else if (model.Notes[1] == 1)
        {
            Assert.Equal(0, state.Apu.Pulse2.Envelope.Register);
        }

        if (model.Drum != 0)
        {
            Assert.Equal((song.Kit[(model.Drum - 1) * 2], song.Kit[((model.Drum - 1) * 2) + 1]), (state.Apu.Noise.Envelope.Register, state.Apu.Noise.Control));
        }
    }

    private static bool EffectsQuiet(GameBoySystem system) =>
        Picture(system, Symbols.Value["wSfx2Frames"], 1)[0] == 0 && Picture(system, Symbols.Value["wSfx4Frames"], 1)[0] == 0;

    // Each scene's song plays a whole loop with every row, note and drum where the model puts them.
    [Theory]
    [InlineData(1, 1000)]
    [InlineData(2, 650)]
    [InlineData(3, 800)]
    public void ScenesPlayTheirSongsRowByRowAndLoop(int id, int frames)
    {
        GameBoySystem system;
        if (id == SongTitle)
        {
            system = Boot();
            WaitFor(system, o => o[OPhase] == TitleIdle, 600);
        }
        else if (id == SongSelect)
        {
            system = OpenTitleMenu();
            Tap(system, A);
            WaitForScene(system, 1);
        }
        else
        {
            system = StartBattle(0, 0);
            new Player(system).Play(new Queue<Command>()); // Stops at the first command menu.
            RunFrames(system, 1);
        }
        WaitFor(system, _ => EffectsQuiet(system), 60);
        var model = FollowSong(system, id, out var song);
        int rows = 0, notes = 0, drums = 0, loops = 0;
        for (var frame = 0; frame < frames; frame++)
        {
            var order = model.Order;
            Frame(system, 0);
            var played = model.Frame();
            Assert.True(EffectsQuiet(system));
            AssertSong(system, model, song, true);
            if (played is null)
            {
                continue;
            }

            rows++;
            notes += played[..3].Count(note => note >= 2);
            if (played[3] != 0)
            {
                drums++;
            }

            if (model.Order < order)
            {
                loops++;
            }
        }
        Assert.True(loops == 1 && rows > song.Patterns.Length * 16, $"{rows} rows, {loops} loops");
        Assert.True(model.Notes.All(note => note >= 2) && drums > 0, $"{notes} notes, {drums} drums");
        output.WriteLine($"song {id}: {rows} rows, {notes} notes, {drums} drums in {frames} frames, one loop");
    }

    // The victory and game over jingles play once, fall silent on their last row and play nothing after.
    [Theory]
    [InlineData(SongVictory)]
    [InlineData(SongGameOver)]
    public void JinglesPlayOnceAndStop(int id)
    {
        var system = id == SongVictory ? StartBattle(0, 0) : StartBattle(5, 2);
        var player = new Player(system);
        Command[] plan = [.. Enumerable.Repeat(id == SongVictory ? Command.Fight : Command.Run, 80)];
        player.Play(new Queue<Command>(plan), stopSong: id); // The jingle has begun.
        var model = FollowSong(system, id, out var song);
        var frame = 0;
        for (; model.Active; frame++)
        {
            Assert.True(frame < 600);
            Frame(system, 0);
            model.Frame();
            AssertSong(system, model, song, false); // A message click may hold CH2.
        }
        var state = system.CaptureState();
        Assert.Equal((0, 0), (state.Apu.Pulse1.Envelope.Register, state.Apu.Pulse2.Envelope.Register));
        Assert.False(state.Apu.Wave.DacOn);
        for (var after = 0; after < 120; after++)
        {
            Frame(system, 0);
            model.Frame();
            AssertSong(system, model, song, true);
        }
        Assert.Equal(id, Observe(system)[OSong]);
        output.WriteLine($"song {id}: stopped {frame} frames after the first frame followed");
    }
}
