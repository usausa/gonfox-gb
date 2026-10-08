namespace GonFox.GameBoy.Core.Devices;

using System.Runtime.CompilerServices;

using GonFox.GameBoy.Core.Memory;
using GonFox.GameBoy.Core.Video;

// OAM DMA: one byte per M-cycle, after the write cycle and one more.
internal sealed class OamDma(MemoryBus bus, Ppu ppu)
{
    private int phase;
    private int pendingCycles;
    private int index;
    private byte sourcePage;

    // A CPU write on the source bus, taken by this M-cycle's transfer (never saved).
    private bool cpuWritePending;
    private byte cpuWriteValue;
    internal byte Register { get; private set; } = 0xFF;
    internal bool Active { get; private set; }
    internal bool Quiet => !Active && pendingCycles == 0;

    // Set while the CPU is halted: the transfer waits with it.
    internal bool Held { get; set; }
    internal bool UsesVideoBus => sourcePage is >= 0x80 and <= 0x9F;

    internal sealed record State(int Phase, int PendingCycles, int Index, byte SourcePage, byte Register, bool Active);

    internal State CaptureState() => new(phase, pendingCycles, index, sourcePage, Register, Active);

    internal static void ValidateState(State? state) => StateValidation.Require(state?.Phase is >= 0 and < 4 &&
        state.PendingCycles is >= 0 and <= 2 &&
        state.Index is >= 0 and <= 160 && (!state.Active || state.Index < 160), "DMA");

    internal void RestoreState(State state)
    {
        phase = state.Phase;
        pendingCycles = state.PendingCycles;
        index = state.Index;
        sourcePage = state.SourcePage;
        Register = state.Register;
        Active = state.Active;
    }

    // Starts a transfer; a running one continues until then, but from the new page.
    internal void Start(byte value)
    {
        Register = sourcePage = value;
        pendingCycles = 2;
    }

    // Whether address is on the transfer's source bus: 8000-9FFF, or the cartridge/WRAM bus.
    internal bool SharesBus(ushort address) => (address is >= 0x8000 and <= 0x9FFF) == UsesVideoBus;

    // A CPU read on the source bus sees the byte the transfer reads in this M-cycle.
    internal byte ConflictingRead() => bus.ReadDmaByte((ushort)((sourcePage << 8) + index));

    // A CPU write on the source bus replaces the byte the transfer copies (ANDed with it from WRAM).
    internal void ConflictingWrite(byte value)
    {
        cpuWritePending = true;
        cpuWriteValue = value;
    }

    internal void Tick()
    {
        if (++phase != 4)
        {
            return;
        }

        phase = 0;
        if (Active || pendingCycles != 0)
        {
            EndMachineCycle();
        }
    }

    // Copies one byte at the end of an M-cycle; kept out of the inlined per-T-cycle Tick.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EndMachineCycle()
    {
        if (Held)
        {
            return;
        }

        if (Active)
        {
            var value = bus.ReadDmaByte((ushort)((sourcePage << 8) + index));
            if (cpuWritePending)
            {
                value = sourcePage >= 0xC0 ? (byte)(value & cpuWriteValue) : cpuWriteValue;
                cpuWritePending = false;
            }
            ppu.WriteOamDma(index, value);
            if (++index == 160)
            {
                Active = false;
                ppu.SetDmaActive(false);
            }
        }
        if (pendingCycles > 0 && --pendingCycles == 0)
        {
            index = 0;
            Active = true;
            ppu.SetDmaActive(true);
        }
    }

    internal void Reset()
    {
        Register = 0xFF;
        Active = Held = cpuWritePending = false;
        phase = pendingCycles = index = 0;
        sourcePage = 0;
        ppu.SetDmaActive(false);
    }
}
