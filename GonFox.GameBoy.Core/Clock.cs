namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Video;

using Timer = GonFox.GameBoy.Core.Devices.Timer;

internal sealed class Clock
{
    internal const int CyclesPerSecond = 4_194_304;
    internal ulong TotalTCycles { get; private set; }
    private Timer? timer;
    private Ppu? ppu;
    private OamDma? dma;
    internal void ConnectPpu(Ppu target) => ppu = target;
    internal void ConnectDma(OamDma target) => dma = target;
    internal void ConnectTimer(Timer target) => timer = target;
    internal void ResetDivider() => timer?.ResetDivider();

    // Advances one M-cycle of an instruction, in a single step when no device acts in it.
    internal void AdvanceMachineCycle()
    {
        if (dma?.Quiet != false && (timer?.QuietTCycles ?? int.MaxValue) >= 4 && (ppu?.QuietTCycles ?? int.MaxValue) >= 4)
        {
            TotalTCycles += 4;
            timer?.Skip(4);
            ppu?.Skip(4);
            return;
        }
        AdvanceTCycles(4);
    }

    // Ticks the devices T-cycle by T-cycle, for steps that act within an M-cycle.
    internal void AdvanceTCycles(int count)
    {
        for (var cycle = 0; cycle < count; cycle++)
        {
            TotalTCycles++;
            timer?.Tick();
            dma?.Tick();
            ppu?.Tick();
        }
    }

    // The whole M-cycles ahead in which no device acts, which a halted CPU may pass at once.
    internal int QuietMachineCycles() => Math.Min(timer?.QuietTCycles ?? int.MaxValue, ppu?.QuietTCycles ?? int.MaxValue) >> 2;

    internal void SkipMachineCycles(int count)
    {
        var tCycles = count << 2;
        TotalTCycles += (ulong)tCycles;
        timer?.Skip(tCycles);
        ppu?.Skip(tCycles); // Whole M-cycles keep the DMA phase.
    }

    internal void Reset() => TotalTCycles = 0;
    internal void RestoreState(ulong totalTCycles) => TotalTCycles = totalTCycles;
}
