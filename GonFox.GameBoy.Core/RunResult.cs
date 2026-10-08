namespace GonFox.GameBoy.Core;

// A long also accommodates a final instruction overshooting an int-sized budget.
public readonly record struct RunResult(long ExecutedTCycles, bool IsStopped);
