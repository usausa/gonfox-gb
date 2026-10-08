namespace GonFox.GameBoy.Platform;

using System.Diagnostics;

using GonFox.GameBoy.Core;
using GonFox.GameBoy.Core.Audio;
using GonFox.GameBoy.Core.Cartridge;

// Checks the PCM the runner hands to the host queue against the core run directly.
public sealed class RunnerAudioTests
{
    private sealed class RecordingSink : IPcmSink
    {
        private readonly Lock gate = new();
        private readonly List<short> recorded = [];
        private int flushes;

        public void Write(ReadOnlySpan<short> samples)
        {
            lock (gate)
            {
                recorded.AddRange(samples);
            }
        }

        public void Flush() => Interlocked.Increment(ref flushes);
        internal int Flushes => Volatile.Read(ref flushes);
        internal int Count
        {
            get
            {
                lock (gate)
                {
                    return recorded.Count;
                }
            }
        }

        internal short[] From(int index)
        {
            lock (gate)
            {
                return recorded[index..].ToArray();
            }
        }
    }

    private static readonly byte[] SoundCheck = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "sound-check.gb"));

    private static async Task Until(Func<bool> predicate)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate() && watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(10).ConfigureAwait(false);
        }

        Assert.True(predicate(), "Worker did not reach the expected state within ten seconds.");
    }

    // Runs the core alone by instructions up to T-cycle t and returns its PCM.
    private static short[] CorePcm(ulong t)
    {
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(SoundCheck).Cartridge);
        var pcm = new List<short>();
        var buffer = new short[AudioOutput.CapacityFrames * 2];
        while (system.TotalTCycles < t)
        {
            system.StepInstruction();
            if (system.Audio.QueuedFrameCount > 1_000)
            {
                pcm.AddRange(buffer.AsSpan(0, system.Audio.ReadFrames(buffer) * 2));
            }
        }
        Assert.Equal(t, system.TotalTCycles);
        pcm.AddRange(buffer.AsSpan(0, system.Audio.ReadFrames(buffer) * 2));
        return [.. pcm];
    }

    private static int Samples(ulong t) => (int)(t * AudioOutput.SampleRate / GameBoySystem.TCyclesPerSecond) * 2;

    [Fact]
    public async Task RunningHandsOverTheCorePcmInOrderAndStopsFlush()
    {
        var sink = new RecordingSink();
        using var runner = new EmulationRunner(sink);
        await runner.LoadAsync(SoundCheck, "sound");
        await Until(() => runner.Status.Registers.TotalTCycles > 600_000);
        var flushes = sink.Flushes;
        await runner.PauseAsync();
        Assert.True(sink.Flushes > flushes);
        var t = runner.Status.Registers.TotalTCycles;
        var recorded = sink.From(0);
        Assert.Equal(Samples(t), recorded.Length);
        Assert.Equal(CorePcm(t), recorded);
        Assert.Contains(recorded, sample => sample != 0);

        foreach (var command in new[] { runner.StepAsync, runner.ResetAsync, runner.CaptureStateAsync, runner.RestoreStateAsync })
        {
            flushes = sink.Flushes;
            await command();
            Assert.True(sink.Flushes > flushes);
        }
        Assert.Equal(recorded.Length, sink.Count);
    }

    [Fact]
    public async Task ResumingStartsWithNewSoundAfterStepsOrARestoredState()
    {
        var sink = new RecordingSink();
        using var runner = new EmulationRunner(sink);
        await runner.LoadAsync(SoundCheck, "sound");
        await Until(() => runner.Status.Registers.TotalTCycles > 200_000);
        await runner.PauseAsync();
        for (var i = 0; i < 300; i++)
        {
            await runner.StepAsync(); // Leaves PCM in the core.
        }

        await runner.CaptureStateAsync(); // Saved with the state.
        var saved = runner.Status.Registers.TotalTCycles;

        var mark = sink.Count;
        await runner.ResumeAsync();
        await Until(() => runner.Status.Registers.TotalTCycles > saved + 300_000);
        await runner.PauseAsync();
        var first = runner.Status.Registers.TotalTCycles;
        var afterResume = sink.From(mark);

        await runner.RestoreStateAsync();
        mark = sink.Count;
        await runner.ResumeAsync();
        await Until(() => runner.Status.Registers.TotalTCycles > saved + 300_000);
        await runner.PauseAsync();
        var second = runner.Status.Registers.TotalTCycles;
        var afterRestore = sink.From(mark);

        var core = CorePcm(Math.Max(first, second));
        Assert.Equal(core[Samples(saved)..Samples(first)], afterResume);
        Assert.Equal(core[Samples(saved)..Samples(second)], afterRestore);
    }
}
