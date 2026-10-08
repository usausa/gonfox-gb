namespace Example.GameBoy.WpfHost;

using GonFox.GameBoy.Core.Cpu;

// Formats the values the libraries report as short status texts.
internal static class HostText
{
    internal const string InvalidMemoryRange = "Start 0000-FFFF (hex), length 1-256";

    internal static string Lockup(CpuFault fault) => $"CPU locked: opcode {fault.Opcode:X2} at {fault.Address:X4}";

    internal static string Session(SessionNotice notice) => notice.Event switch
    {
        SessionEvent.Loaded when notice.Path is null => "No battery save",
        SessionEvent.Loaded => $"Save RAM: {notice.Path}{LoadNotes(notice)}",
        SessionEvent.LoadFailed => $"Load failed: {notice.Error?.Message}",
        SessionEvent.Saving => notice.Automatic ? "Auto-saving" : "Saving RAM",
        SessionEvent.Saved => $"{(notice.Automatic ? "Auto-saved" : "Saved")} {notice.At.ToLocalTime():HH:mm:ss}: {notice.Path}",
        SessionEvent.SaveFailed when notice.Automatic => $"Auto-save failed, retrying: {notice.Error?.Message}",
        SessionEvent.SaveFailed => $"Save failed, RAM kept: {notice.Error?.Message}",
        _ => string.Empty
    };

    internal static string AudioDevice(AudioStreamInfo? info) => info is null
        ? string.Empty
        : $"WASAPI shared, {info.SampleRate / 1000.0:0.#} kHz, {info.Latency.TotalMilliseconds:0} ms";

    private static string LoadNotes(SessionNotice notice) =>
        (notice.Notes.HasFlag(BatteryLoadNotes.NewFile) ? " (new)" : string.Empty) +
        (notice.Notes.HasFlag(BatteryLoadNotes.ChangedOutside) ? " (changed outside, backup kept)" : string.Empty) +
        (notice.Notes.HasFlag(BatteryLoadNotes.MissingClockRecord) ? " (no clock record)" : string.Empty) +
        (notice.Notes.HasFlag(BatteryLoadNotes.ClockRecord) ? $" (clock +{notice.ClockSeconds} s)" : string.Empty);
}
