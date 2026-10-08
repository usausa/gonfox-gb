namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Devices;

// CH1/CH2 tests; the frame sequencer steps every 8,192 T-cycles from T=8192.
[Trait("Category", "Unit")]
public sealed class ApuPulseTests
{
    private const int Edge = 8192;
    private static readonly ushort[] Channel1Registers = [0xFF11, 0xFF12, 0xFF13, 0xFF14];

    private static PeripheralTestMachine Powered()
    {
        var m = new PeripheralTestMachine();
        m.Bus.WriteByte(0xFF26, 0x00);
        m.Bus.WriteByte(0xFF26, 0x80); // Next step 0.
        return m;
    }

    private static ushort Reg(int channel, int index) => (ushort)((channel == 1 ? 0xFF10 : 0xFF15) + index);
    private static void Run(PeripheralTestMachine m, int cycles) => m.Clock.AdvanceTCycles(cycles);
    private static bool On(PeripheralTestMachine m, int channel) => (m.Bus.ReadByte(0xFF26) & (1 << (channel - 1))) != 0;

    // Starts a channel: duty, NRx2, the 11-bit period and NRx4's length enable.
    private static void Start(PeripheralTestMachine m, int channel, int duty, byte envelope, int period, bool length = false)
    {
        m.Bus.WriteByte(Reg(channel, 1), (byte)((duty << 6) | 0x3F)); // Length 1 if enabled.
        m.Bus.WriteByte(Reg(channel, 2), envelope);
        m.Bus.WriteByte(Reg(channel, 3), (byte)period);
        m.Bus.WriteByte(Reg(channel, 4), (byte)(0x80 | (length ? 0x40 : 0) | (period >> 8)));
    }

    // Level changes over the next cycles, starting with the current level at offset 0.
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

    private static int Peak(PeripheralTestMachine m, int channel, int cycles)
    {
        var peak = 0;
        for (var cycle = 0; cycle < cycles; cycle++)
        {
            Run(m, 1);
            peak = Math.Max(peak, m.Apu.ChannelOutput(channel));
        }
        return peak;
    }

    public static TheoryData<int, int, int[]> Duties() => new()
    {
        // Channel, duty and the first three level changes at period $700.
        { 1, 0, [7176, 8200, 15368] }, { 2, 0, [7176, 8200, 15368] },
        { 1, 1, [7176, 9224, 15368] }, { 2, 1, [7176, 9224, 15368] },
        { 1, 2, [5128, 9224, 13320] }, { 2, 2, [5128, 9224, 13320] },
        { 1, 3, [1032, 7176, 9224] }, { 2, 3, [1032, 7176, 9224] }
    };

    [Theory]
    [MemberData(nameof(Duties))]
    public void LevelsFollowTheDmgDutyStepsAfterASilentFirstStep(int channel, int duty, int[] edges)
    {
        var m = Powered();
        Start(m, channel, duty, 0xF0, 0x700);
        var trace = Trace(m, channel, 16384);
        Assert.Equal([(0, 0), (edges[0], 15), (edges[1], 0), (edges[2], 15)], trace.Take(4));
    }

    [Fact]
    public void RetriggerKeepsTheDutyStepAndRestartsThePeriod()
    {
        var m = Powered();
        Start(m, 1, 2, 0xF0, 0x700);
        Run(m, 5220); // Duty step 5, high.
        m.Bus.WriteByte(0xFF14, 0x87);
        Assert.Equal([(0, 15), (4100, 0)], Trace(m, 1, 4200));
    }

    [Fact]
    public void PeriodWritesTakeEffectAtTheNextReload()
    {
        var m = Powered();
        Start(m, 2, 3, 0xF0, 0x700);
        Run(m, 100);
        m.Bus.WriteByte(0xFF18, 0x80); // Period $780.
        Assert.Equal([(0, 0), (932, 15), (4004, 0)], Trace(m, 2, 4100));
    }

    [Fact]
    public void RestartFromOffIsSilentUntilTheFirstStep()
    {
        var m = Powered();
        Start(m, 1, 3, 0xF0, 0x700); // Duty 3: steps 1-6 high.
        Run(m, 2100); // Duty step 2.
        Assert.Equal(15, m.Apu.ChannelOutput(1));
        m.Bus.WriteByte(0xFF12, 0x00); // DAC off stops the channel.
        m.Bus.WriteByte(0xFF12, 0xF0);
        m.Bus.WriteByte(0xFF14, 0x87); // Restart from off.
        Assert.Equal([(0, 0), (1032, 15)], Trace(m, 1, 1100));
    }

    [Fact]
    public void DutyWritesChangeTheLevelFromTheNextStep()
    {
        var m = Powered();
        Start(m, 1, 0, 0xF0, 0x700);
        Run(m, 3180);
        m.Bus.WriteByte(0xFF11, 0xFF); // Duty 3.
        Assert.Equal([(0, 0), (924, 15)], Trace(m, 1, 1000)); // High from step 4.
        m.Bus.WriteByte(0xFF11, 0x3F); // Duty 0.
        Assert.Equal([(0, 15), (948, 0)], Trace(m, 1, 1000)); // Low from step 5.
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void DacOffStopsTheChannelAndTriggersCannotStartIt(int channel)
    {
        var m = Powered();
        Start(m, channel, 2, 0x08, 0x700); // Volume 0, DAC on.
        Assert.True(On(m, channel));
        m.Bus.WriteByte(Reg(channel, 2), 0x00);
        Assert.False(On(m, channel));
        Assert.Equal(0, m.Apu.ChannelOutput(channel));
        m.Bus.WriteByte(Reg(channel, 4), 0x87);
        Assert.False(On(m, channel));
        m.Bus.WriteByte(Reg(channel, 2), 0x10); // DAC on again.
        Assert.False(On(m, channel));
        m.Bus.WriteByte(Reg(channel, 4), 0x87);
        Assert.True(On(m, channel));
    }

    [Fact]
    public void LengthEndsTheChannelOnAFrameSequencerLengthStep()
    {
        var m = Powered();
        Start(m, 1, 2, 0xF0, 0x700, length: true); // No extra length clock.
        Run(m, Edge - 1);
        Assert.True(On(m, 1));
        Run(m, 1);
        Assert.False(On(m, 1));
    }

    [Fact]
    public void EnablingLengthBeforeAStepWithoutLengthClocksItOnce()
    {
        var early = Powered();
        Start(early, 2, 2, 0xF0, 0x700);
        early.Bus.WriteByte(0xFF16, 0x3F); // Length 1.
        early.Bus.WriteByte(0xFF19, 0x47); // Length enable, no trigger.
        Assert.True(On(early, 2));

        var late = Powered();
        Start(late, 2, 2, 0xF0, 0x700);
        Run(late, Edge); // Next step 1 clocks no length.
        late.Bus.WriteByte(0xFF16, 0x3F);
        late.Bus.WriteByte(0xFF19, 0x47); // The extra clock stops it.
        Assert.False(On(late, 2));

        var two = Powered();
        Start(two, 2, 2, 0xF0, 0x700);
        Run(two, Edge);
        two.Bus.WriteByte(0xFF16, 0x3E); // Length 2.
        two.Bus.WriteByte(0xFF19, 0x47);
        Assert.True(On(two, 2));
        Run(two, (2 * Edge) - 1);
        Assert.True(On(two, 2));
        Run(two, 1);
        Assert.False(On(two, 2));
    }

    [Fact]
    public void TriggeringAnEmptyLengthBeforeAStepWithoutLengthLoads63()
    {
        var m = Powered(); // Power off emptied the counter.
        Run(m, Edge + 100);
        m.Bus.WriteByte(0xFF12, 0xF0);
        m.Bus.WriteByte(0xFF14, 0xC7);
        Run(m, (127 * Edge) - (Edge + 100) - 1);
        Assert.True(On(m, 1));
        Run(m, 1);
        Assert.False(On(m, 1));
    }

    [Fact]
    public void EnvelopeStepsOnStep7AndStopsAtItsLimits()
    {
        var down = Powered();
        Start(down, 1, 2, 0xF1, 0x7FF); // 15, decreasing every step.
        Run(down, (8 * Edge) - 64);
        Assert.Equal(15, Peak(down, 1, 32));
        Run(down, 64);
        Assert.Equal(14, Peak(down, 1, 32));
        Run(down, 15 * 8 * Edge);
        Assert.Equal(0, Peak(down, 1, 32));
        Assert.True(On(down, 1)); // Volume 0 does not stop the channel.

        var up = Powered();
        Start(up, 2, 2, 0x09, 0x7FF); // 0, increase.
        Run(up, 20 * 8 * Edge);
        Assert.Equal(15, Peak(up, 2, 32));

        var zero = Powered();
        Start(zero, 2, 2, 0x80, 0x7FF); // Period 0: no envelope steps.
        Run(zero, 3 * 8 * Edge);
        Assert.Equal(8, Peak(zero, 2, 32));
    }

    [Fact]
    public void TriggerJustBeforeTheEnvelopeStepWaitsOneMoreEnvelopePeriod()
    {
        var m = Powered();
        Run(m, (7 * Edge) + 100); // Step 7 comes next.
        Start(m, 1, 2, 0xF1, 0x7FF);
        Run(m, (8 * Edge) - ((7 * Edge) + 100) + 64);
        Assert.Equal(15, Peak(m, 1, 32));
        Run(m, 8 * Edge);
        Assert.Equal(14, Peak(m, 1, 32));
    }

    [Theory]
    [InlineData((7 * Edge) - 8, 8 * Edge)]  // Step 6 after the M-cycle.
    [InlineData((7 * Edge) - 4, 16 * Edge)] // Step 6 within the M-cycle.
    [InlineData((8 * Edge) - 4, 8 * Edge)]  // Step 7 within the M-cycle.
    public void TriggerSettlesTheEnvelopeAfterAStepInItsMachineCycle(int trigger, int firstStep)
    {
        var m = Powered();
        Run(m, trigger);
        Start(m, 1, 2, 0xF1, 0x7FF); // 15, decreasing every step.
        Run(m, firstStep - 1 - trigger);
        Assert.Equal(15, m.Apu.CaptureState().Pulse1.Envelope.Volume);
        Run(m, 1);
        Assert.Equal(14, m.Apu.CaptureState().Pulse1.Envelope.Volume);
    }

    [Fact]
    public void WritingNrx2WhilePlayingAddsOneOnlyAfterARunningPeriod0Envelope()
    {
        static int Volume(PeripheralTestMachine machine, int channel) => channel == 2
            ? machine.Apu.CaptureState().Pulse2.Envelope.Volume : machine.Apu.CaptureState().Noise.Envelope.Volume;
        var m = Powered();
        Start(m, 2, 2, 0xA0, 0x7FF); // Volume 10, period 0.
        m.Bus.WriteByte(0xFF17, 0xA8);
        Assert.Equal(11, Volume(m, 2));
        m.Bus.WriteByte(0xFF17, 0xA1); // Old period still 0.
        Assert.Equal(12, Volume(m, 2));
        m.Bus.WriteByte(0xFF17, 0xA0); // Old period 1: unchanged.
        Assert.Equal(12, Volume(m, 2));

        var wrap = Powered();
        Start(wrap, 2, 2, 0xF0, 0x7FF);
        wrap.Bus.WriteByte(0xFF17, 0xF0); // 15 + 1 wraps to 0.
        Assert.Equal(0, Volume(wrap, 2));

        var stopped = Powered(); // Not playing.
        stopped.Bus.WriteByte(0xFF17, 0xA0);
        stopped.Bus.WriteByte(0xFF17, 0xA8);
        Assert.Equal(0, Volume(stopped, 2));

        var noise = Powered(); // CH4 uses the same envelope.
        noise.Bus.WriteByte(0xFF21, 0x50);
        noise.Bus.WriteByte(0xFF23, 0x80);
        noise.Bus.WriteByte(0xFF21, 0x58);
        Assert.Equal(6, Volume(noise, 4));
    }

    [Fact]
    public void SweepAddsOnSteps2And6UntilTheSecondCheckOverflows()
    {
        var m = Powered();
        m.Bus.WriteByte(0xFF10, 0x11); // Pace 1, addition, shift 1.
        Start(m, 1, 2, 0xF0, 0x100);
        int[] periods = [0x180, 0x240, 0x360, 0x510];
        for (var i = 0; i < periods.Length; i++)
        {
            var edge = 3 + (4 * i); // Steps 2 and 6.
            Run(m, (edge * Edge) + 4 - (int)m.Clock.TotalTCycles);
            Assert.Equal(periods[i], m.Apu.CaptureState().Pulse1.Period);
        }
        Run(m, (19 * Edge) - (int)m.Clock.TotalTCycles);
        Assert.True(On(m, 1));
        Run(m, 4); // The second check overflows.
        Assert.False(On(m, 1));
        Assert.Equal(0x798, m.Apu.CaptureState().Pulse1.Period);
    }

    [Theory]
    [InlineData((3 * Edge) - 8, 7 * Edge)]  // Step 2 after the M-cycle.
    [InlineData((3 * Edge) - 4, 11 * Edge)] // Step 2 within the M-cycle.
    [InlineData(3 * Edge, 11 * Edge)]     // Step 2's clock pending.
    public void SweepClockComesAfterItsEdgeAndNotForATriggerBeforeIt(int trigger, int lastEdge)
    {
        var m = Powered();
        m.Bus.WriteByte(0xFF10, 0x20); // Pace 2, addition, shift 0.
        Run(m, trigger);
        Start(m, 1, 2, 0xF0, 0x700);
        Run(m, lastEdge - trigger);
        Assert.True(On(m, 1)); // Sweep clock still pending.
        Run(m, 4);
        Assert.False(On(m, 1));
    }

    [Fact]
    public void StateKeepsAPendingSweepClockAndTheLongestTimers()
    {
        // Captured between a step-2 edge and its sweep clock.
        var m = Powered();
        m.Bus.WriteByte(0xFF10, 0x01); // Pace 0, addition, shift 1.
        Run(m, (3 * Edge) - 4);
        Start(m, 1, 2, 0xF0, 0x000);
        Run(m, 4);
        var state = m.Apu.CaptureState();
        Assert.Equal(((3UL * Edge) + 3, 9, 8196), (state.PendingSweepAt, state.Pulse1.SweepTimer, state.Pulse1.Timer));
        var copy = Powered();
        copy.Clock.RestoreState(m.Clock.TotalTCycles);
        copy.Timer.RestoreState(m.Timer.CaptureState());
        Apu.ValidateState(state, m.Clock.TotalTCycles);
        copy.Apu.RestoreState(state, m.Clock.TotalTCycles);
        Run(m, 8);
        Run(copy, 8);
        Assert.Equal(m.Apu.CaptureState().Pulse1, copy.Apu.CaptureState().Pulse1);
        Assert.Equal((ulong.MaxValue, 8), (copy.Apu.CaptureState().PendingSweepAt, copy.Apu.CaptureState().Pulse1.SweepTimer));

        var off = Powered(); // Power off drops a pending sweep clock.
        off.Bus.WriteByte(0xFF10, 0x01);
        Start(off, 1, 2, 0xF0, 0x000);
        Run(off, 3 * Edge);
        off.Bus.WriteByte(0xFF26, 0x00);
        Apu.ValidateState(off.Apu.CaptureState(), off.Clock.TotalTCycles);
    }

    [Fact]
    public void TriggerChecksSweepOverflowEvenWithPace0()
    {
        var m = Powered();
        m.Bus.WriteByte(0xFF10, 0x01); // Pace 0, shift 1.
        Start(m, 1, 2, 0xF0, 0x600); // Overflows.
        Assert.False(On(m, 1));
        Start(m, 1, 2, 0xF0, 0x500); // Fits.
        Run(m, 40 * Edge);
        Assert.True(On(m, 1));
        Assert.Equal(0x500, m.Apu.CaptureState().Pulse1.Period);
    }

    [Fact]
    public void LeavingSubtractionAfterASubtractingCalculationStopsCh1()
    {
        var shifted = Powered();
        shifted.Bus.WriteByte(0xFF10, 0x19); // Pace 1, subtraction, shift 1.
        Start(shifted, 1, 2, 0xF0, 0x400);
        shifted.Bus.WriteByte(0xFF10, 0x11);
        Assert.False(On(shifted, 1));

        var unshifted = Powered();
        unshifted.Bus.WriteByte(0xFF10, 0x18); // Shift 0: no calculation yet.
        Start(unshifted, 1, 2, 0xF0, 0x400);
        unshifted.Bus.WriteByte(0xFF10, 0x10);
        Assert.True(On(unshifted, 1));
        unshifted.Bus.WriteByte(0xFF10, 0x18);
        Run(unshifted, (3 * Edge) + 4); // Step 2's sweep clock subtracts.
        Assert.Equal(0x400, unshifted.Apu.CaptureState().Pulse1.Period);
        unshifted.Bus.WriteByte(0xFF10, 0x10);
        Assert.False(On(unshifted, 1));
    }

    [Fact]
    public void DividerWritesStepTheSequencerOnlyWhenBit12Falls()
    {
        var set = Powered();
        Start(set, 1, 2, 0xF0, 0x700, length: true);
        Run(set, 4096); // DIV bit 12 set.
        set.Bus.WriteByte(0xFF04, 0); // Bit 12 falls.
        Assert.False(On(set, 1));

        var clear = Powered();
        Start(clear, 1, 2, 0xF0, 0x700, length: true);
        Run(clear, 100);
        clear.Bus.WriteByte(0xFF04, 0); // Bit 12 was clear: no step.
        Run(clear, Edge - 1);
        Assert.True(On(clear, 1));
        Run(clear, 1);
        Assert.False(On(clear, 1));

        var stop = Powered();
        Start(stop, 1, 2, 0xF0, 0x700, length: true);
        Run(stop, 4096);
        stop.Clock.ResetDivider(); // As STOP does.
        Assert.False(On(stop, 1));
    }

    [Fact]
    public void BusWritesSeeTheSequencerBeforeEdgesInTheirMachineCycle()
    {
        var before = Powered();
        Start(before, 1, 2, 0xF0, 0x700);
        Run(before, Edge - 4); // Edge in the next M-cycle.
        before.Bus.WriteByte(0xFF11, 0x3F);
        before.Bus.WriteByte(0xFF14, 0x47); // No extra clock yet.
        Assert.True(On(before, 1));
        before.Clock.AdvanceMachineCycle();
        Assert.False(On(before, 1));

        var after = Powered();
        Start(after, 1, 2, 0xF0, 0x700);
        Run(after, Edge); // Edge already passed.
        after.Bus.WriteByte(0xFF11, 0x3F);
        after.Bus.WriteByte(0xFF14, 0x47);
        Assert.False(On(after, 1));
    }

    [Fact]
    public void TheDivEdgeIsHandledBeforeTheChannelsInTheSameTCycle()
    {
        var m = Powered();
        Start(m, 1, 2, 0xF0, 0x700, length: true); // 8th duty step at the edge.
        Run(m, Edge);
        var pulse = m.Apu.CaptureState().Pulse1;
        Assert.False(pulse.Enabled); // Stopped before its 8th step.
        Assert.Equal(7, pulse.Position);
    }

    [Fact]
    public void PowerOffClearsChannelsAndKeepsOnlyLengthWrites()
    {
        var m = Powered();
        Start(m, 1, 3, 0xF0, 0x700);
        Start(m, 2, 3, 0xF0, 0x700);
        Run(m, 3000); // Duty steps moved.
        m.Bus.WriteByte(0xFF26, 0x00);
        Assert.Equal(0x70, m.Bus.ReadByte(0xFF26));
        Assert.Equal(0, m.Apu.ChannelOutput(1));
        m.Bus.WriteByte(0xFF12, 0xF0); // Ignored while off.
        m.Bus.WriteByte(0xFF11, 0xFF); // Only the length is kept.
        Assert.Equal(0x3F, m.Bus.ReadByte(0xFF11));
        Assert.Equal(0x00, m.Bus.ReadByte(0xFF12));
        m.Bus.WriteByte(0xFF26, 0x8F); // The channel bits are read-only.
        Assert.Equal(0xF0, m.Bus.ReadByte(0xFF26));
        var pulse = m.Apu.CaptureState().Pulse1;
        Assert.Equal((0, 1, 0, false), (pulse.Position, pulse.Length.Counter, pulse.Duty, pulse.Enabled));
        m.Bus.WriteByte(0xFF12, 0xF0);
        m.Bus.WriteByte(0xFF14, 0xC7); // Next step 0: no extra clock.
        Run(m, Edge - 3000 - 1);
        Assert.True(On(m, 1));
        Run(m, 1);
        Assert.False(On(m, 1));
    }

    [Fact]
    public void PoweringOnWhileDivBit12IsSetIgnoresTheFirstEdge()
    {
        var m = new PeripheralTestMachine();
        Run(m, 4096); // DIV bit 12 set.
        m.Bus.WriteByte(0xFF26, 0x00);
        m.Bus.WriteByte(0xFF26, 0x80);
        Start(m, 1, 2, 0xF0, 0x700);
        Run(m, Edge - 4096 + 100); // First edge skipped.
        m.Bus.WriteByte(0xFF11, 0x3F);
        m.Bus.WriteByte(0xFF14, 0x47); // Next step 0: no extra clock.
        Assert.True(On(m, 1));
        Run(m, Edge - 101);
        Assert.True(On(m, 1));
        Run(m, 1);
        Assert.False(On(m, 1));
    }

    [Theory]
    [InlineData(4088, false)] // Bit 12 rises after the M-cycle.
    [InlineData(4092, true)]  // Bit 12 rises within the M-cycle.
    public void PoweringOnDecidesTheSkippedEdgeAtTheEndOfItsMachineCycle(int cycles, bool skips)
    {
        var m = new PeripheralTestMachine();
        Run(m, cycles);
        m.Bus.WriteByte(0xFF26, 0x00);
        m.Bus.WriteByte(0xFF26, 0x80);
        Assert.Equal(skips, m.Apu.CaptureState().SkipStep);
    }

    [Fact]
    public void BootBypassLeavesCh1OnAndSilent()
    {
        var m = new PeripheralTestMachine();
        Assert.Equal(0xF1, m.Bus.ReadByte(0xFF26));
        Assert.Equal(0, Peak(m, 1, 1024));
        Assert.Equal([0xBF, 0xF3, 0xFF, 0xBF], Channel1Registers.Select(a => (int)m.Bus.ReadByte(a)));
    }

    [Fact]
    public void BootBypassLeavesTheSequencerAfterStep0AndCh1OnDutyStep3()
    {
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(TestRom.Create(0x18, 0xFE)).Cartridge);
        var apu = system.CaptureState().Apu;
        Assert.Equal((1, 3, 276), (apu.FrameStep, apu.Pulse1.Position, apu.Pulse1.Timer));
        system.RunForTCycles(276); // 252 T per duty step.
        Assert.Equal(4, system.CaptureState().Apu.Pulse1.Position);
        system.RunForTCycles(528 - (int)system.TotalTCycles);
        Assert.Equal(5, system.CaptureState().Apu.Pulse1.Position);
        system.RunForTCycles(5172 - (int)system.TotalTCycles); // DIV bit 12 first falls.
        Assert.Equal(2, system.CaptureState().Apu.FrameStep);
    }

    [Fact]
    public void HaltRunsTheApuAndStopFreezesItAfterResettingDiv()
    {
        byte[] setup =
        [
            0xAF, 0xE0, 0x26, 0xE0, 0x04,         // APU off, DIV = 0
            0x3E, 0x80, 0xE0, 0x26,               // APU on: next step 0
            0x3E, 0x3F, 0xE0, 0x11,               // Length 1
            0x3E, 0xF0, 0xE0, 0x12,               // DAC on
            0x3E, 0xC7, 0xE0, 0x14               // Trigger with length
        ];
        var halt = TestRom.Start([.. setup, 0xAF, 0xE0, 0xFF, 0x76, 0x18, 0xFE]); // IE=0, HALT forever
        halt.RunForTCycles(4000);
        Assert.True(halt.IsHalted);
        Assert.Equal(1, Nr52(halt) & 1);
        halt.RunForTCycles(Edge); // Length ends while halted.
        Assert.Equal(0, Nr52(halt) & 1);

        // Waits for DIV bit 12, then STOP's DIV reset steps the sequencer at once.
        var stop = TestRom.Start([.. setup, 0xF0, 0x04, 0xE6, 0x10, 0x28, 0xFA, 0x10, 0x00]);
        stop.RunForTCycles(Edge);
        Assert.True(stop.IsStopped);
        Assert.Equal(0, Nr52(stop) & 1);
        var frozen = stop.CaptureState();
        Assert.Equal(0L, stop.RunForTCycles(20_000).ExecutedTCycles);
        StateTests.EqualState(frozen, stop.CaptureState());
    }

    private static byte Nr52(GameBoySystem system)
    {
        var value = new byte[1];
        system.CopyMemory(0xFF26, value);
        return value[0];
    }

    [Fact]
    public void RestoringMidNoteRepeatsTheSameApuFuture()
    {
        byte[] program =
        [
            0x3E, 0x23, 0xE0, 0x10,   // Sweep pace 2, addition, shift 3
            0x3E, 0x80, 0xE0, 0x11,   // Duty 2, length 64
            0x3E, 0xF3, 0xE0, 0x12,   // 15, decrease every 3 envelope steps
            0x3E, 0x20, 0xE0, 0x13,
            0x3E, 0xC4, 0xE0, 0x14,   // Period $420, trigger with length
            0x3E, 0xC0, 0xE0, 0x16,   // CH2 duty 3, length 64
            0x3E, 0xA8, 0xE0, 0x17,   // 10, increase
            0x3E, 0xC0, 0xE0, 0x18,
            0x3E, 0x87, 0xE0, 0x19,   // Period $7C0, trigger
            0x18, 0xFE
        ];
        var system = TestRom.Start(program);
        system.RunForTCycles(40_000); // Mid-sweep, mid-envelope and mid-period.
        StateTests.ReplayTwice(system, s => s.RunForTCycles(200_000));
        var saved = system.CaptureState();
        Assert.True(saved.Apu.Pulse1.Enabled && saved.Apu.Pulse2.Enabled);
        Assert.NotEqual(0x420, saved.Apu.Pulse1.Period); // The sweep already moved the period.

        // Many short runs and one long run end on the same boundary with the same state.
        GameBoySystem split = TestRom.Start(program), whole = TestRom.Start(program);
        var start = whole.TotalTCycles;
        for (var i = 0; i < 300; i++)
        {
            split.RunForTCycles(997);
        }

        whole.RunForTCycles((int)(split.TotalTCycles - start));
        Assert.Equal(split.TotalTCycles, whole.TotalTCycles);
        StateTests.EqualState(split.CaptureState(), whole.CaptureState());
    }
}
