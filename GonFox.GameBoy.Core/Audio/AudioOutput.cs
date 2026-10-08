namespace GonFox.GameBoy.Core.Audio;

// Queues 48 kHz stereo 16-bit PCM frames; a full queue drops its oldest frames.
public sealed class AudioOutput
{
    public const int SampleRate = 48_000;
    public const int ChannelCount = 2; // Left, then right.
    public const int CapacityFrames = 2_048;
    private readonly short[] ring = new short[CapacityFrames * ChannelCount];
    private int start; // In frames.

    public int QueuedFrameCount { get; private set; }
    public long DroppedFrameCount { get; private set; }

    internal sealed record State(short[] Queued, long Dropped);

    internal State CaptureState()
    {
        var queued = new short[QueuedFrameCount * ChannelCount];
        Peek(queued);
        return new(queued, DroppedFrameCount);
    }

    internal static bool IsValid(State? s) => s?.Queued is { Length: <= CapacityFrames * ChannelCount } && s.Queued.Length % ChannelCount == 0 && s.Dropped >= 0;

    internal void RestoreState(State s)
    {
        s.Queued.CopyTo(ring, 0);
        (start, QueuedFrameCount, DroppedFrameCount) = (0, s.Queued.Length / ChannelCount, s.Dropped);
    }

    // Moves up to destination.Length / ChannelCount queued frames out; returns how many.
    public int ReadFrames(Span<short> destination)
    {
        if (destination.Length % ChannelCount != 0)
        {
            throw new ArgumentException("Provide room for whole stereo frames (an even number of samples).", nameof(destination));
        }

        var frames = Peek(destination);
        start = (start + frames) % CapacityFrames;
        QueuedFrameCount -= frames;
        return frames;
    }

    private int Peek(Span<short> destination)
    {
        var frames = Math.Min(QueuedFrameCount, destination.Length / ChannelCount);
        var first = Math.Min(frames, CapacityFrames - start);
        ring.AsSpan(start * ChannelCount, first * ChannelCount).CopyTo(destination);
        ring.AsSpan(0, (frames - first) * ChannelCount).CopyTo(destination[(first * ChannelCount)..]);
        return frames;
    }

    internal void Write(short left, short right)
    {
        if (QueuedFrameCount == CapacityFrames)
        {
            start = (start + 1) % CapacityFrames;
            QueuedFrameCount--;
            DroppedFrameCount++;
        }
        var index = ((start + QueuedFrameCount) % CapacityFrames) * ChannelCount;
        ring[index] = left;
        ring[index + 1] = right;
        QueuedFrameCount++;
    }

    internal void Clear() => (start, QueuedFrameCount, DroppedFrameCount) = (0, 0, 0);
}
