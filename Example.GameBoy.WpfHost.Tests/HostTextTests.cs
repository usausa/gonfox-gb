namespace Example.GameBoy.WpfHost;

using System.IO;

using GonFox.GameBoy.Core.Cpu;
using GonFox.GameBoy.Platform;

public sealed class HostTextTests
{
    [Fact]
    public void SaveNoticesAreWorded()
    {
        Assert.Equal(string.Empty, HostText.Session(SessionNotice.None));
        Assert.Equal("No battery save", HostText.Session(new(SessionEvent.Loaded)));
        Assert.Equal("Save RAM: x.sav (new)", HostText.Session(new(SessionEvent.Loaded, "x.sav", Notes: BatteryLoadNotes.NewFile)));
        Assert.Equal("Save RAM: x.sav (no clock record)", HostText.Session(new(SessionEvent.Loaded, "x.sav", Notes: BatteryLoadNotes.MissingClockRecord)));
        Assert.Equal("Save RAM: x.sav (changed outside, backup kept) (clock +100 s)",
            HostText.Session(new(SessionEvent.Loaded, "x.sav", Notes: BatteryLoadNotes.ChangedOutside | BatteryLoadNotes.ClockRecord, ClockSeconds: 100)));
        Assert.Equal("Load failed: bad", HostText.Session(new(SessionEvent.LoadFailed, Error: new InvalidDataException("bad"))));
        Assert.Equal("Saving RAM", HostText.Session(new(SessionEvent.Saving, "x.sav")));
        Assert.Equal("Auto-saving", HostText.Session(new(SessionEvent.Saving, "x.sav", Automatic: true)));
        var at = new DateTimeOffset(2026, 10, 8, 3, 4, 5, TimeSpan.Zero);
        Assert.Equal($"Saved {at.ToLocalTime():HH:mm:ss}: x.sav", HostText.Session(new(SessionEvent.Saved, "x.sav", At: at)));
        Assert.Equal($"Auto-saved {at.ToLocalTime():HH:mm:ss}: x.sav", HostText.Session(new(SessionEvent.Saved, "x.sav", true, at)));
        Assert.Equal("Save failed, RAM kept: full", HostText.Session(new(SessionEvent.SaveFailed, Error: new IOException("full"))));
        Assert.Equal("Auto-save failed, retrying: full", HostText.Session(new(SessionEvent.SaveFailed, Automatic: true, Error: new IOException("full"))));
    }

    [Fact]
    public void TheLockupAndTheAudioDeviceAreWorded()
    {
        Assert.Equal("CPU locked: opcode D3 at 0150", HostText.Lockup(new CpuFault(0x0150, 0xD3)));
        Assert.Equal(string.Empty, HostText.AudioDevice(null));
        Assert.Equal($"WASAPI shared, {44.1:0.#} kHz, 30 ms", HostText.AudioDevice(new(44_100, TimeSpan.FromMilliseconds(30))));
    }
}
