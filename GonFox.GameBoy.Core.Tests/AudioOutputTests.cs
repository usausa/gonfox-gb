namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Audio;
using GonFox.GameBoy.Core.Cartridge;

// The 48 kHz mix and its queue; one DAC level step is 64 PCM units.
[Trait("Category", "Unit")]
public sealed class AudioOutputTests
{
    private const int Second = GameBoySystem.TCyclesPerSecond;

    // Frames completed by T-cycle t.
    private static int FramesBy(ulong t) => (int)(t * AudioOutput.SampleRate / Second);

    private static short[] Frames(AudioOutput output, int count)
    {
        var pcm = new short[count * 2];
        Assert.Equal(count, output.ReadFrames(pcm));
        return pcm;
    }

    private static IEnumerable<short> Side(IEnumerable<short> pcm, int side) => pcm.Where((_, i) => i % 2 == side);

    [Fact]
    public void ReadFramesCopiesWholeFramesInOrderAndRejectsHalfFrames()
    {
        var output = new AudioOutput();
        for (short i = 0; i < 10; i++)
        {
            output.Write(i, (short)-i);
        }

        Assert.Throws<ArgumentException>(() => output.ReadFrames(new short[7]));
        Assert.Equal(10, output.QueuedFrameCount);
        Assert.Equal(0, output.ReadFrames([]));
        Assert.Equal(new short[] { 0, 0, 1, -1, 2, -2 }, Frames(output, 3));
        var rest = Enumerable.Repeat((short)99, 20).ToArray();
        Assert.Equal(7, output.ReadFrames(rest));
        Assert.Equal(new short[] { 3, -3, 4, -4, 5, -5, 6, -6, 7, -7, 8, -8, 9, -9, 99, 99, 99, 99, 99, 99 }, rest);
        Assert.Equal(0, output.QueuedFrameCount);
        Assert.Equal(0, output.ReadFrames(rest));
        Assert.Equal(0, output.DroppedFrameCount);
    }

    [Fact]
    public void AFullQueueDropsItsOldestFramesAndCountsThem()
    {
        var output = new AudioOutput();
        for (var i = 0; i < AudioOutput.CapacityFrames + 5; i++)
        {
            output.Write((short)i, (short)~i);
        }

        Assert.Equal(AudioOutput.CapacityFrames, output.QueuedFrameCount);
        Assert.Equal(5, output.DroppedFrameCount);
        Assert.Equal(Enumerable.Range(5, AudioOutput.CapacityFrames).SelectMany(i => new[] { (short)i, (short)~i }),
            Frames(output, AudioOutput.CapacityFrames));

        // Around the end of the ring.
        for (var i = 0; i < 1_500; i++)
        {
            output.Write((short)i, 0);
        }

        Assert.Equal(Enumerable.Range(0, 1_000).Select(i => (short)i), Side(Frames(output, 1_000), 0));
        for (var i = 1_500; i < 2_500; i++)
        {
            output.Write((short)i, 0);
        }

        Assert.Equal(Enumerable.Range(1_000, 1_500).Select(i => (short)i), Side(Frames(output, 1_500), 0));
        Assert.Equal(5, output.DroppedFrameCount);
    }

    [Fact]
    public void MixerAveragesEachSampleAndClipsTheHighPassOvershoot()
    {
        var output = new AudioOutput();
        var mixer = new AudioMixer(output);
        mixer.Reset(0, 0); // High-pass settled at 0.
        Assert.Equal(88, mixer.CyclesUntilSample); // The first sample boundary.
        mixer.Advance(44, 480, 0, true);
        mixer.Advance(44, 0, -480, true);
        Assert.Equal(new short[] { 15_360, -15_360 }, Frames(output, 1)); // Means of 240 and -240.

        mixer.Reset(480, -480); // Settled at one ceiling.
        mixer.Advance(88, -480, 480, true);
        Assert.Equal(new[] { short.MinValue, short.MaxValue }, Frames(output, 1));

        mixer.Reset(0, 0); // DACs off: silent, charge kept.
        mixer.Advance(88, 480, 480, false);
        Assert.Equal(87, mixer.CyclesUntilSample);
        mixer.Advance(87, 480, -480, true);
        Assert.Equal(new short[] { 0, 0, 30_720, -30_720 }, Frames(output, 2));
    }

    [Fact]
    public void AHeldLevelDecaysByTheDmgChargeFactorPerSample()
    {
        // The DMG charge factor per 48 kHz sample is 65,296 in Q16.
        Assert.Equal(65_296, (int)Math.Round(65_536 * Math.Pow(0.999958, (double)Second / AudioOutput.SampleRate)));
        var output = new AudioOutput();
        var mixer = new AudioMixer(output);
        mixer.Reset(0, 0);
        for (var k = 0; k < 3_000; k++)
        {
            mixer.Advance(mixer.CyclesUntilSample, 120, -120, true);
            var frame = Frames(output, 1);
            var expected = 7_680 * Math.Pow(65_296 / 65_536.0, k);
            Assert.InRange(frame[0], expected - 1, expected + 1);
            Assert.InRange(frame[1], -expected - 1, -expected + 1);
        }
    }

    // A machine just powered on, every DAC off and the high-pass settled at 0.
    private static PeripheralTestMachine Quiet()
    {
        var m = new PeripheralTestMachine();
        m.Bus.WriteByte(0xFF26, 0x00);
        m.Bus.WriteByte(0xFF26, 0x80);
        var s = m.Apu.CaptureState();
        m.Apu.RestoreState(s with { Mixer = s.Mixer with { CapacitorLeft = 0, CapacitorRight = 0 } }, m.Clock.TotalTCycles);
        return m;
    }

    private static readonly (ushort Register, byte On)[] Dacs = [(0xFF12, 0x08), (0xFF17, 0x08), (0xFF1A, 0x80), (0xFF21, 0x08)];

    [Theory]
    [InlineData(0x72, 0x21, 0b0011, 15 * 8, 15 * 3)] // CH2 left x8, CH1 right x3.
    [InlineData(0x07, 0x33, 0b0011, 30 * 1, 30 * 8)]
    [InlineData(0x77, 0x00, 0b0011, 0, 0)]           // Nothing routed.
    [InlineData(0x77, 0xFF, 0b1111, 60 * 8, 60 * 8)] // The ceiling.
    [InlineData(0xFF, 0x44, 0b0100, 15 * 8, 15 * 8)] // CH3 alone; VIN ignored.
    [InlineData(0x77, 0x88, 0b1000, 15 * 8, 15 * 8)] // CH4 alone.
    [InlineData(0x77, 0xFF, 0b0000, 0, 0)]           // Every DAC off.
    public void Nr51RoutesNr50ScalesAndEachDacAddsItsLevel(int nr50, int nr51, int dacs, int left, int right)
    {
        var m = Quiet();
        m.Bus.WriteByte(0xFF24, (byte)nr50);
        m.Bus.WriteByte(0xFF25, (byte)nr51);
        for (var c = 0; c < 4; c++)
        {
            if (((dacs >> c) & 1) != 0)
            {
                m.Bus.WriteByte(Dacs[c].Register, Dacs[c].On);
            }
        }

        m.Clock.AdvanceTCycles(88);
        m.Apu.Synchronize();
        Assert.Equal(new[] { (short)(left * 64), (short)(right * 64) }, Frames(m.Audio, 1));
    }

    [Fact]
    public void WithEveryDacOffTheOutputIsSilentAndTheCapacitorWaits()
    {
        var m = new PeripheralTestMachine(); // High-pass settled at +7,680.
        m.Bus.WriteByte(0xFF26, 0x00);
        m.Bus.WriteByte(0xFF26, 0x80);
        m.Clock.AdvanceTCycles(Second / 4);
        m.Apu.Synchronize();
        Assert.All(Frames(m.Audio, AudioOutput.CapacityFrames), sample => Assert.Equal(0, sample));
        Assert.Equal(0, m.Audio.QueuedFrameCount);
        Assert.Equal(FramesBy(Second / 4) - AudioOutput.CapacityFrames, m.Audio.DroppedFrameCount);

        var t = m.Clock.TotalTCycles;
        Assert.Equal(0UL, (t * AudioOutput.SampleRate) % Second); // A sample boundary.
        m.Bus.WriteByte(0xFF17, 0x08); // A DAC on, nothing routed.
        m.Clock.AdvanceTCycles(88);
        m.Apu.Synchronize();
        Assert.Equal(new short[] { -7_680, -7_680 }, Frames(m.Audio, 1));
    }

    [Fact]
    public void EachSideCarriesOnlyItsOwnChannels()
    {
        static short[] Play(bool pulse1, bool pulse2)
        {
            var m = Quiet();
            m.Bus.WriteByte(0xFF24, 0x75);
            m.Bus.WriteByte(0xFF25, 0x12); // CH1 left, CH2 right.
            if (pulse1)
            {
                m.Bus.WriteByte(0xFF11, 0x80);
                m.Bus.WriteByte(0xFF12, 0xF0);
                m.Bus.WriteByte(0xFF13, 0x40);
                m.Bus.WriteByte(0xFF14, 0x87);
            }
            m.Clock.AdvanceTCycles(1_000);
            if (pulse2)
            {
                m.Bus.WriteByte(0xFF16, 0xC0);
                m.Bus.WriteByte(0xFF17, 0x91);
                m.Bus.WriteByte(0xFF18, 0x00);
                m.Bus.WriteByte(0xFF19, 0x87);
            }
            m.Clock.AdvanceTCycles(150_000);
            m.Apu.Synchronize();
            return Frames(m.Audio, m.Audio.QueuedFrameCount);
        }

        short[] both = Play(true, true), left = Play(true, false), right = Play(false, true);
        Assert.Equal(Side(left, 0), Side(both, 0));
        Assert.Equal(Side(right, 1), Side(both, 1));
        Assert.All(Side(left, 1).Concat(Side(right, 0)), sample => Assert.Equal(0, sample));
        Assert.True(Side(left, 0).Distinct().Count() > 50 && Side(right, 1).Distinct().Count() > 50);
    }

    // The documented mix, computed per T-cycle from the observed channel outputs and registers.
    private sealed class ReferenceMix(long capacitorLeft, long capacitorRight)
    {
        private long left;
        private long right;
        private long capacitorLeft = capacitorLeft;
        private long capacitorRight = capacitorRight;
        private int cycles;
        internal List<short> Pcm { get; } = [];

        internal static (int Left, int Right, bool Dacs) Observe(PeripheralTestMachine m)
        {
            int nr50 = m.Bus.ReadByte(0xFF24), nr51 = m.Bus.ReadByte(0xFF25), left = 0, right = 0;
            bool[] dacs = [(m.Bus.ReadByte(0xFF12) & 0xF8) != 0, (m.Bus.ReadByte(0xFF17) & 0xF8) != 0,
                (m.Bus.ReadByte(0xFF1A) & 0x80) != 0, (m.Bus.ReadByte(0xFF21) & 0xF8) != 0];
            for (var c = 0; c < 4; c++)
            {
                if (!dacs[c])
                {
                    continue;
                }

                var level = 15 - (2 * m.Apu.ChannelOutput(c + 1));
                if (((nr51 >> (4 + c)) & 1) != 0)
                {
                    left += level;
                }

                if (((nr51 >> c) & 1) != 0)
                {
                    right += level;
                }
            }
            return (left * (((nr50 >> 4) & 7) + 1), right * ((nr50 & 7) + 1), dacs.Any(dac => dac));
        }

        // Adds T-cycle t at the given level, ending a sample when t + 1 completes one.
        internal void Add((int Left, int Right, bool Dacs) level, ulong t)
        {
            left += level.Left;
            right += level.Right;
            cycles++;
            if (FramesBy(t + 1) == FramesBy(t))
            {
                return;
            }

            Pcm.Add(Filter(ref capacitorLeft, left, level.Dacs));
            Pcm.Add(Filter(ref capacitorRight, right, level.Dacs));
            (left, right, cycles) = (0, 0, 0);
        }

        private short Filter(ref long capacitor, long sum, bool on)
        {
            if (!on)
            {
                return 0;
            }

            var input = sum * 64 * 65_536 / cycles;
            var result = input - capacitor;
            capacitor = input - (long)Math.Floor((result * 65_296 / 65_536.0) + 0.5);
            return (short)Math.Clamp((long)Math.Floor((result / 65_536.0) + 0.5), short.MinValue, short.MaxValue);
        }
    }

    private static readonly byte[] WavePattern =
        [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF, 0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10];

    private static IEnumerable<(int At, ushort Register, byte Value)> At(int at, params (ushort Register, byte Value)[] writes) =>
        writes.Select(w => (at, w.Register, w.Value));

    private static IEnumerable<(int At, ushort Register, byte Value)> FillWave(int at) => At(at, WavePattern.Select((v, i) => ((ushort)(0xFF30 + i), v)).ToArray());

    // Writes on odd T-cycles keep every channel step off the DIV-APU edges.
    private static readonly (int At, ushort Register, byte Value)[] ReferenceScript =
    [
        .. At(1, (0xFF24, 0x75), (0xFF25, 0x96)), .. FillWave(1),
        .. At(1, (0xFF10, 0x13), (0xFF11, 0xB0), (0xFF12, 0xF1), (0xFF13, 0x40), (0xFF14, 0xC6)),
        .. At(1_001, (0xFF16, 0x7C), (0xFF17, 0x6A), (0xFF18, 0x80), (0xFF19, 0xC6)),
        .. At(2_001, (0xFF1A, 0x80), (0xFF1B, 0xFE), (0xFF1C, 0x20), (0xFF1D, 0xC0), (0xFF1E, 0xC7)),
        .. At(3_001, (0xFF21, 0xA1), (0xFF22, 0x24), (0xFF23, 0x80)),
        .. At(20_001, (0xFF25, 0xFF)), .. At(30_007, (0xFF1C, 0x40), (0xFF1E, 0x87)), .. At(45_013, (0xFF22, 0x0B)),
        .. At(50_051, (0xFF24, 0x07)), .. At(66_001, (0xFF12, 0x00), (0xFF17, 0x00), (0xFF1A, 0x00), (0xFF21, 0x00)),
        .. At(68_001, (0xFF17, 0x08)), .. At(70_001, (0xFF1A, 0x80), (0xFF1E, 0x87)), .. At(75_001, (0xFF26, 0x00)),
        .. At(80_001, (0xFF26, 0x80), (0xFF24, 0x77), (0xFF25, 0xFF), (0xFF21, 0xF1), (0xFF22, 0x00), (0xFF23, 0x80)),
        .. At(80_001, (0xFF16, 0x80), (0xFF17, 0x1F), (0xFF19, 0x80)),
        .. At(100_001, (0xFF22, 0xE0)), .. At(110_001, (0xFF22, 0x13)), .. At(120_001, (0xFF17, 0x00))
    ];

    // Envelopes, lengths, a sweep, retriggers, Wave RAM accesses, DAC and power switches.
    private static readonly (int At, ushort Register, byte Value)[] RichScript =
    [
        .. At(0, (0xFF24, 0x77), (0xFF25, 0xFF)), .. FillWave(0),
        .. At(0, (0xFF10, 0x13), (0xFF11, 0xB0), (0xFF12, 0xF1), (0xFF13, 0x00), (0xFF14, 0xC6)),
        .. At(0, (0xFF16, 0x40), (0xFF17, 0x19), (0xFF18, 0x30), (0xFF19, 0x87)),
        .. At(0, (0xFF1A, 0x80), (0xFF1B, 0xF0), (0xFF1C, 0x20), (0xFF1D, 0x00), (0xFF1E, 0xC7)),
        .. At(0, (0xFF20, 0x20), (0xFF21, 0x7A), (0xFF22, 0x31), (0xFF23, 0xC0)),
        .. At(9_000, (0xFF3A, 0x5A)), .. At(15_000, (0xFF1E, 0x87)), .. At(17_000, (0xFF24, 0x31)),
        .. At(25_000, (0xFF25, 0x5A)), .. At(33_000, (0xFF12, 0x00)), .. At(33_500, (0xFF12, 0xF7)),
        .. At(40_000, (0xFF14, 0x87)), .. At(52_000, (0xFF26, 0x00)),
        .. At(52_100, (0xFF26, 0x80), (0xFF24, 0x77), (0xFF25, 0xFF), (0xFF16, 0x80), (0xFF17, 0xF2), (0xFF19, 0x86)),
        .. At(52_100, (0xFF21, 0xF3), (0xFF22, 0x08), (0xFF23, 0x80), (0xFF1A, 0x80), (0xFF1C, 0x60), (0xFF1E, 0x86)),
        .. At(100_000, (0xFF1E, 0x86), (0xFF37, 0x00)), .. At(150_000, (0xFF11, 0x40), (0xFF12, 0x3A), (0xFF14, 0x85)),
        .. At(200_000, (0xFF23, 0x80), (0xFF25, 0xA5))
    ];

    // Runs the script synchronizing the APU at every T-cycle, feeding the reference.
    private static short[] PlayEveryCycle(PeripheralTestMachine m, (int At, ushort Register, byte Value)[] script, int cycles,
        ReferenceMix? reference)
    {
        var pcm = new List<short>();
        var next = 0;
        for (var t = 0; t < cycles; t++)
        {
            while (next < script.Length && script[next].At == t)
            {
                m.Bus.WriteByte(script[next].Register, script[next++].Value);
            }

            m.Apu.Synchronize();
            var level = reference is null ? default : ReferenceMix.Observe(m);
            int divider = m.Timer.DividerCounter;
            m.Clock.AdvanceTCycles(1);

            // Re-observes after a DIV-APU edge, whose clocks apply from the T-cycle in progress.
            if (reference is not null && (divider & ~m.Timer.DividerCounter & 0x1000) != 0)
            {
                level = ReferenceMix.Observe(m);
            }

            reference?.Add(level, (ulong)t);
            if (m.Audio.QueuedFrameCount >= 1_000)
            {
                pcm.AddRange(Frames(m.Audio, m.Audio.QueuedFrameCount));
            }
        }
        m.Apu.Synchronize();
        pcm.AddRange(Frames(m.Audio, m.Audio.QueuedFrameCount));
        return [.. pcm];
    }

    // Runs the script leaving the APU to its own sync points.
    private static short[] PlayBatched(PeripheralTestMachine m, IEnumerable<(int At, ushort Register, byte Value)> script, int cycles)
    {
        var pcm = new List<short>();
        foreach (var (at, register, value) in script)
        {
            m.Clock.AdvanceTCycles(at - (int)m.Clock.TotalTCycles);
            m.Bus.WriteByte(register, value);
            pcm.AddRange(Frames(m.Audio, m.Audio.QueuedFrameCount));
        }
        m.Clock.AdvanceTCycles(cycles - (int)m.Clock.TotalTCycles);
        m.Apu.Synchronize();
        pcm.AddRange(Frames(m.Audio, m.Audio.QueuedFrameCount));
        return [.. pcm];
    }

    [Fact]
    public void PcmFollowsTheDocumentedMixOfTheObservedChannelLevels()
    {
        const int cycles = 160_000;
        var m = new PeripheralTestMachine(); // Boot level on the capacitors.
        var mixer = m.Apu.CaptureState().Mixer;
        var reference = new ReferenceMix(mixer.CapacitorLeft, mixer.CapacitorRight);
        var observed = PlayEveryCycle(m, ReferenceScript, cycles, reference);
        Assert.Equal(FramesBy(cycles) * 2, reference.Pcm.Count);
        Assert.Equal(reference.Pcm, observed);
        Assert.Equal(observed, PlayBatched(new PeripheralTestMachine(), ReferenceScript, cycles));
        Assert.True(observed.Distinct().Count() > 500);
        Assert.All(observed[(FramesBy(66_100) * 2)..(FramesBy(68_000) * 2)], sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void PcmDoesNotDependOnWhenTheApuIsSynchronized()
    {
        const int cycles = 260_000;
        var everyCycle = PlayEveryCycle(new PeripheralTestMachine(), RichScript, cycles, null);
        Assert.Equal(FramesBy(cycles) * 2, everyCycle.Length);
        Assert.Equal(everyCycle, PlayBatched(new PeripheralTestMachine(), RichScript, cycles));
        Assert.True(everyCycle.Distinct().Count() > 500);
    }

    // Assembles LD A,value; LDH (register),A for each pair.
    private static byte[] Writes(params (byte Register, byte Value)[] writes) =>
        writes.SelectMany(w => new byte[] { 0x3E, w.Value, 0xE0, w.Register }).ToArray();

    // A program that plays all four channels while a DIV-driven loop rewrites their registers.
    private static GameBoySystem SoundSystem()
    {
        byte[] program =
        [
            .. Writes((0x24, 0x77), (0x25, 0xD6)),
            .. Writes(WavePattern.Select((v, i) => ((byte)(0x30 + i), v)).ToArray()),
            .. Writes((0x10, 0x2C), (0x11, 0x80), (0x12, 0xF3), (0x13, 0x00), (0x14, 0x87)),
            .. Writes((0x16, 0x7C), (0x17, 0x6A), (0x18, 0x80), (0x19, 0xC6)),
            .. Writes((0x1A, 0x80), (0x1C, 0x20), (0x1D, 0x00), (0x1E, 0x86)),
            .. Writes((0x21, 0xA1), (0x22, 0x35), (0x23, 0x80)),
            0xF0, 0x04, // loop: LDH A,(DIV)
            0xE0, 0x13, // LDH (NR13),A
            0xE0, 0x24, // LDH (NR50),A
            0xE6, 0x3F, // AND $3F
            0xE0, 0x22, // LDH (NR43),A
            0xE6, 0x1F, // AND $1F
            0x20, 0x04, // JR NZ,+4
            0x3E, 0x86, // LD A,$86
            0xE0, 0x1E, // LDH (NR34),A
            0xF0, 0x30, // LDH A,($30)
            0xE0, 0x3F, // LDH ($3F),A
            0x18, 0xE8 // JR loop
        ];
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(TestRom.Create(program)).Cartridge);
        return system;
    }

    private static void Drain(GameBoySystem system, List<short> pcm) =>
        pcm.AddRange(Frames(system.Audio, system.Audio.QueuedFrameCount));

    private static List<short> RunInChunks(GameBoySystem system, int chunk, ulong until, List<short>? pcm = null)
    {
        pcm ??= [];
        while (system.TotalTCycles < until)
        {
            system.RunForTCycles(chunk);
            Drain(system, pcm);
        }
        return pcm;
    }

    [Fact]
    public void EachEmulatedSecondGivesExactly48000Frames()
    {
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(TestRom.Create("v"u8.ToArray())).Cartridge); // HALT: 4 T-cycles a step.
        int Generated() => system.Audio.QueuedFrameCount + (int)system.Audio.DroppedFrameCount;
        system.RunForTCycles(Second - 4);
        Assert.Equal((ulong)Second - 4, system.TotalTCycles);
        Assert.Equal(47_999, Generated());
        system.RunForTCycles(4);
        Assert.Equal(48_000, Generated());
        for (var i = 0; i < Second; i += 4)
        {
            system.StepInstruction();
        }

        Assert.Equal(96_000, Generated());
        Assert.Equal(AudioOutput.CapacityFrames, system.Audio.QueuedFrameCount);
        system.Reset();
        Assert.Equal(0, Generated());
    }

    [Fact]
    public void PcmIsTheSameHoweverTheRunIsSplitOrRead()
    {
        const ulong until = 300_000;
        var chunked = SoundSystem();
        var chunks = RunInChunks(chunked, 7_000, until);
        Assert.Equal(FramesBy(chunked.TotalTCycles) * 2, chunks.Count);

        var random = new Random(16);
        var mixed = SoundSystem();
        var mixedPcm = new List<short>();
        while (mixed.TotalTCycles < until)
        {
            if (random.Next(4) == 0)
            {
                mixed.StepInstruction();
            }
            else
            {
                mixed.RunForTCycles(random.Next(0, 3_000));
            }

            var t = mixed.TotalTCycles;
            var buffer = new short[random.Next(0, 200) * 2]; // Partial reads leave frames queued.
            var frames = mixed.Audio.ReadFrames(buffer);
            Assert.Equal(t, mixed.TotalTCycles); // Reading never runs the emulation.
            mixedPcm.AddRange(buffer.Take(frames * 2));
        }
        Drain(mixed, mixedPcm);
        Assert.Equal(0, mixed.Audio.DroppedFrameCount);
        Assert.Equal(FramesBy(mixed.TotalTCycles) * 2, mixedPcm.Count);

        var common = Math.Min(chunks.Count, mixedPcm.Count);
        Assert.Equal(chunks.Take(common), mixedPcm.Take(common));
        Assert.True(chunks.Distinct().Count() > 1_000);
        Assert.NotEqual(Side(chunks, 0), Side(chunks, 1));

        // Without reads the queue keeps the newest frames.
        var undrained = SoundSystem();
        while (undrained.TotalTCycles < until)
        {
            undrained.RunForTCycles(7_000);
        }

        Assert.Equal(chunked.TotalTCycles, undrained.TotalTCycles);
        Assert.Equal(AudioOutput.CapacityFrames, undrained.Audio.QueuedFrameCount);
        Assert.Equal((chunks.Count / 2) - AudioOutput.CapacityFrames, undrained.Audio.DroppedFrameCount);
        Assert.Equal(chunks.Skip(chunks.Count - (AudioOutput.CapacityFrames * 2)), Frames(undrained.Audio, AudioOutput.CapacityFrames));
    }

    [Fact]
    public void RestoringMidRunReplaysTheQueuedAndTheFollowingPcm()
    {
        const ulong until = 300_000;
        var system = SoundSystem();
        var before = RunInChunks(system, 5_000, 150_000);
        system.RunForTCycles(20_000); // Left queued in the state.
        var queued = system.Audio.QueuedFrameCount;
        Assert.True(queued > 200);
        var saved = system.CaptureState();

        static List<short> Continue(GameBoySystem s)
        {
            var pcm = new List<short>();
            Drain(s, pcm);
            return RunInChunks(s, 5_000, until, pcm);
        }
        var after = Continue(system);
        system.RestoreState(saved);
        Assert.Equal(queued, system.Audio.QueuedFrameCount);
        Assert.Equal(after, Continue(system));
        var other = SoundSystem();
        other.RunForTCycles(1_000); // Its own queued PCM is replaced.
        other.RestoreState(saved);
        Assert.Equal(after, Continue(other));

        var straight = RunInChunks(SoundSystem(), 9_000, until); // The same as never saving.
        List<short> whole = [.. before, .. after];
        var common = Math.Min(straight.Count, whole.Count);
        Assert.True(common >= FramesBy(until) * 2);
        Assert.Equal(straight.Take(common), whole.Take(common));

        var full = SoundSystem();
        full.RunForTCycles(250_000); // Overfills the queue.
        var copy = SoundSystem();
        copy.RestoreState(full.CaptureState());
        Assert.True(copy.Audio.DroppedFrameCount > 0);
        Assert.Equal(full.Audio.DroppedFrameCount, copy.Audio.DroppedFrameCount);
        Assert.Equal(Frames(full.Audio, AudioOutput.CapacityFrames), Frames(copy.Audio, AudioOutput.CapacityFrames));
    }
}
