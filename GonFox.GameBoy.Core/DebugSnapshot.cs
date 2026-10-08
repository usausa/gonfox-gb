namespace GonFox.GameBoy.Core;

public readonly record struct DebugSnapshot(
    ushort AF, ushort BC, ushort DE, ushort HL,
    ushort SP, ushort PC, ulong TotalTCycles)
{
    public bool InterruptMasterEnable { get; init; }
    public bool InterruptEnablePending { get; init; }
    public bool IsHalted { get; init; }
    public bool IsStopped { get; init; }
    public ushort DividerCounter { get; init; }
    public byte TimerCounter { get; init; }
    public byte TimerModulo { get; init; }
    public byte TimerControl { get; init; }
    public byte InterruptFlags { get; init; }
    public byte InterruptEnable { get; init; }
    public byte Ly { get; init; }
    public byte PpuMode { get; init; }
    public byte LcdControl { get; init; }
    public byte ScrollX { get; init; }
    public byte ScrollY { get; init; }
    public int PpuDot { get; init; }
    public bool DmaActive { get; init; }
    public int RomBank0 { get; init; }
    public int RomBank1 { get; init; }
    public int RamBank { get; init; }
    public bool RamEnabled { get; init; }
    public byte BankingMode { get; init; }

    public byte A => (byte)(AF >> 8);
    public byte F => (byte)AF;
    public byte B => (byte)(BC >> 8);
    public byte C => (byte)BC;
    public byte D => (byte)(DE >> 8);
    public byte E => (byte)DE;
    public byte H => (byte)(HL >> 8);
    public byte L => (byte)HL;
}
