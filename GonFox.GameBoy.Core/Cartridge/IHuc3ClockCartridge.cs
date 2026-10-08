namespace GonFox.GameBoy.Core.Cartridge;

using System.Diagnostics.CodeAnalysis;

// The HuC3 clock MCU's 256 nibbles and the whole seconds counted into the current minute.
public sealed record Huc3ClockSnapshot(
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "A copy of the clock memory, exported and imported as a whole.")] byte[] Memory,
    int Seconds);

// A HuC3 clock that counts emulated time only; a host adds the time that passed between sessions.
public interface IHuc3ClockCartridge : IBatteryBackedCartridge
{
    Huc3ClockSnapshot ExportClock();

    // Imports the low nibble of each byte; the current minute goes on from Seconds.
    void ImportClock(Huc3ClockSnapshot clock);

    // Adds whole seconds as if the clock had kept running.
    void AdvanceClock(long seconds);

    // Counts changes by the program or a state load, so that a host notices settings worth saving.
    long ClockChanges { get; }
}
