namespace GonFox.GameBoy.Core.Cartridge;

// The MBC3 clock registers as a game reads them; DH holds day bit 8, halt and day carry.
public readonly record struct RtcRegisters(byte Seconds, byte Minutes, byte Hours, byte DayLow, byte DayHigh);

// The running clock and the copy a game last latched.
public readonly record struct RtcSnapshot(RtcRegisters Current, RtcRegisters Latched);

// An MBC3 clock that counts emulated time only; a host adds the time that passed between sessions.
public interface IRealTimeClockCartridge : IBatteryBackedCartridge
{
    RtcSnapshot ExportClock();

    // Imports only the bits each register has and restarts the current second.
    void ImportClock(RtcSnapshot clock);

    // Adds whole seconds as if the clock had kept running; a halted clock does not move.
    void AdvanceClock(long seconds);
}
