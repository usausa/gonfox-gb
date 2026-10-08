namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Audio;

// Mega demo music: a model of the sequencer predicts every step and note the ROM plays.
public sealed partial class MegaDemoTests
{
    private const int TempoBase = 24;
    private const int TempoPerSpeed = 6;
    private const int SongSteps = 64;
    private const int EffectFrames = 8;
    private const int Panning = 0x7D;

    private static byte[] Rom(string label, int length) => Image.Value[Symbols.Value[label]..(Symbols.Value[label] + length)];
    private static int Word(byte[] bytes, int index) => bytes[index * 2] | (bytes[(index * 2) + 1] << 8);

    // Reads the music state at C050-C05B, from the song step to the instrument registers.
    private static byte[] Music(GameBoySystem system)
    {
        var block = new byte[12];
        system.CopyMemory(0xC050, block);
        return block;
    }

    [Fact]
    public void NoteTableFollowsEqualTemperament()
    {
        var table = Rom("NoteTable", 61 * 2);
        for (var midi = 36; midi <= 96; midi++)
        {
            Assert.Equal((int)Math.Round(2048 - (131072 / (440 * Math.Pow(2, (midi - 69) / 12.0)))), Word(table, midi - 36));
        }

        var blips = Rom("EffectPeriods", 16);
        int[] pentatonic = [72, 74, 76, 79, 81, 84, 86, 88]; // C5 D5 E5 G5 A5 C6 D6 E6
        for (var amount = 0; amount < 8; amount++)
        {
            Assert.Equal(Word(table, pentatonic[amount] - 36), Word(blips, amount));
        }
    }

    // Models the sequencer: each frame adds the tempo to a fraction whose carry plays the next step.
    private sealed class SongModel(byte[] song, int step, int fraction)
    {
        internal int Step { get; private set; } = step;
        internal int Fraction { get; private set; } = fraction;
        internal int[] Notes { get; init; } = new int[3]; // Melody, harmony, bass.
        internal int Drum { get; private set; } // This frame's drum, 0 if none.

        internal bool Frame(int speed)
        {
            Drum = 0;
            Fraction += TempoBase + (TempoPerSpeed * speed);
            if (Fraction < 256)
            {
                return false;
            }

            Fraction -= 256;
            Step = (Step + 1) % SongSteps;
            for (var channel = 0; channel < 3; channel++)
            {
                if (song[(Step * 4) + channel] != 0)
                {
                    Notes[channel] = song[(Step * 4) + channel];
                }
            }

            Drum = song[(Step * 4) + 3];
            return true;
        }
    }

    [Fact]
    public void TempoFollowsTheSpeedAndEveryChannelFollowsTheSong()
    {
        byte[] song = Rom("Song", SongSteps * 4), notes = Rom("NoteTable", 61 * 2), kits = Rom("DrumKits", 18);
        var instruments = Rom("Instruments", 12);
        var system = Boot();
        var music = Music(system);
        var model = new SongModel(song, music[0], music[1]) { Notes = [music[5], music[6], music[7]] };
        int played = 0, drums = 0;

        // Speed 3, then up to 7, then down to 0.
        byte[] inputs = [.. Enumerable.Repeat((byte)0, 120), .. Enumerable.Repeat(Right, 60), .. Enumerable.Repeat((byte)0, 120),
            .. Enumerable.Repeat(Left, 80), .. Enumerable.Repeat((byte)0, 200)];
        foreach (var input in inputs)
        {
            var o = Frame(system, input);
            if (model.Frame(o[1]))
            {
                played++;
            }

            music = Music(system);
            Assert.Equal((model.Step, model.Fraction), (music[0], music[1]));
            Assert.Equal(model.Notes, music[5..8].Select(b => (int)b));
            var state = system.CaptureState();
            if (model.Notes[0] >= 2)
            {
                Assert.Equal(Word(notes, model.Notes[0] - 2), state.Apu.Pulse1.Period);
                Assert.Equal(o[3], state.Apu.Pulse1.Duty); // The pattern chooses the melody duty.
                Assert.Equal(instruments[0], state.Apu.Pulse1.Envelope.Register);
            }
            if (model.Notes[1] >= 2)
            {
                Assert.Equal(Word(notes, model.Notes[1] - 2), state.Apu.Pulse2.Period);
            }

            if (model.Notes[2] >= 2)
            {
                Assert.Equal(Word(notes, model.Notes[2] - 2), state.Apu.Wave.Period);
            }

            if (model.Drum != 0)
            {
                Assert.Equal(kits[(model.Drum - 1) * 2], state.Apu.Noise.Envelope.Register);
                Assert.Equal(kits[((model.Drum - 1) * 2) + 1], state.Apu.Noise.Control);
                drums++;
            }
            Assert.Equal(Panning, state.Apu.Registers[0x15]);
        }
        Assert.InRange(played, 80, 120); // About 95 steps.
        Assert.True(drums >= (played / 2) - 2); // A drum on every eighth note.
        Assert.Equal(0, Observe(system)[0x0D]); // No missed frame.
    }

    [Fact]
    public void StartStopsAndSilencesThenResumesTheHeldNotesAtTheSavedStep()
    {
        var system = Boot(90);
        var before = Music(system);
        Frame(system, Start);
        var stopped = Music(system);
        Assert.Equal(0, stopped[2]);
        Assert.Equal(0, system.CaptureState().Apu.Registers[0x15]); // NR51 routes nothing.
        for (var frame = 0; frame < 30; frame++)
        {
            Frame(system, 0);
        }

        Assert.Equal(stopped[..2], Music(system)[..2]); // The step and its fraction wait.
        var pcm = new short[AudioOutput.CapacityFrames * 2];
        var frames = system.Audio.ReadFrames(pcm);
        Assert.All(pcm[((frames - 1000) * 2)..(frames * 2)], sample => Assert.Equal(0, sample)); // Silent once the high-pass settles.

        Frame(system, Start);
        var resumed = Music(system);
        Assert.Equal(1, resumed[2]);
        var state = system.CaptureState();
        Assert.Equal(Panning, state.Apu.Registers[0x15]);
        Assert.True(state.Apu.Pulse1.Enabled && state.Apu.Wave.Enabled);
        byte[] notes = Rom("NoteTable", 61 * 2), instruments = Rom("Instruments", 12);
        Assert.Equal(Word(notes, stopped[5] - 2), state.Apu.Pulse1.Period); // Held melody note resumes.
        Assert.Equal(instruments[0] >> 4, state.Apu.Pulse1.Envelope.Volume); // At its initial volume.
        Assert.Equal(Word(notes, stopped[7] - 2), state.Apu.Wave.Period);
        var expected = (stopped[0] + (stopped[1] + TempoBase + (TempoPerSpeed * Observe(system)[1]) >= 256 ? 1 : 0)) % SongSteps;
        Assert.Equal(expected, resumed[0]); // The song goes on from the saved step.
        Assert.Equal(before[..2], stopped[..2]); // The stopping frame does not advance.
        system.Audio.ReadFrames(pcm);
        for (var frame = 0; frame < 10; frame++)
        {
            Frame(system, 0);
        }

        frames = system.Audio.ReadFrames(pcm);
        Assert.Contains(pcm[..(frames * 2)], sample => sample != 0);
    }

    [Fact]
    public void AmountStepsPlayABlipPitchedByTheAmountOnCh2()
    {
        byte[] blips = Rom("EffectPeriods", 16), notes = Rom("NoteTable", 61 * 2);
        var system = Boot();
        foreach (var input in new[] { Up, Down, Down })
        {
            var o = Frame(system, input);
            var state = system.CaptureState();
            Assert.Equal(Word(blips, o[2]), state.Apu.Pulse2.Period);
            Assert.Equal(0xF1, state.Apu.Pulse2.Envelope.Register);
            Assert.Equal(2, state.Apu.Pulse2.Duty);
            Assert.Equal(EffectFrames - 1, Music(system)[3]);
            Frame(system, 0);
        }

        // The arpeggio leaves CH2 to the blip until the effect frames run out, then plays its next note.
        for (var frame = 0; frame < EffectFrames - 2; frame++)
        {
            Frame(system, 0);
            Assert.Equal(Word(blips, Observe(system)[2]), system.CaptureState().Apu.Pulse2.Period);
        }
        for (var frame = 0; frame < 40 && system.CaptureState().Apu.Pulse2.Period == Word(blips, Observe(system)[2]); frame++)
        {
            Frame(system, 0);
        }

        Assert.Equal(Word(notes, Music(system)[6] - 2), system.CaptureState().Apu.Pulse2.Period);
    }

    [Fact]
    public void PatternAndSceneChangeTheTimbre()
    {
        byte[] instruments = Rom("Instruments", 12), waves = Rom("WaveTables", 48), kits = Rom("DrumKits", 18);
        var system = Boot();
        Frame(system, A);
        for (var frame = 0; frame < 30 && system.CaptureState().Apu.Pulse1.Duty != 1; frame++)
        {
            Frame(system, 0);
        }

        Assert.Equal(1, system.CaptureState().Apu.Pulse1.Duty); // The next melody note takes pattern 1.
        for (var scene = 1; scene <= 2; scene++)
        {
            Frame(system, Select);
            Assert.Equal(scene, Music(system)[4]);
            var state = system.CaptureState();
            Assert.Equal(waves[(scene * 16)..((scene * 16) + 16)], state.Apu.WaveRam);
            Assert.Equal(instruments[(scene * 4)..((scene * 4) + 4)], Music(system)[8..12]);
            bool harmony = false, hat = false;
            for (var frame = 0; frame < 120 && !(harmony && hat); frame++)
            {
                Frame(system, 0);
                state = system.CaptureState();
                harmony |= state.Apu.Pulse2.Duty == instruments[(scene * 4) + 1] >> 6 && state.Apu.Pulse2.Envelope.Register == instruments[(scene * 4) + 2];
                hat |= state.Apu.Noise.Control == kits[(scene * 6) + 5];
                Assert.Equal(instruments[(scene * 4) + 3] switch { 0x20 => 0, 0x40 => 1, _ => 2 }, state.Apu.Wave.Shift);
            }
            Assert.True(harmony && hat, $"scene {scene}");
        }
    }

    [Fact]
    public void BassNotesNeverCorruptWaveRam()
    {
        // Two loops of the song keep the wave table, as the driver stops CH3 before each bass note.
        var table = Rom("WaveTables", 16);
        var system = Boot(); // MusicInit has loaded the table.
        while (system.TotalTCycles < 14UL * GameBoySystem.TCyclesPerSecond)
        {
            system.RunForTCycles(70_224);
            Assert.Equal(table, system.CaptureState().Apu.WaveRam);
        }
    }

    [Fact]
    public void TheMusicIsStereo()
    {
        var system = Boot(0);
        var pcm = new List<short>();
        var buffer = new short[AudioOutput.CapacityFrames * 2];
        while (system.TotalTCycles < 4UL * GameBoySystem.TCyclesPerSecond)
        {
            system.RunForTCycles(4096);
            pcm.AddRange(buffer.AsSpan(0, system.Audio.ReadFrames(buffer) * 2));
        }
        double Rms(int side) => Math.Sqrt(pcm.Where((_, i) => i % 2 == side).Average(s => (double)s * s));
        Assert.True(Rms(0) > 1_000 && Rms(1) > 1_000);
        var different = pcm.Chunk(2).Count(frame => frame[0] != frame[1]);
        Assert.True(different > pcm.Count / 4); // Harmony left, drums right.
    }
}
