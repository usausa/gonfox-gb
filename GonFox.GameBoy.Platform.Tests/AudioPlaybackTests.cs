namespace GonFox.GameBoy.Platform;

// A device stand-in: the test pulls what a real device thread would.
internal sealed class FakeAudioDevice(Exception? openFailure = null) : IAudioDevice
{
    internal AudioBufferReader? Reader { get; private set; }

    internal Func<IAudioDevice> Factory => () => this;
    internal bool Opened { get; private set; }
    internal bool Playing { get; private set; }
    internal bool Disposed { get; private set; }
    internal int Plays { get; private set; }
    internal int Stops { get; private set; }
    public AudioStreamInfo? Info { get; private set; }
    public event EventHandler<AudioDeviceFailedEventArgs>? Failed;

    public Task OpenAsync()
    {
        if (openFailure is not null)
        {
            return Task.FromException(openFailure);
        }

        Opened = true;
        Info = new(48_000, TimeSpan.FromMilliseconds(20));
        return Task.CompletedTask;
    }

    public void StartPlayback(AudioBufferReader reader)
    {
        Reader = reader;
        Playing = true;
        Plays++;
    }

    public void StopPlayback()
    {
        Playing = false;
        Stops++;
    }

    public void Dispose()
    {
        Playing = false;
        Disposed = true;
    }

    internal void Fail(Exception exception) => Failed?.Invoke(this, new AudioDeviceFailedEventArgs(exception));

    internal float[] Pull(int frames)
    {
        var buffer = new float[frames * 2];
        Reader!.Read(buffer);
        return buffer;
    }
}

public sealed class AudioPlaybackTests
{
    private static short[] Tone(int frames) => Enumerable.Range(0, frames).SelectMany(_ => new short[] { 16384, -16384 }).ToArray();

    [Fact]
    public void TheDevicePlaysOnlyWhileTheEmulationRuns()
    {
        using var device = new FakeAudioDevice();
        using var playback = new AudioPlayback(device.Factory);
        playback.Update(false);
        Assert.False(device.Opened);
        playback.Prepare();
        Assert.True(device.Opened);
        Assert.False(device.Playing);
        playback.Update(true);
        Assert.True(device.Playing);
        Assert.True(playback.Buffer.Stats.Playing);
        playback.Buffer.Write(Tone(AudioBuffer.TargetFrames));
        Assert.Equal(0.5f, device.Pull(1)[0]);

        playback.Update(false);
        Assert.False(device.Playing);
        Assert.False(playback.Buffer.Stats.Playing);
        Assert.Equal(0, playback.Buffer.Stats.QueuedFrames);
        Assert.All(device.Pull(10), sample => Assert.Equal(0, sample)); // Stale reader.
        playback.Update(true);
        playback.Update(true);
        Assert.Equal(2, device.Plays);
        Assert.Equal(1, device.Stops);
        Assert.Equal(device.Info, playback.DeviceInfo);
        Assert.Null(playback.Error);
    }

    [Fact]
    public void PlaybackStartsAsSoonAsTheDeviceOpens()
    {
        using var device = new FakeAudioDevice();
        using var playback = new AudioPlayback(device.Factory);
        playback.Update(true);
        Assert.True(device.Opened && device.Playing);
        Assert.Equal(1, device.Plays);
    }

    [Fact]
    public void WithoutADeviceTheEmulationGoesOnSilentlyUntilRetry()
    {
        var created = 0;
        FakeAudioDevice? good = null;
        using var playback = new AudioPlayback(() => ++created == 1
            ? new FakeAudioDevice(new InvalidOperationException("no device"))
            : good = new FakeAudioDevice());
        playback.Update(true);
        Assert.Equal("no device", playback.Error);
        playback.Buffer.Write(Tone(100));
        Assert.Equal(0, playback.Buffer.Stats.ReceivedFrames);
        playback.Update(true);
        Assert.Equal(1, created);
        playback.Retry();
        playback.Update(true);
        playback.Update(true);
        Assert.Null(playback.Error);
        Assert.True(good!.Playing);
    }

    [Fact]
    public void ADeviceFailureStopsPlaybackAndSilencesItsCallbacks()
    {
        using var device = new FakeAudioDevice();
        using var playback = new AudioPlayback(device.Factory);
        playback.Update(true);
        playback.Update(true);
        playback.Buffer.Write(Tone(AudioBuffer.TargetFrames));
        device.Fail(new IOException("unplugged"));
        Assert.Equal("unplugged", playback.Error);
        Assert.True(device.Disposed);
        Assert.False(playback.Buffer.Stats.Playing);
        Assert.All(device.Pull(10), sample => Assert.Equal(0, sample));
        playback.Update(true);
        Assert.Equal(1, device.Plays);
    }

    [Fact]
    public void VolumeFollowsASquaredCurveAndMuteSilences()
    {
        using var device = new FakeAudioDevice();
        using var playback = new AudioPlayback(device.Factory);
        playback.Update(true);
        playback.Update(true);
        playback.Buffer.Write(Tone(AudioBuffer.TargetFrames));
        playback.SetVolume(50, muted: false);
        Assert.Equal(0.125f, device.Pull(1)[0]); // 0.5 x 0.5^2
        playback.SetVolume(100, muted: true);
        Assert.Equal(0f, device.Pull(1)[0]);
        playback.SetVolume(150, muted: false);
        Assert.Equal(0.5f, device.Pull(1)[0]);
    }

    [Fact]
    public void DisposeReleasesTheDeviceAndIgnoresLaterUpdates()
    {
        using var device = new FakeAudioDevice();
        var playback = new AudioPlayback(device.Factory);
        playback.Update(true);
        playback.Update(true);
        playback.Dispose();
        Assert.True(device.Disposed);
        playback.Update(true);
        Assert.Equal(1, device.Plays);
        Assert.False(playback.Buffer.Stats.Playing);
    }
}
