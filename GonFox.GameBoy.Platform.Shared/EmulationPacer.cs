namespace GonFox.GameBoy.Platform;

using GonFox.GameBoy.Core;

internal sealed class EmulationPacer
{
    internal double PendingTCycles { get; private set; }
    internal double DroppedSeconds { get; private set; }
    internal int NextBudget => PendingTCycles < 1 ? 0 : (int)Math.Min(4096, Math.Floor(PendingTCycles));

    internal void Accrue(double seconds)
    {
        var requested = PendingTCycles + (seconds * GameBoySystem.TCyclesPerSecond);
        var limit = GameBoySystem.TCyclesPerSecond * 0.1;
        if (requested > limit)
        {
            DroppedSeconds += (requested - limit) / GameBoySystem.TCyclesPerSecond;
        }

        PendingTCycles = Math.Min(requested, limit);
    }

    internal void Consume(long cycles) => PendingTCycles -= cycles;

    internal void Reset()
    {
        PendingTCycles = 0;
        DroppedSeconds = 0;
    }
}

internal sealed class FrameRateCounter
{
    private ulong baseline;
    private double elapsed;
    internal double Fps { get; private set; }

    internal void Reset(ulong completedFrames)
    {
        baseline = completedFrames;
        elapsed = Fps = 0;
    }

    internal void Advance(ulong completedFrames, double seconds)
    {
        elapsed += seconds;
        if (elapsed < 1)
        {
            return;
        }

        Fps = (completedFrames - baseline) / elapsed;
        baseline = completedFrames;
        elapsed = 0;
    }
}
