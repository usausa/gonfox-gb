namespace GonFox.GameBoy.Platform;

public sealed class AudioBufferTests
{
    // Frame i holds (i, -i): order and sides stay visible after the conversion to floats.
    private static short[] Ramp(int from, int count) =>
        Enumerable.Range(from, count).SelectMany(i => new[] { (short)i, (short)-i }).ToArray();

    private static float[] Expected(int from, int count, float gain = 1) =>
        Enumerable.Range(from, count).SelectMany(i => new[] { i / 32768f * gain, -i / 32768f * gain }).ToArray();

    private static float[] Read(AudioBufferReader reader, int frames)
    {
        var buffer = new float[frames * 2];
        Assert.Equal(buffer.Length, reader.Read(buffer));
        return buffer;
    }

    [Fact]
    public void PlaybackWaitsForFortyMillisecondsThenKeepsOrderAndSides()
    {
        var queue = new AudioBuffer();
        var reader = queue.Start();
        queue.Write(Ramp(0, AudioBuffer.TargetFrames - 1));
        Assert.All(Read(reader, 480), sample => Assert.Equal(0, sample));
        Assert.True(queue.Stats.Priming);
        queue.Write(Ramp(AudioBuffer.TargetFrames - 1, 1));
        Assert.Equal(Expected(0, 480), Read(reader, 480));
        Assert.False(queue.Stats.Priming);
        Assert.Equal(AudioBuffer.TargetFrames - 480, queue.Stats.QueuedFrames);
    }

    [Fact]
    public void AShortageIsFilledWithSilenceAndPrimesAgain()
    {
        var queue = new AudioBuffer();
        var reader = queue.Start();
        queue.Write(Ramp(1, AudioBuffer.TargetFrames));
        var output = Read(reader, AudioBuffer.TargetFrames + 100);
        var played = (int)queue.Stats.PlayedFrames;
        Assert.InRange(played, AudioBuffer.TargetFrames - 3, AudioBuffer.TargetFrames); // Up to 3 held as neighbours.
        Assert.All(output[(played * 2)..], sample => Assert.Equal(0, sample));
        Assert.Contains(output[..(played * 2)], sample => sample != 0);
        var stats = queue.Stats;
        Assert.Equal(1, stats.Underruns);
        Assert.Equal(AudioBuffer.TargetFrames + 100 - played, stats.SilentFrames);
        Assert.True(stats.Priming);
        Assert.All(Read(reader, 480), sample => Assert.Equal(0, sample));
    }

    private const int FramesPerMs = 48;

    // Also checks that the limit stays at twice the raised target.
    [Fact]
    public void EachShortageRaisesTheTargetByTwentyMillisecondsUpToOneHundredTwenty()
    {
        var queue = new AudioBuffer();
        var reader = queue.Start();
        int[] targetsMs = [40, 60, 80, 100, 120, 120]; // Before each shortage.
        for (var shortage = 1; shortage <= targetsMs.Length; shortage++)
        {
            var target = targetsMs[shortage - 1] * FramesPerMs;
            Assert.Equal(target, queue.Stats.TargetFrames);
            queue.Write(Ramp(1, target - 1 - queue.Stats.QueuedFrames));
            Assert.All(Read(reader, 480), sample => Assert.Equal(0, sample)); // One frame short: priming.
            queue.Write(Ramp(1, 1));
            Assert.Contains(Read(reader, 480), sample => sample != 0);
            Read(reader, target);
            Assert.Equal(shortage, queue.Stats.Underruns);
        }
        Assert.Equal(120 * FramesPerMs, queue.Stats.TargetFrames);
        queue.Write(Ramp(0, (240 * FramesPerMs) + 10));
        Assert.Equal(240 * FramesPerMs, queue.Stats.QueuedFrames);
    }

    [Fact]
    public void FlushStopAndStartKeepTheRaisedTarget()
    {
        var queue = new AudioBuffer();
        var reader = queue.Start();
        queue.Write(Ramp(1, 40 * FramesPerMs));
        Read(reader, (40 * FramesPerMs) + 100);
        queue.Flush();
        Assert.Equal(60 * FramesPerMs, queue.Stats.TargetFrames);
        queue.Stop();
        reader = queue.Start();
        Assert.Equal(60 * FramesPerMs, queue.Stats.TargetFrames);
        queue.Write(Ramp(1, (60 * FramesPerMs) - 1));
        Assert.All(Read(reader, 480), sample => Assert.Equal(0, sample));
        Assert.Equal(1, queue.Stats.Underruns);
    }

    // A stall on the way raises the target again and restarts the 30 s.
    [Fact]
    public void ThirtySecondsWithoutAShortageLowersTheTargetByTenMillisecondsDownToForty()
    {
        var queue = new AudioBuffer();
        var reader = queue.Start();
        queue.Write(Ramp(1, 40 * FramesPerMs));
        Read(reader, (40 * FramesPerMs) + 100);
        Assert.Equal(60 * FramesPerMs, queue.Stats.TargetFrames);
        var slice = Ramp(0, FramesPerMs);
        var buffer = new float[480 * 2];
        for (var ms = 1; ms <= 175_000; ms++)
        {
            // Writes 1 ms slices, stalling 100 ms at 20 s, and reads 10 ms at the same clock.
            if (ms is <= 20_000 or > 20_100)
            {
                queue.Write(slice);
            }

            if (ms % 10 == 0)
            {
                reader.Read(buffer);
            }

            int? expectedMs = ms switch
            {
                <= 20_000 => 60,
                > 20_500 and <= 49_500 => 80,
                > 51_000 and <= 79_500 => 70,
                > 81_000 and <= 109_500 => 60,
                > 111_000 and <= 139_500 => 50,
                > 141_000 => 40,
                _ => null // At a step.
            };
            if (expectedMs is { } value)
            {
                Assert.Equal(value * FramesPerMs, queue.Stats.TargetFrames);
            }

            if (ms == 45_000)
            {
                Assert.InRange(queue.Stats.AverageFrames, 77 * FramesPerMs, 83 * FramesPerMs);
            }
        }
        var stats = queue.Stats;
        Assert.Equal(2, stats.Underruns);
        Assert.Equal(0, stats.DroppedFrames);
        Assert.InRange(stats.AverageFrames, 38 * FramesPerMs, 42 * FramesPerMs);
    }

    [Fact]
    public void MoreThanEightyMillisecondsLosesTheOldestFrames()
    {
        var queue = new AudioBuffer();
        var reader = queue.Start();
        queue.Write(Ramp(0, AudioBuffer.LimitFrames));
        queue.Write(Ramp(AudioBuffer.LimitFrames, 100));
        Assert.Equal(100, queue.Stats.DroppedFrames);
        Assert.Equal(AudioBuffer.LimitFrames, queue.Stats.QueuedFrames);
        Assert.Equal(Expected(100, 1), Read(reader, 1));

        queue.Write(Ramp(0, AudioBuffer.LimitFrames + 7));
        Assert.Equal(100 + AudioBuffer.LimitFrames - 1 + 7, queue.Stats.DroppedFrames);
        Assert.Equal(AudioBuffer.LimitFrames, queue.Stats.QueuedFrames);
        Assert.Equal(AudioBuffer.LimitFrames + 100 + AudioBuffer.LimitFrames + 7, queue.Stats.ReceivedFrames);
    }

    [Fact]
    public void StoppedQueuesKeepNothingAndEarlierReadersOnlyGetSilence()
    {
        var queue = new AudioBuffer();
        queue.Write(Ramp(0, AudioBuffer.TargetFrames));
        Assert.Equal(0, queue.Stats.ReceivedFrames);
        var first = queue.Start();
        queue.Write(Ramp(0, AudioBuffer.TargetFrames));
        queue.Stop();
        var second = queue.Start();
        queue.Write(Ramp(5, AudioBuffer.TargetFrames));
        Assert.All(Read(first, 100), sample => Assert.Equal(0, sample));
        Assert.Equal(AudioBuffer.TargetFrames, queue.Stats.QueuedFrames);
        Assert.Equal(Expected(5, 100), Read(second, 100));

        queue.Flush();
        Assert.Equal(0, queue.Stats.QueuedFrames);
        Assert.True(queue.Stats.Priming);
        queue.Write(Ramp(50, AudioBuffer.TargetFrames));
        Assert.Equal(Expected(50, 10), Read(second, 10));
    }

    [Fact]
    public void GainScalesTheOutputAndZeroKeepsConsuming()
    {
        var queue = new AudioBuffer();
        var reader = queue.Start();
        queue.SetGain(0.25f);
        queue.Write(Ramp(1000, AudioBuffer.TargetFrames));
        Assert.Equal(Expected(1000, 50, 0.25f), Read(reader, 50));
        queue.SetGain(0);
        Assert.All(Read(reader, 50), sample => Assert.Equal(0, sample));
        Assert.InRange(queue.Stats.PlayedFrames, 99, 100); // Slowed slightly below target.
    }

    // Writes 1 ms slices and reads 10 ms from a device clock that is off by ppm.
    [Theory]
    [InlineData(1_000)]
    [InlineData(-1_000)]
    [InlineData(0)]
    public void ReadingFollowsADeviceClockThatDrifts(int ppm)
    {
        var queue = new AudioBuffer();
        var reader = queue.Start();
        double produced = 0;
        var written = 0;
        var buffer = new float[480 * 2];
        for (var ms = 1; ms <= 120_000; ms++)
        {
            produced += 48 * (1 + (ppm / 1e6));
            var frames = (int)produced - written;
            queue.Write(Ramp(0, frames));
            written += frames;
            if (ms % 10 == 0)
            {
                reader.Read(buffer);
            }

            if (ms == 2_000)
            {
                Assert.Equal(0, queue.Stats.Underruns + queue.Stats.DroppedFrames);
            }
        }
        var stats = queue.Stats;
        Assert.Equal(0, stats.Underruns);
        Assert.Equal(0, stats.DroppedFrames);
        Assert.InRange(stats.CorrectionPpm, ppm - 150, ppm + 150);
        Assert.InRange(stats.QueuedFrames, AudioBuffer.TargetFrames / 2, AudioBuffer.LimitFrames - 500);
        Assert.Equal(stats.ReceivedFrames, stats.PlayedFrames + stats.QueuedFrames);
    }

    [Fact]
    public void ReadingDoesNotAllocate()
    {
        var queue = new AudioBuffer();
        var reader = queue.Start();
        var slice = Ramp(0, 480);
        var buffer = new float[480 * 2];
        queue.Write(Ramp(0, AudioBuffer.TargetFrames));
        reader.Read(buffer);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            queue.Write(slice);
            reader.Read(buffer);
        }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public async Task ConcurrentSidesKeepEveryFrameAccountedFor()
    {
        var queue = new AudioBuffer();
        var reader = queue.Start();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var stopping = stop.Token;
        var producer = Task.Run(() =>
        {
            var slice = Ramp(0, 48);
            while (!stopping.IsCancellationRequested)
            {
                queue.Write(slice);
                Thread.SpinWait(500);
            }
        }, TestContext.Current.CancellationToken);
        var consumer = Task.Run(() =>
        {
            var buffer = new float[256 * 2];
            while (!stopping.IsCancellationRequested)
            {
                reader.Read(buffer);
                Thread.SpinWait(500);
            }
        }, TestContext.Current.CancellationToken);
        await Task.WhenAll(producer, consumer);
        var stats = queue.Stats;
        Assert.True(stats.ReceivedFrames > 0 && stats.PlayedFrames > 0);
        Assert.Equal(stats.ReceivedFrames, stats.PlayedFrames + stats.QueuedFrames + stats.DroppedFrames);
    }
}
