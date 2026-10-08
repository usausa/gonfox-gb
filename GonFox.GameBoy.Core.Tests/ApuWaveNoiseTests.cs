namespace GonFox.GameBoy.Core;

// CH3/CH4 tests; the frame sequencer steps every 8,192 T-cycles from T=8192.
[Trait("Category", "Unit")]
public sealed class ApuWaveNoiseTests
{
    private const int Edge = 8192;

    private static PeripheralTestMachine Powered()
    {
        var m = new PeripheralTestMachine();
        m.Bus.WriteByte(0xFF26, 0x00);
        m.Bus.WriteByte(0xFF26, 0x80); // Next step 0.
        return m;
    }

    private static void Run(PeripheralTestMachine m, int cycles) => m.Clock.AdvanceTCycles(cycles);
    private static bool On(PeripheralTestMachine m, int channel) => (m.Bus.ReadByte(0xFF26) & (1 << (channel - 1))) != 0;

    private static List<(int Cycle, int Level)> Trace(PeripheralTestMachine m, int channel, int cycles)
    {
        var level = m.Apu.ChannelOutput(channel);
        var changes = new List<(int, int)> { (0, level) };
        for (var cycle = 1; cycle <= cycles; cycle++)
        {
            Run(m, 1);
            var next = m.Apu.ChannelOutput(channel);
            if (next != level)
            {
                changes.Add((cycle, level = next));
            }
        }
        return changes;
    }

    private static void FillWave(PeripheralTestMachine m, Func<int, byte> value)
    {
        for (var i = 0; i < 16; i++)
        {
            m.Bus.WriteByte((ushort)(0xFF30 + i), value(i));
        }
    }

    // Wave RAM patterns: sample n plays n % 16, or byte i holds i in both nibbles.
    private static byte Counting(int i) => (byte)((((2 * i) % 16) << 4) | (((2 * i) + 1) % 16));
    private static byte Distinct(int i) => (byte)((0x10 * i) + i);

    private static void StartWave(PeripheralTestMachine m, int period, byte level = 0x20, bool length = false)
    {
        m.Bus.WriteByte(0xFF1A, 0x80);
        m.Bus.WriteByte(0xFF1C, level);
        m.Bus.WriteByte(0xFF1D, (byte)period);
        m.Bus.WriteByte(0xFF1E, (byte)(0x80 | (length ? 0x40 : 0) | (period >> 8)));
    }

    [Fact]
    public void WaveReadsFromTheSecondSampleAfterAPeriodAndSixCycles()
    {
        var m = Powered();
        FillWave(m, Counting);
        StartWave(m, 0x700); // 512 T per sample.
        var expected = new List<(int, int)> { (0, 0) };
        var level = 0;
        for (var read = 1; read <= 33; read++)
        {
            var value = (read % 32) % 16; // Read 1 is position 1.
            if (value != level)
            {
                expected.Add((518 + ((read - 1) * 512), level = value));
            }
        }
        Assert.Equal(expected, Trace(m, 3, 518 + (32 * 512)));
    }

    [Fact]
    public void TriggerPlaysTheUpperNibbleOfTheLastByteUntilTheFirstRead()
    {
        var m = Powered();
        FillWave(m, Counting);
        StartWave(m, 0x700);
        Run(m, 518 + (4 * 512)); // Read 5: position 5.
        Assert.Equal(5, m.Apu.ChannelOutput(3));
        m.Bus.WriteByte(0xFF1E, 0x87);
        Assert.Equal([(0, 4), (518, 1)], Trace(m, 3, 600));
    }

    [Fact]
    public void OutputLevelShiftsTheSampleAtOnce()
    {
        var m = Powered();
        FillWave(m, Counting);
        StartWave(m, 0x700);
        Run(m, 518 + (14 * 512)); // Read 15: sample 15.
        Assert.Equal(15, m.Apu.ChannelOutput(3));
        foreach (var (level, expected) in new[] { (0x40, 7), (0x60, 3), (0x00, 0), (0x20, 15) })
        {
            m.Bus.WriteByte(0xFF1C, (byte)level);
            Assert.Equal(expected, m.Apu.ChannelOutput(3));
        }
    }

    [Fact]
    public void WaveLengthCountsTo256AndTheDacStopsTheChannel()
    {
        var one = Powered();
        one.Bus.WriteByte(0xFF1B, 0xFF); // Length 1.
        StartWave(one, 0x700, length: true);
        Run(one, Edge - 1);
        Assert.True(On(one, 3));
        Run(one, 1);
        Assert.False(On(one, 3));

        var full = Powered(); // Empty counter: loads 256.
        StartWave(full, 0x700, length: true);
        Run(full, (511 * Edge) - 1); // 256 clocks end on edge 511.
        Assert.True(On(full, 3));
        Run(full, 1);
        Assert.False(On(full, 3));

        var late = Powered(); // After step 0: loads 255.
        Run(late, Edge + 100);
        StartWave(late, 0x700, length: true);
        Run(late, (511 * Edge) - (Edge + 100) - 1);
        Assert.True(On(late, 3));
        Run(late, 1);
        Assert.False(On(late, 3));

        var dac = Powered();
        StartWave(dac, 0x700);
        dac.Bus.WriteByte(0xFF1A, 0x00);
        Assert.False(On(dac, 3));
        Assert.Equal(0, dac.Apu.ChannelOutput(3));
        dac.Bus.WriteByte(0xFF1E, 0x87);
        Assert.False(On(dac, 3));
        dac.Bus.WriteByte(0xFF1A, 0x80);
        Assert.False(On(dac, 3));
        dac.Bus.WriteByte(0xFF1E, 0x87);
        Assert.True(On(dac, 3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // Routed to the mix.
    public void PlayingWaveRamAnswersTheCpuOnlyInTheCycleOfARead(bool routed)
    {
        var m = Powered();
        if (routed)
        {
            m.Bus.WriteByte(0xFF25, 0x44);
        }

        FillWave(m, Distinct);
        StartWave(m, 0x700);
        Run(m, 517);
        Assert.Equal(0xFF, m.Bus.ReadByte(0xFF3A)); // Between reads.
        m.Bus.WriteByte(0xFF3A, 0x5A);
        Run(m, 1); // T=518: position 1 reads byte 0.
        Assert.Equal(0x00, m.Bus.ReadByte(0xFF3A)); // The byte being read, whatever the address.
        Assert.Equal(0xAA, m.Bus.PeekByte(0xFF3A)); // The write was dropped.
        Run(m, 512); // T=1030: position 2 reads byte 1.
        m.Bus.WriteByte(0xFF3F, 0x99);
        Assert.Equal(0x99, m.Bus.PeekByte(0xFF31));
        Assert.Equal(0xFF, m.Bus.PeekByte(0xFF3F));
        Run(m, (3 * 512) + 100); // Three reads at once, ending between two.
        Assert.Equal(0xFF, m.Bus.ReadByte(0xFF30));
        Run(m, 412); // T=3078: position 6 reads byte 3.
        Assert.Equal(0x33, m.Bus.ReadByte(0xFF30));
        m.Bus.WriteByte(0xFF1A, 0x00); // Stopped: ordinary access.
        Assert.Equal(0xAA, m.Bus.ReadByte(0xFF3A));
        m.Bus.WriteByte(0xFF3A, 0x5A);
        Assert.Equal(0x5A, m.Bus.ReadByte(0xFF3A));
    }

    [Theory]
    [InlineData(2, 2, new byte[] { 0x11, 0x11, 0x22, 0x33 })] // Next read in byte 1.
    [InlineData(9, 2, new byte[] { 0x44, 0x55, 0x66, 0x77 })] // Next read in byte 5.
    [InlineData(9, 4, new byte[] { 0x00, 0x11, 0x22, 0x33 })] // 4 T early: no change.
    public void RetriggeringJustBeforeAReadRewritesTheStartOfWaveRam(int reads, int before, byte[] start)
    {
        var m = Powered();
        FillWave(m, Distinct);
        StartWave(m, 0x700);
        Run(m, 518 + (reads * 512) - before);
        m.Bus.WriteByte(0xFF1E, 0x87);
        var ram = Enumerable.Range(0, 16).Select(i => m.Bus.PeekByte((ushort)(0xFF30 + i))).ToArray();
        Assert.Equal(start, ram[..4]);
        Assert.Equal(Enumerable.Range(4, 12).Select(Distinct), ram[4..]);
    }

    // Reference noise levels from an independent model: a complemented LFSR starting at all ones.
    private static IEnumerable<int> ReferenceNoise(bool shortMode, int clocks)
    {
        var lfsr = 0x7FFF;
        for (var i = 0; i < clocks; i++)
        {
            var bit = (lfsr ^ (lfsr >> 1)) & 1;
            lfsr = (lfsr >> 1) | (bit << 14);
            if (shortMode)
            {
                lfsr = (lfsr & ~0x40) | (bit << 6);
            }

            yield return (~lfsr & 1) * 15;
        }
    }

    [Theory]
    [InlineData(false, 40_000, 32_767)]
    [InlineData(true, 2_000, 127)]
    public void NoiseFollowsTheKnownLfsrSequence(bool shortMode, int clocks, int period)
    {
        var expected = ReferenceNoise(shortMode, clocks).ToArray();
        for (var i = 15; i + period < clocks; i++)
        {
            Assert.Equal(expected[i], expected[i + period]); // The known periods.
        }

        Assert.Contains(expected.Take(period), level => level == 0);
        Assert.Contains(expected.Take(period), level => level == 15);

        var m = Powered();
        m.Bus.WriteByte(0xFF21, 0xF0);
        m.Bus.WriteByte(0xFF22, (byte)(shortMode ? 0x08 : 0x00)); // Divisor 8 T, shift 0.
        m.Bus.WriteByte(0xFF23, 0x80);
        Assert.Equal(0, m.Apu.ChannelOutput(4)); // A trigger clears the LFSR.
        var actual = new int[clocks];
        for (var i = 0; i < clocks; i++)
        {
            Run(m, 8);
            actual[i] = m.Apu.ChannelOutput(4);
        }
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0x00, 15 * 8)]          // Divisor 8 T, shift 0.
    [InlineData(0x21, 15 * (16 << 2))]  // Divisor 16 T, shift 2.
    [InlineData(0x17, 15 * (112 << 1))] // Divisor 112 T, shift 1.
    [InlineData(0x0A, 7 * 32)]          // Short mode, divisor 32 T.
    public void NoiseClocksEveryDivisorShiftedLeft(byte nr43, int firstHigh)
    {
        var m = Powered();
        m.Bus.WriteByte(0xFF21, 0xF0);
        m.Bus.WriteByte(0xFF22, nr43);
        m.Bus.WriteByte(0xFF23, 0x80);
        Run(m, firstHigh - 1);
        Assert.Equal(0, m.Apu.ChannelOutput(4));
        Run(m, 1);
        Assert.Equal(15, m.Apu.ChannelOutput(4));
    }

    [Theory]
    [InlineData(0xE0)]
    [InlineData(0xF7)]
    public void ShiftsOf14And15StopTheLfsr(byte nr43)
    {
        var m = Powered();
        m.Bus.WriteByte(0xFF21, 0xF0);
        m.Bus.WriteByte(0xFF22, nr43);
        m.Bus.WriteByte(0xFF23, 0x80);
        Run(m, 200_000);
        Assert.True(On(m, 4));
        Assert.Equal(0, m.Apu.CaptureState().Noise.Lfsr);
    }

    [Fact]
    public void NoiseUsesTheSharedLengthEnvelopeAndDac()
    {
        var length = Powered();
        length.Bus.WriteByte(0xFF20, 0x3F); // Length 1.
        length.Bus.WriteByte(0xFF21, 0xF0);
        length.Bus.WriteByte(0xFF23, 0xC0); // Trigger with length.
        Run(length, Edge - 1);
        Assert.True(On(length, 4));
        Run(length, 1);
        Assert.False(On(length, 4));

        var envelope = Powered();
        envelope.Bus.WriteByte(0xFF21, 0xF1); // 15, decreasing every step.
        envelope.Bus.WriteByte(0xFF23, 0x80);
        Run(envelope, (8 * Edge) - 1);
        Assert.Equal(15, envelope.Apu.CaptureState().Noise.Envelope.Volume);
        Run(envelope, 1);
        Assert.Equal(14, envelope.Apu.CaptureState().Noise.Envelope.Volume);
        envelope.Bus.WriteByte(0xFF21, 0x00); // DAC off.
        Assert.False(On(envelope, 4));
        envelope.Bus.WriteByte(0xFF23, 0x80);
        Assert.False(On(envelope, 4));
    }
}
