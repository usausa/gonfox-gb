namespace GonFox.GameBoy.Core;

using Benchmark;

using GonFox.GameBoy.Core.Audio;

[Trait("Category", "Unit")]
public sealed class BenchmarkWorkloadTests
{
    [Theory]
    [InlineData(ExecutionCase.Alu)]
    [InlineData(ExecutionCase.MemoryBranch)]
    [InlineData(ExecutionCase.TimerDma)]
    [InlineData(ExecutionCase.Sound)]
    public void SyntheticCpuRunsWithoutHaltingAndReplaysAllState(ExecutionCase scenario)
    {
        var workload = new ExecutionWorkload(scenario);
        workload.System.RunForTCycles(8192);
        var state = workload.System.CaptureState();
        Assert.False(state.Cpu.Halted);
        Assert.False(state.Cpu.Stopped);
        Assert.Equal(0, state.Ppu.Lcdc);
        if (scenario == ExecutionCase.MemoryBranch)
        {
            Assert.Contains(state.Memory.WorkRam, value => value != 0);
        }

        if (scenario == ExecutionCase.TimerDma)
        {
            Assert.InRange(state.Cpu.PC, (ushort)0xFF80, (ushort)0xFF8A);
            Assert.Equal(5, state.Timer.Tac);
            Assert.NotEqual(0, state.Interrupts.Requested & 4);
            Assert.Equal(0x5A, state.Ppu.Oam[0]);
        }
        if (scenario == ExecutionCase.Sound)
        {
            Assert.True(state.Apu.Pulse1.Enabled && state.Apu.Pulse2.Enabled && state.Apu.Wave.Enabled && state.Apu.Noise.Enabled);
            Assert.Equal(AudioOutput.CapacityFrames * 2, state.Audio.Queued.Length);
            Assert.True(state.Audio.Queued.Distinct().Count() > 100);
        }
        workload.Reset();
        workload.System.RunForTCycles(8192);
        Assert.Equal(ExecutionWorkload.Fingerprint(state), ExecutionWorkload.Fingerprint(workload.System.CaptureState()));
    }

    [Theory]
    [InlineData(PpuCase.Background)]
    [InlineData(PpuCase.Window)]
    [InlineData(PpuCase.Sprites)]
    public void PpuFixtureMatchesIndependentPixelSpecification(PpuCase scenario)
    {
        var workload = new PpuWorkload(scenario);
        for (var i = 0; i < 2 * 70_224; i++)
        {
            workload.Ppu.Tick(); // First frame is not shown.
        }

        Assert.Equal(2UL, workload.Video.CompletedFrameCount);
        var pixels = workload.Pixels();
        for (var y = 0; y < 144; y++)
        {
            for (var x = 0; x < 160; x++)
            {
                var offset = ((y * 160) + x) * 4;
                var expected = PpuWorkload.Expected(scenario, x, y);
                Assert.Equal(expected, pixels[offset]);
                Assert.Equal(expected, pixels[offset + 1]);
                Assert.Equal(expected, pixels[offset + 2]);
                Assert.Equal(255, pixels[offset + 3]);
            }
        }
    }

    [Theory]
    [InlineData(false, 526336)]
    [InlineData(true, 530431)]
    public void BusFixtureReallyExercisesBanksAndEchoRam(bool mbc1, int expected)
    {
        var workload = new BusWorkload(mbc1);
        Assert.Equal(expected, workload.Run());
        Assert.Equal(expected, workload.Run());
    }
}
