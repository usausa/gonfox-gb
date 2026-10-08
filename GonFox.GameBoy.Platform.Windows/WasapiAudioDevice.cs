namespace GonFox.GameBoy.Platform.Windows;

using System.Runtime.InteropServices;

using GonFox.GameBoy.Core.Audio;
using GonFox.GameBoy.Platform.Audio;

using NAudio.CoreAudioApi;
using NAudio.Wave;

// Plays through WASAPI shared mode on the default output, following device switches.
public sealed class WasapiAudioDevice : IAudioDevice
{
    private const int LatencyMilliseconds = 30;
    private readonly ReaderProvider provider = new();
    private WasapiPlayer? player;

    public event EventHandler<AudioDeviceFailedEventArgs>? Failed;

    public AudioStreamInfo? Info { get; private set; }

    // Opens the player on the UI thread, so PlaybackStopped arrives there too.
    public async Task OpenAsync()
    {
        player = await new WasapiPlayerBuilder()
            .WithDefaultDeviceStreamRouting()
            .WithLatency(LatencyMilliseconds)
            .WithCategory(AudioStreamCategory.GameMedia)
            .WithMmcssThreadPriority("Audio")
            .BuildAsync().ConfigureAwait(true); // Stays on the UI thread.
        player.Init(provider);
        Info = new(player.OutputWaveFormat.SampleRate, TimeSpan.FromMilliseconds(player.LatencyMilliseconds));
        player.PlaybackStopped += (_, e) =>
        {
            if (e.Exception is { } exception)
            {
                Failed?.Invoke(this, new AudioDeviceFailedEventArgs(exception));
            }
        };
    }

    public void StartPlayback(AudioBufferReader reader)
    {
        provider.Reader = reader;
        player!.Play();
    }

    public void StopPlayback() => player?.Stop();

    public void Dispose()
    {
        player?.Dispose();
        player = null;
    }

    // NAudio asks for bytes; the queue writes its floats straight into the device buffer.
    private sealed class ReaderProvider : IWaveProvider
    {
        private volatile AudioBufferReader? reader;

        public AudioBufferReader? Reader { get => reader; set => reader = value; }

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(AudioOutput.SampleRate, AudioOutput.ChannelCount);

        public int Read(Span<byte> buffer)
        {
            var samples = MemoryMarshal.Cast<byte, float>(buffer);
            if (reader is { } current)
            {
                current.Read(samples);
            }
            else
            {
                samples.Clear();
            }

            return buffer.Length; // 0 would end the stream.
        }
    }
}
