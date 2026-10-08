namespace GonFox.GameBoy.Platform.Audio;

using GonFox.GameBoy.Core.Audio;

// The emulation thread's side of the host audio buffer, taking 48 kHz interleaved stereo PCM.
public interface IPcmSink
{
    void Write(ReadOnlySpan<short> samples);
    void Flush();
}

public readonly record struct AudioBufferStats(bool Playing, bool Priming, int QueuedFrames, double AverageFrames,
    double CorrectionPpm, long ReceivedFrames, long PlayedFrames, long Underruns, long SilentFrames, long DroppedFrames, int TargetFrames);

// Queues PCM for the device thread at an adaptive target level, resampling slightly to hold it.
public sealed class AudioBuffer : IPcmSink
{
    public const int TargetFrames = AudioOutput.SampleRate / 25;          // 40 ms.
    public const int LimitFrames = 2 * TargetFrames;
    public const int MaxTargetFrames = AudioOutput.SampleRate * 3 / 25;    // 120 ms.
    public const int TargetStepFrames = AudioOutput.SampleRate / 50;       // 20 ms.
    public const int RecoveryStepFrames = AudioOutput.SampleRate / 100;    // 10 ms.
    public const long RecoveryFrames = AudioOutput.SampleRate * 30L;
    private const int CapacityFrames = 2 * MaxTargetFrames;
    private const double MaxCorrection = 0.002;
    private const double CorrectionGain = 0.01;
    private const double LevelSmoothing = 0.02;
    private readonly Lock gate = new();
    private readonly short[] ring = new short[CapacityFrames * 2];
    private int start;
    private int count;
    private int generation;
    private int target = TargetFrames;
    private bool playing;
    private bool priming = true;
    private double phase;
    private double level;
    private double correction;
    private float gain = 1;
    private float previousLeft;
    private float previousRight;
    private long received;
    private long played;
    private long underruns;
    private long silent;
    private long dropped;
    private long sinceShortage;

    public AudioBufferReader Start()
    {
        lock (gate)
        {
            Clear();
            playing = true;
            return new AudioBufferReader(this, ++generation);
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            Clear();
            playing = false;
            generation++;
        }
    }

    public void Flush()
    {
        lock (gate)
        {
            Clear();
        }
    }

    private void Clear()
    {
        (start, count, priming, phase, correction) = (0, 0, true, 0, 0);
        previousLeft = previousRight = 0;
    }

    public void SetGain(float factor)
    {
        lock (gate)
        {
            gain = factor;
        }
    }

    public AudioBufferStats Stats
    {
        get
        {
            lock (gate)
            {
                return new(playing, priming, count, level, correction * 1e6, received, played, underruns, silent, dropped, target);
            }
        }
    }

    public void Write(ReadOnlySpan<short> samples)
    {
        lock (gate)
        {
            if (!playing)
            {
                return;
            }

            int frames = samples.Length / 2, limit = 2 * target;
            received += frames;
            if (frames > limit)
            {
                dropped += frames - limit;
                samples = samples[^(limit * 2)..];
                frames = limit;
            }
            var excess = count + frames - limit;
            if (excess > 0)
            {
                start = (start + excess) % CapacityFrames;
                count -= excess;
                dropped += excess;
            }
            int end = (start + count) % CapacityFrames, first = Math.Min(frames, CapacityFrames - end);
            samples[..(first * 2)].CopyTo(ring.AsSpan(end * 2));
            samples[(first * 2)..].CopyTo(ring);
            count += frames;
        }
    }

    // Always fills the whole destination, left then right.
    internal int Read(int readerGeneration, Span<float> destination)
    {
        lock (gate)
        {
            int frames = destination.Length / 2, written = 0;
            if (readerGeneration != generation || !playing)
            {
                destination.Clear();
                return destination.Length;
            }
            if (priming && count >= target)
            {
                priming = false;
                level = count;
                (previousLeft, previousRight) = (Sample(0, 0), Sample(0, 1));
            }
            if (!priming)
            {
                level += LevelSmoothing * (count - level);
                correction = Math.Clamp(CorrectionGain * (level - target) / target, -MaxCorrection, MaxCorrection);
                var step = 1 + correction;
                for (; written < frames && count >= 3; written++)
                {
                    var t = (float)phase;
                    destination[written * 2] = Interpolate(previousLeft, Sample(0, 0), Sample(1, 0), Sample(2, 0), t);
                    destination[(written * 2) + 1] = Interpolate(previousRight, Sample(0, 1), Sample(1, 1), Sample(2, 1), t);
                    for (phase += step; phase >= 1; phase--)
                    {
                        (previousLeft, previousRight) = (Sample(0, 0), Sample(0, 1));
                        start = (start + 1) % CapacityFrames;
                        count--;
                        played++;
                    }
                }
                if (written < frames)
                {
                    underruns++;
                    silent += frames - written;
                    priming = true;
                    target = Math.Min(MaxTargetFrames, target + TargetStepFrames);
                    sinceShortage = 0;
                }
                else if ((sinceShortage += written) >= RecoveryFrames && target > TargetFrames)
                {
                    target -= RecoveryStepFrames;
                    sinceShortage = 0;
                }
            }
            destination[(written * 2)..].Clear();
            return destination.Length;
        }
    }

    private float Sample(int frame, int side) => ring[(((start + frame) % CapacityFrames) * 2) + side] / 32768f;

    // Catmull-Rom through four neighbouring samples; t = 0 returns x0 exactly.
    private float Interpolate(float previous, float x0, float x1, float x2, float t)
    {
        var y = x0 + (0.5f * t * (x1 - previous + (t * ((2 * previous) - (5 * x0) + (4 * x1) - x2 + (t * ((3 * (x0 - x1)) + x2 - previous))))));
        return Math.Clamp(y * gain, -1f, 1f);
    }
}

// Reads one playback's PCM; a reader from an earlier playback gets only silence.
public sealed class AudioBufferReader
{
    private readonly AudioBuffer buffer;
    private readonly int generation;

    internal AudioBufferReader(AudioBuffer buffer, int generation)
    {
        this.buffer = buffer;
        this.generation = generation;
    }

    public int Read(Span<float> destination) => buffer.Read(generation, destination);
}
