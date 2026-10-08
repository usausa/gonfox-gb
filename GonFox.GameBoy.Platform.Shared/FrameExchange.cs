namespace GonFox.GameBoy.Platform;

using System.Diagnostics.CodeAnalysis;

using GonFox.GameBoy.Core.Video;

// Holds a completed frame's BGRA32 pixels and description; Width 0 means no frame yet.
public sealed class FrameSlot
{
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "The slot is filled and drawn in place, without copies.")]
    public byte[] Pixels { get; } = new byte[VideoOutput.BufferSize];
    public VideoFrameInfo Info { get; set; }
}

// Passes frames from the emulation thread to one display thread through a lock-free triple buffer.
public sealed class FrameExchange
{
    private const int Fresh = 4;
    private const int Index = 3;
    private readonly FrameSlot[] slots = [new(), new(), new()];
    private int shared = 1; // Index, plus Fresh while untaken.
    private int back;
    private int front = 2;

    // Writer: fill Back, then Publish it.
    public FrameSlot Back => slots[back];
    public void Publish() => back = Interlocked.Exchange(ref shared, back | Fresh) & Index;

    // Reader: takes the newest image, or null before the first one or when it was already shown.
    public FrameSlot? TryTake(ulong shownSequence)
    {
        if ((Volatile.Read(ref shared) & Fresh) != 0)
        {
            front = Interlocked.Exchange(ref shared, front) & Index;
        }

        var slot = slots[front];
        return slot.Info.Width == 0 || slot.Info.Sequence == shownSequence ? null : slot;
    }
}
