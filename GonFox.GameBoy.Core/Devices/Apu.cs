namespace GonFox.GameBoy.Core.Devices;

using GonFox.GameBoy.Core.Audio;

// DMG APU: four channels and their mix, brought up to the clock on demand, not per T-cycle.
internal sealed class Apu
{
    private const int DivApuBit = 0x1000; // DIV bit 4.

    // T-cycles from the DIV-APU edge to the sweep clock of steps 2 and 6.
    private const int SweepDelay = 4;
    private const ulong NoSweep = ulong.MaxValue;
    private readonly Timer timer;
    private readonly Clock clock;
    private readonly byte[] registers = new byte[0x16]; // NR10-NR51 as written.
    private readonly PulseChannel pulse1 = new(hasSweep: true);
    private readonly PulseChannel pulse2 = new(hasSweep: false);
    private readonly WaveChannel wave = new();
    private readonly NoiseChannel noise = new();
    private readonly AudioMixer mixer;
    private bool powered;
    private bool skipStep;
    private bool dacsOn;
    private int frameStep; // Next DIV-APU step, 0..7.

    // Mixed level of each side, NR50 volume included.
    private int left;
    private int right;
    private ulong synced;
    private ulong sweepAt = NoSweep;

    internal Apu(Timer timer, Clock clock, AudioOutput output)
    {
        this.timer = timer;
        this.clock = clock;
        mixer = new AudioMixer(output);
    }

    internal sealed record State(byte[] Registers, byte[] WaveRam, bool Powered, int FrameStep, bool SkipStep, ulong PendingSweepAt,
        PulseChannel.State Pulse1, PulseChannel.State Pulse2, WaveChannel.State Wave, NoiseChannel.State Noise, AudioMixer.State Mixer);

    internal State CaptureState()
    {
        Synchronize();
        return new(registers.ToArray(), wave.Ram.ToArray(), powered, frameStep, skipStep, sweepAt, pulse1.CaptureState(),
            pulse2.CaptureState(), wave.CaptureState(), noise.CaptureState(), mixer.CaptureState());
    }

    internal static void ValidateState(State? state, ulong totalTCycles)
    {
        StateValidation.Require(state is not null, "APU");
        StateValidation.Length(state.Registers, 0x16, "APU registers");
        StateValidation.Length(state.WaveRam, 16, "Wave RAM");
        StateValidation.Require(state.FrameStep is >= 0 and <= 7 && PulseChannel.IsValid(state.Pulse1, true) &&
            PulseChannel.IsValid(state.Pulse2, false) && WaveChannel.IsValid(state.Wave) && NoiseChannel.IsValid(state.Noise) &&
            (state.Powered || (!state.Pulse1.Enabled && !state.Pulse2.Enabled && !state.Wave.Enabled && !state.Noise.Enabled && !state.SkipStep)) &&
            (state.PendingSweepAt == NoSweep || (state.Powered && state.PendingSweepAt > totalTCycles && state.PendingSweepAt - totalTCycles <= SweepDelay)),
            "APU channels");
        StateValidation.Require(AudioMixer.IsValid(state.Mixer, totalTCycles), "APU mixer");
    }

    internal void RestoreState(State state, ulong totalTCycles)
    {
        state.Registers.CopyTo(registers, 0);
        state.WaveRam.CopyTo(wave.Ram, 0);
        (powered, frameStep, skipStep, sweepAt) = (state.Powered, state.FrameStep, state.SkipStep, state.PendingSweepAt);
        pulse1.RestoreState(state.Pulse1);
        pulse2.RestoreState(state.Pulse2);
        wave.RestoreState(state.Wave);
        noise.RestoreState(state.Noise);
        mixer.RestoreState(state.Mixer);
        synced = totalTCycles;
        Mix();
    }

    private static ReadOnlySpan<byte> ReadMasks =>
        [0x80, 0x3F, 0, 0xFF, 0xBF, 0xFF, 0x3F, 0, 0xFF, 0xBF, 0x7F,
         0xFF, 0x9F, 0xFF, 0xBF, 0xFF, 0xFF, 0, 0, 0xBF, 0, 0];

    // Generates the PCM up to the current clock.
    internal void Synchronize() => CatchUp(clock.TotalTCycles);

    private void CatchUp(ulong until)
    {
        if (sweepAt <= until)
        {
            var at = sweepAt;
            sweepAt = NoSweep;
            Generate(at);
            pulse1.ClockSweep();
            Mix();
        }
        Generate(until);
    }

    private void Generate(ulong until)
    {
        if (until <= synced)
        {
            return;
        }

        // Only channels whose level can change are stepped one by one; the rest elapse at once.
        var pulse1Audible = Audible(pulse1.Enabled, pulse1.DacOn, 0, pulse1.Volume > 0);
        var pulse2Audible = Audible(pulse2.Enabled, pulse2.DacOn, 1, pulse2.Volume > 0);
        var waveAudible = Audible(wave.Enabled, wave.DacOn, 2, !wave.Muted);
        var noiseAudible = Audible(noise.Enabled, noise.DacOn, 3, noise.Volume > 0 && noise.Clocked);
        for (var at = synced; at < until;)
        {
            var cycles = (long)(until - at);
            if (pulse1Audible)
            {
                cycles = Math.Min(cycles, pulse1.Timer);
            }

            if (pulse2Audible)
            {
                cycles = Math.Min(cycles, pulse2.Timer);
            }

            if (waveAudible)
            {
                cycles = Math.Min(cycles, wave.Timer);
            }

            if (noiseAudible)
            {
                cycles = Math.Min(cycles, noise.Timer);
            }

            mixer.Advance(cycles, left, right, dacsOn);
            at += (ulong)cycles;
            var changed = (pulse1Audible && pulse1.Advance((int)cycles)) | (pulse2Audible && pulse2.Advance((int)cycles)) |
                (waveAudible && wave.Advance((int)cycles, at)) | (noiseAudible && noise.Advance((int)cycles));
            if (changed)
            {
                Mix();
            }
        }
        var elapsed = until - synced;
        if (!pulse1Audible)
        {
            pulse1.Elapse(elapsed);
        }

        if (!pulse2Audible)
        {
            pulse2.Elapse(elapsed);
        }

        if (!waveAudible)
        {
            wave.Elapse(elapsed, until);
        }

        if (!noiseAudible)
        {
            noise.Elapse(elapsed);
        }

        synced = until;
    }

    private bool Audible(bool enabled, bool dacOn, int channel, bool varies) =>
        enabled && dacOn && varies && (registers[0x15] & (0x11 << channel)) != 0;

    // Sums the DAC outputs (+15..-15, 0 when off) by NR51 routing and scales each side by NR50.
    private void Mix()
    {
        int routing = registers[0x15], volume = registers[0x14], mixLeft = 0, mixRight = 0;
        var dacs = false;
        Add(pulse1.DacOn, pulse1.Output, 0);
        Add(pulse2.DacOn, pulse2.Output, 1);
        Add(wave.DacOn, wave.Output, 2);
        Add(noise.DacOn, noise.Output, 3);
        mixLeft *= ((volume >> 4) & 7) + 1;
        mixRight *= (volume & 7) + 1;
        (left, right, dacsOn) = (mixLeft, mixRight, dacs);

        void Add(bool dacOn, int level, int channel)
        {
            if (!dacOn)
            {
                return;
            }

            dacs = true;
            var analog = 15 - (2 * level);
            if ((routing & (0x10 << channel)) != 0)
            {
                mixLeft += analog;
            }

            if ((routing & (0x01 << channel)) != 0)
            {
                mixRight += analog;
            }
        }
    }

    // Runs a DIV-APU step; while counting, it precedes the current T-cycle's channel steps.
    internal void ClockFrameSequencer(bool duringTick)
    {
        if (!powered)
        {
            return;
        }

        if (skipStep)
        {
            skipStep = false;
            return;
        }
        ulong now = clock.TotalTCycles, at = duringTick && now > 0 ? now - 1 : now;
        CatchUp(at);
        var step = frameStep;
        frameStep = (step + 1) & 7;
        if ((step & 3) == 2)
        {
            sweepAt = at + SweepDelay;
        }

        if ((step & 1) == 0)
        {
            pulse1.ClockLength();
            pulse2.ClockLength();
            wave.ClockLength();
            noise.ClockLength();
        }
        if (step == 7)
        {
            pulse1.ClockEnvelope();
            pulse2.ClockEnvelope();
            noise.ClockEnvelope();
        }
        Mix();
    }

    // Digital output 0..15 of a channel (1-based) at the current clock.
    internal int ChannelOutput(int channel)
    {
        Synchronize();
        return channel switch { 1 => pulse1.Output, 2 => pulse2.Output, 3 => wave.Output, 4 => noise.Output, _ => 0 };
    }

    internal byte ReadRegister(ushort address)
    {
        if (address is >= 0xFF30 and <= 0xFF3F)
        {
            if (!wave.Enabled)
            {
                return wave.Ram[address - 0xFF30];
            }

            return CpuReachesWaveRam() ? wave.Ram[wave.Position >> 1] : (byte)0xFF;
        }
        if (address == 0xFF26)
        {
            Synchronize(); // A pending sweep may stop CH1.
            return (byte)((powered ? 0xF0 : 0x70) | (pulse1.Enabled ? 1 : 0) | (pulse2.Enabled ? 2 : 0) |
                (wave.Enabled ? 4 : 0) | (noise.Enabled ? 8 : 0));
        }
        if (address is >= 0xFF10 and <= 0xFF25)
        {
            return (byte)(registers[address - 0xFF10] | ReadMasks[address - 0xFF10]);
        }

        return 0xFF;
    }

    // Reads Wave RAM for debugging, without the restrictions of a playing CH3.
    internal byte PeekWaveRam(ushort address) => wave.Ram[address - 0xFF30];

    // While CH3 plays, the CPU reaches Wave RAM only in the T-cycle of a sample read.
    private bool CpuReachesWaveRam()
    {
        Synchronize();
        return wave.ReadAt == clock.TotalTCycles;
    }

    internal void WriteRegister(ushort address, byte value)
    {
        if (address is >= 0xFF30 and <= 0xFF3F)
        {
            if (!wave.Enabled)
            {
                wave.Ram[address - 0xFF30] = value;
            }
            else if (CpuReachesWaveRam())
            {
                wave.Ram[wave.Position >> 1] = value;
            }

            return;
        }
        if (address == 0xFF26)
        {
            WritePower(value);
            return;
        }
        if (address is < 0xFF10 or > 0xFF25 or 0xFF15 or 0xFF1F)
        {
            return;
        }

        // While powered off, only length writes pass, without the duty bits of NR11/NR21.
        if (!powered && address is not (0xFF11 or 0xFF16 or 0xFF1B or 0xFF20))
        {
            return;
        }

        if (!powered && address is 0xFF11 or 0xFF16)
        {
            value &= 0x3F;
        }

        Synchronize();
        registers[address - 0xFF10] = value;
        var lengthNext = (frameStep & 1) == 0 && !skipStep;

        // A trigger settles its envelope and sweep timers after a DIV-APU step in this M-cycle.
        var stepNow = !skipStep && (timer.DividerCounter & ((DivApuBit * 2) - 1)) >= (DivApuBit * 2) - 4;
        bool envelopeNext = !skipStep && frameStep == (stepNow ? 6 : 7), sweepStepNow = stepNow && (frameStep & 3) == 2;
        switch (address)
        {
            case 0xFF10: pulse1.WriteSweep(value); break;
            case 0xFF11: pulse1.WriteLength(value, powered); break;
            case 0xFF12: pulse1.WriteEnvelope(value); break;
            case 0xFF13: pulse1.WritePeriodLow(value); break;
            case 0xFF14:
                if ((value & 0x80) != 0)
                {
                    sweepAt = NoSweep;
                }

                pulse1.WriteControl(value, lengthNext, envelopeNext, sweepStepNow);
                break;
            case 0xFF16: pulse2.WriteLength(value, powered); break;
            case 0xFF17: pulse2.WriteEnvelope(value); break;
            case 0xFF18: pulse2.WritePeriodLow(value); break;
            case 0xFF19: pulse2.WriteControl(value, lengthNext, envelopeNext); break;
            case 0xFF1A: wave.WriteDac(value); break;
            case 0xFF1B: wave.WriteLength(value); break;
            case 0xFF1C: wave.WriteLevel(value); break;
            case 0xFF1D: wave.WritePeriodLow(value); break;
            case 0xFF1E: wave.WriteControl(value, lengthNext); break;
            case 0xFF20: noise.WriteLength(value); break;
            case 0xFF21: noise.WriteEnvelope(value); break;
            case 0xFF22: noise.WriteControl(value); break;
            case 0xFF23: noise.WriteTrigger(value, lengthNext, envelopeNext); break;
        }
        Mix();
    }

    private void WritePower(byte value)
    {
        var powerOn = (value & 0x80) != 0;
        if (powerOn == powered)
        {
            return;
        }

        Synchronize();
        if (!powerOn)
        {
            Array.Clear(registers);
            pulse1.PowerOff();
            pulse2.PowerOff();
            wave.PowerOff();
            noise.PowerOff(); // Wave RAM is kept.
            skipStep = false;
            sweepAt = NoSweep;
        }
        else
        {
            // Starts at step 0, skipping the first edge if counter bit 12 is set at the M-cycle's end.
            frameStep = 0;
            skipStep = ((timer.DividerCounter + 4) & DivApuBit) != 0;
        }
        powered = powerOn;
        Mix();
    }

    internal void Reset()
    {
        var boot = DmgBootProfile.ApuRegisters;
        boot.CopyTo(registers);
        Array.Clear(wave.Ram);
        (powered, frameStep, skipStep, sweepAt) = (true, DmgBootProfile.ApuNextStep, false, NoSweep);
        pulse1.Boot(boot[0], boot[1] >> 6, boot[2], DmgBootProfile.Pulse1Period, DmgBootProfile.Pulse1Position, DmgBootProfile.Pulse1Timer);
        pulse2.PowerOff();
        wave.Boot(boot[0x0A], boot[0x0C]);
        noise.PowerOff();
        Mix();
        mixer.Reset(left, right); // Settled by the boot ROM.
        synced = clock.TotalTCycles;
    }

    // Before a boot ROM: powered off, every register clear, the high-pass discharged.
    internal void PowerOn()
    {
        Array.Clear(registers);
        pulse1.PowerOff();
        pulse2.PowerOff();
        wave.PowerOff();
        noise.PowerOff();
        (powered, frameStep, skipStep, sweepAt) = (false, 0, false, NoSweep);
        Mix();
        mixer.Reset(left, right);
        synced = clock.TotalTCycles;
    }
}
