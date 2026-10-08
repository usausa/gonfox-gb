namespace Example.GameBoy.WpfHost;

[Flags]
internal enum StatusLines
{
    None = 0,
    Title = 1,
    Fps = 2,
    State = 4,
    Registers = 8,
    Video = 16,
    Device = 32,
    Snapshot = 64
}

// Formats each status line only when its inputs change, and the debugging lines only while shown.
internal sealed class StatusText
{
    private EmulationStatus? shownStatus;
    private bool showingDetails;
    private double shownFps;
    private (ushort, ushort, ushort, ushort, ushort, ushort) shownRegisters;
    private (byte, byte, int, byte, byte, bool, bool, bool, bool) shownVideo;
    private (ushort, byte, byte, byte, byte, byte, int, int, int, bool, byte, bool) shownDevice;
    private (ulong, double, int) shownSnapshot;

    public string Title { get; private set; } = string.Empty;
    public string Fps { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;
    public string Registers { get; private set; } = string.Empty;
    public string Video { get; private set; } = string.Empty;
    public string Device { get; private set; } = string.Empty;
    public string Snapshot { get; private set; } = string.Empty;

    public StatusLines Update(EmulationStatus status, bool details = true)
    {
        var opened = details && !showingDetails; // Reopened: lines may be stale.
        showingDetails = details;
        if (ReferenceEquals(status, shownStatus) && !opened)
        {
            return StatusLines.None; // New record on each change.
        }

        var first = shownStatus is null;
        shownStatus = status;
        var changed = StatusLines.None;
        var title = status.RomLoaded ? status.Title : "No ROM";
        if (first || title != Title)
        {
            Title = title;
            changed |= StatusLines.Title;
        }
        if (first || !status.Fps.Equals(shownFps))
        {
            shownFps = status.Fps;
            Fps = $"FPS {status.Fps:F1}";
            changed |= StatusLines.Fps;
        }
        var state = (!status.RomLoaded ? "No ROM" : !status.Running ? "Paused" : status.Stopped ? "STOP" : "Running") +
            (status.Faulted ? " · CPU locked" : string.Empty) +
            (status.Rumble ? " · Rumble" : string.Empty) +
            (status.BootRomMapped ? " · Boot ROM" : string.Empty);
        if (first || state != State)
        {
            State = state;
            changed |= StatusLines.State;
        }
        if (!details)
        {
            return changed;
        }

        first |= opened;
        var r = status.Registers;
        var registers = (r.AF, r.BC, r.DE, r.HL, r.SP, r.PC);
        if (first || registers != shownRegisters)
        {
            shownRegisters = registers;
            changed |= StatusLines.Registers;
            Registers = $"AF {r.AF:X4} BC {r.BC:X4} DE {r.DE:X4} HL {r.HL:X4} SP {r.SP:X4} PC {r.PC:X4}  ZNHC {Convert.ToString(r.F >> 4, 2).PadLeft(4, '0')}";
        }
        var video = (r.Ly, r.PpuMode, r.PpuDot, r.ScrollX, r.ScrollY, r.InterruptMasterEnable, r.InterruptEnablePending, r.IsHalted, r.IsStopped);
        if (first || video != shownVideo)
        {
            shownVideo = video;
            changed |= StatusLines.Video;
            Video = $"LY {r.Ly,3} Mode {r.PpuMode} Dot {r.PpuDot,3} SCX {r.ScrollX,3} SCY {r.ScrollY,3} IME {(r.InterruptMasterEnable ? 1 : 0)} EI {(r.InterruptEnablePending ? 1 : 0)} HALT {(r.IsHalted ? 1 : 0)} STOP {(r.IsStopped ? 1 : 0)}";
        }
        var device = (r.DividerCounter, r.TimerCounter, r.TimerModulo, r.TimerControl, r.InterruptFlags, r.InterruptEnable,
            r.RomBank0, r.RomBank1, r.RamBank, r.RamEnabled, r.BankingMode, r.DmaActive);
        if (first || device != shownDevice)
        {
            shownDevice = device;
            changed |= StatusLines.Device;
            Device = $"DIV {r.DividerCounter:X4} TIMA {r.TimerCounter:X2} TMA {r.TimerModulo:X2} TAC {r.TimerControl:X2} IF {r.InterruptFlags:X2} IE {r.InterruptEnable:X2}  ROM {r.RomBank0}/{r.RomBank1} RAM {r.RamBank}:{(r.RamEnabled ? "ON" : "OFF")} MBC mode {r.BankingMode} DMA {(r.DmaActive ? 1 : 0)}";
        }
        var snapshot = (r.TotalTCycles, status.DroppedSeconds, status.RewindSeconds);
        if (first || snapshot != shownSnapshot)
        {
            shownSnapshot = snapshot;
            changed |= StatusLines.Snapshot;
            Snapshot = $"T={r.TotalTCycles:N0}  Delay {status.DroppedSeconds * 1000:F0} ms  Rewind {status.RewindSeconds} s";
        }
        return changed;
    }
}
