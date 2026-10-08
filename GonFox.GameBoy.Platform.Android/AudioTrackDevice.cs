namespace GonFox.GameBoy.Platform.Android;

using global::Android.Media;

using GonFox.GameBoy.Core.Audio;
using GonFox.GameBoy.Platform.Audio;

// Plays through a low-latency AudioTrack, fed by a thread whose blocking writes pace the reads.
public sealed class AudioTrackDevice : IAudioDevice
{
    private const int PieceFrames = 240;
    private readonly AutoResetEvent wake = new(false);
    private AudioTrack? track;
    private Thread? thread;
    private SynchronizationContext? opener;
    private volatile AudioBufferReader? source;
    private volatile bool closed;

    public event EventHandler<AudioDeviceFailedEventArgs>? Failed;

    public AudioStreamInfo? Info { get; private set; }

    public Task OpenAsync()
    {
        opener = SynchronizationContext.Current;
        var minimum = AudioTrack.GetMinBufferSize(AudioOutput.SampleRate, ChannelOut.Stereo, Encoding.PcmFloat);
        if (minimum <= 0)
        {
            throw new InvalidOperationException($"AudioTrack does not take 48 kHz float stereo ({minimum}).");
        }

        var bytes = Math.Max(minimum, PieceFrames * 2 * AudioOutput.ChannelCount * sizeof(float));
        using var attributesBuilder = new AudioAttributes.Builder();
        using var attributes = attributesBuilder.SetUsage(AudioUsageKind.Game)!.SetContentType(AudioContentType.Music)!.Build()!;
        using var formatBuilder = new AudioFormat.Builder();
        using var format = formatBuilder.SetSampleRate(AudioOutput.SampleRate)!.SetEncoding(Encoding.PcmFloat)!
            .SetChannelMask(ChannelOut.Stereo).Build()!;
        using var trackBuilder = new AudioTrack.Builder();
        track = trackBuilder
            .SetAudioAttributes(attributes)
            .SetAudioFormat(format)
            .SetBufferSizeInBytes(bytes)
            .SetTransferMode(AudioTrackMode.Stream)
            .SetPerformanceMode(AudioTrackPerformanceMode.LowLatency)
            .Build();
        var frames = track.BufferSizeInFrames;
        Info = new(AudioOutput.SampleRate, TimeSpan.FromSeconds(frames / (double)AudioOutput.SampleRate),
            track.PerformanceMode == AudioTrackPerformanceMode.LowLatency);
        thread = new Thread(Pump) { IsBackground = true, Name = "AudioTrack", Priority = ThreadPriority.AboveNormal };
        thread.Start();
        return Task.CompletedTask;
    }

    // Flushes any piece written after the last stop, then plays from the reader.
    public void StartPlayback(AudioBufferReader reader)
    {
        track!.Flush();
        source = reader;
        track.Play();
        wake.Set();
    }

    public void StopPlayback()
    {
        source = null;
        if (track is null)
        {
            return;
        }

        track.Pause();
        track.Flush();
    }

    private void Pump()
    {
        var piece = new float[PieceFrames * AudioOutput.ChannelCount];
        try
        {
            while (!closed)
            {
                if (source is not { } reader)
                {
                    wake.WaitOne();
                    continue;
                }

                reader.Read(piece);
                var written = track!.Write(piece, 0, piece.Length, WriteMode.Blocking);
                if (written < 0)
                {
                    throw new InvalidOperationException($"AudioTrack.write: {written}");
                }
            }
        }
        catch (Exception exception) when (!closed)
        {
            source = null;
            var failed = new AudioDeviceFailedEventArgs(exception);
            if (opener is { } context)
            {
                context.Post(_ => Failed?.Invoke(this, failed), null);
            }
            else
            {
                Failed?.Invoke(this, failed);
            }
        }
    }

    public void Dispose()
    {
        closed = true;
        source = null;
        wake.Set();
        track?.Pause();
        track?.Flush();
        thread?.Join();
        track?.Release();
        track?.Dispose();
        track = null;
        wake.Dispose();
    }
}
