namespace GonFox.GameBoy.Platform.Audio;

using System.Diagnostics;

// An audio output that pulls 48 kHz stereo floats through a buffer reader on its own thread.
public interface IAudioDevice : IDisposable
{
    event EventHandler<AudioDeviceFailedEventArgs>? Failed;

    AudioStreamInfo? Info { get; }

    Task OpenAsync();

    void StartPlayback(AudioBufferReader reader);

    void StopPlayback();
}

// An open device's sample rate, buffered latency and whether it runs on a low-latency path.
public sealed record AudioStreamInfo(int SampleRate, TimeSpan Latency, bool LowLatency = false);

// Why a device stopped by itself.
public sealed class AudioDeviceFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}

// Plays the device while the emulation runs, silent without one; UI thread only.
public sealed class AudioPlayback(Func<IAudioDevice> create) : IDisposable
{
    private IAudioDevice? device;
    private bool opening;
    private bool playing;
    private bool emulationRunning;
    private bool disposed;
    public AudioBuffer Buffer { get; } = new();
    public string? Error { get; private set; }
    public AudioStreamInfo? DeviceInfo => device?.Info;
    public TimeSpan Latency => playing && device?.Info is { } info ? info.Latency : TimeSpan.Zero;

    // Sets the gain from a 0..100 volume on a squared curve; mute keeps reading silently.
    public void SetVolume(int percent, bool muted)
    {
        var level = Math.Clamp(percent, 0, 100) / 100f;
        Buffer.SetGain(muted ? 0 : level * level);
    }

    // Opens the device ahead of the first run, so that starting to play takes no device setup.
    public void Prepare()
    {
        if (!disposed && Error is null && device is null && !opening)
        {
            _ = OpenAsync();
        }
    }

    public void Update(bool run)
    {
        emulationRunning = run;
        if (disposed || Error is not null)
        {
            return;
        }

        if (!run)
        {
            if (!playing)
            {
                return;
            }

            playing = false;
            Buffer.Stop();
#pragma warning disable CA1031
            try
            {
                device!.StopPlayback();
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
#pragma warning restore CA1031
            return;
        }
        if (playing)
        {
            return;
        }

        if (device is null)
        {
            Prepare(); // Plays once open.
            return;
        }
#pragma warning disable CA1031
        try
        {
            device.StartPlayback(Buffer.Start());
            playing = true;
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
#pragma warning restore CA1031
    }

    private async Task OpenAsync()
    {
        opening = true;
        var opened = create();
#pragma warning disable CA1031
        try
        {
            await opened.OpenAsync().ConfigureAwait(true); // Back on the UI thread.
        }
        catch (Exception exception)
        {
            opened.Dispose();
            opening = false;
            if (!disposed)
            {
                Fail(exception);
            }

            return;
        }
#pragma warning restore CA1031
        opening = false;
        if (disposed)
        {
            opened.Dispose();
            return;
        }

        opened.Failed += (_, e) =>
        {
            if (ReferenceEquals(opened, device))
            {
                Fail(e.Exception);
            }
        };
        device = opened;
        if (emulationRunning)
        {
            Update(true);
        }
    }

    private void Fail(Exception exception)
    {
        Error = exception.Message;
        playing = false;
        Buffer.Stop();
        var broken = device;
        device = null;
#pragma warning disable CA1031
        try
        {
            broken?.Dispose();
        }
        catch (Exception release)
        {
            // A broken device may fail to release; only the first error is reported.
            Debug.WriteLine($"Audio device release failed: {release.Message}");
        }
#pragma warning restore CA1031
    }

    // Opens the device again at the next update.
    public void Retry() => Error = null;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        playing = false;
        Buffer.Stop();
        device?.Dispose();
        device = null;
    }
}
