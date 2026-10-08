namespace Example.GameBoy.MauiHost;

using GonFox.GameBoy.Core.Cpu;

// Formats the values the libraries report as short status texts.
internal static class HostText
{
    internal static string Title(EmulationStatus status) => status.RomLoaded ? status.Title : "No ROM";

    internal static string State(EmulationStatus status) =>
        (!status.RomLoaded ? "No ROM" : !status.Running ? "Paused" : status.Stopped ? "STOP" : "Running") +
        (status.Faulted ? " · CPU locked" : string.Empty) +
        (status.Rumble ? " · Rumble" : string.Empty) +
        (status.BootRomMapped ? " · Boot ROM" : string.Empty);

    internal static string Lockup(CpuFault fault) => $"CPU locked: opcode {fault.Opcode:X2} at {fault.Address:X4}";

    internal static string AudioDevice(AudioStreamInfo? info) => info is null
        ? string.Empty
        : $"AudioTrack, {info.SampleRate / 1000.0:0.#} kHz, {info.Latency.TotalMilliseconds:0} ms{(info.LowLatency ? ", low latency" : string.Empty)}";
}
