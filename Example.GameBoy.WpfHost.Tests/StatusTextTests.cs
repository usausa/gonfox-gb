namespace Example.GameBoy.WpfHost;

using GonFox.GameBoy.Core;
using GonFox.GameBoy.Core.Cpu;
using GonFox.GameBoy.Platform;

public sealed class StatusTextTests
{
    private static readonly DebugSnapshot Registers = new(0x01B0, 0x0013, 0x00D8, 0x014D, 0xFFFE, 0x0150, 1_234_567)
    {
        Ly = 144,
        PpuMode = 1,
        PpuDot = 12,
        ScrollX = 3,
        DividerCounter = 0xAB00,
        TimerControl = 0xF8,
        RomBank1 = 1,
        IsHalted = true
    };

    private static EmulationStatus Status(double fps = 59.7, bool running = true, DebugSnapshot? registers = null, int rewind = 0) =>
        new(true, running, false, null, "DEMO", null, string.Empty, registers ?? Registers, fps, 0, false, null, rewind, rewind > 0);

    [Fact]
    public void FirstUpdateFormatsEveryLine()
    {
        var text = new StatusText();
        Assert.Equal(StatusLines.Title | StatusLines.Fps | StatusLines.State | StatusLines.Registers | StatusLines.Video |
            StatusLines.Device | StatusLines.Snapshot, text.Update(Status()));
        Assert.Equal("DEMO", text.Title);
        Assert.Equal($"FPS {59.7:F1}", text.Fps);
        Assert.Equal("Running", text.State);
        Assert.Equal("AF 01B0 BC 0013 DE 00D8 HL 014D SP FFFE PC 0150  ZNHC 1011", text.Registers);
        Assert.Equal("LY 144 Mode 1 Dot  12 SCX   3 SCY   0 IME 0 EI 0 HALT 1 STOP 0", text.Video);
        Assert.Equal("DIV AB00 TIMA 00 TMA 00 TAC F8 IF 00 IE 00  ROM 0/1 RAM 0:OFF MBC mode 0 DMA 0", text.Device);
        Assert.Equal($"T={1_234_567:N0}  Delay 0 ms  Rewind 0 s", text.Snapshot);
    }

    [Fact]
    public void UnchangedValuesKeepTheirStringsAndOnlyChangedLinesAreReported()
    {
        var text = new StatusText();
        var status = Status();
        text.Update(status);
        string[] before = [text.Title, text.Fps, text.State, text.Registers, text.Video, text.Device, text.Snapshot];
        Assert.Equal(StatusLines.None, text.Update(status)); // Same record.
        Assert.Equal(StatusLines.None, text.Update(Status())); // Equal values in a new record.
        string[] after = [text.Title, text.Fps, text.State, text.Registers, text.Video, text.Device, text.Snapshot];
        Assert.Equal(before, after);
        for (var i = 0; i < before.Length; i++)
        {
            Assert.Same(before[i], after[i]);
        }

        Assert.Equal(StatusLines.Fps, text.Update(Status(fps: 0)));
        Assert.Equal($"FPS {0.0:F1}", text.Fps);
        Assert.Equal(StatusLines.State, text.Update(Status(fps: 0, running: false)));
        Assert.Equal("Paused", text.State);
        Assert.Equal(StatusLines.Registers, text.Update(Status(fps: 0, running: false, registers: Registers with { PC = 0x0151 })));
        Assert.EndsWith("PC 0151  ZNHC 1011", text.Registers, StringComparison.Ordinal);
        var moved = Registers with { PC = 0x0151, Ly = 0, TotalTCycles = 1_234_571 };
        Assert.Equal(StatusLines.Video | StatusLines.Snapshot, text.Update(Status(fps: 0, running: false, registers: moved)));
        Assert.Equal(StatusLines.Device, text.Update(Status(fps: 0, running: false, registers: moved with { DividerCounter = 0xAB04 })));
        Assert.Equal(StatusLines.Snapshot, text.Update(Status(fps: 0, running: false, registers: moved with { DividerCounter = 0xAB04 }, rewind: 5)));
        Assert.EndsWith("Rewind 5 s", text.Snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStateLineShowsAMappedBootRom()
    {
        var text = new StatusText();
        text.Update(Status() with { BootRomMapped = true });
        Assert.Equal("Running · Boot ROM", text.State);
        Assert.Equal(StatusLines.State, text.Update(Status()));
        Assert.Equal("Running", text.State);
    }

    [Fact]
    public void TheTitleWithoutARomTheMotorAndALockedCpuAreWorded()
    {
        var text = new StatusText();
        text.Update(Status() with { RomLoaded = false, Title = string.Empty });
        Assert.Equal(("No ROM", "No ROM"), (text.Title, text.State));
        Assert.Equal(StatusLines.Title | StatusLines.State, text.Update(Status() with { Rumble = true }));
        Assert.Equal(("DEMO", "Running · Rumble"), (text.Title, text.State));
        text.Update(Status() with { Fault = new CpuFault(0x0150, 0xD3) });
        Assert.Equal("Running · CPU locked", text.State);
    }

    // Opening the debugging section formats all its lines, even when nothing changed.
    [Fact]
    public void DebuggingLinesAreFormattedOnlyWhileShown()
    {
        var text = new StatusText();
        Assert.Equal(StatusLines.Title | StatusLines.Fps | StatusLines.State, text.Update(Status(), details: false));
        Assert.Equal(string.Empty, text.Registers);
        Assert.Equal(string.Empty, text.Snapshot);
        var moved = Registers with { PC = 0x0151, TotalTCycles = 2_000_000 };
        Assert.Equal(StatusLines.None, text.Update(Status(registers: moved), details: false)); // Only debugging values moved.
        Assert.Equal(string.Empty, text.Registers);
        var status = Status(registers: moved);
        Assert.Equal(StatusLines.Registers | StatusLines.Video | StatusLines.Device | StatusLines.Snapshot, text.Update(status, details: true));
        Assert.Contains("PC 0151", text.Registers, StringComparison.Ordinal);
        Assert.StartsWith($"T={2_000_000:N0}", text.Snapshot, StringComparison.Ordinal);
        Assert.Equal(StatusLines.None, text.Update(status, details: true));

        // The first opening formats the lines even when all values are zero.
        var zero = new StatusText();
        var blank = Status(fps: 0, running: false, registers: new DebugSnapshot(0, 0, 0, 0, 0, 0, 0));
        zero.Update(blank, details: false);
        Assert.Equal(StatusLines.Registers | StatusLines.Video | StatusLines.Device | StatusLines.Snapshot, zero.Update(blank, details: true));
        Assert.Equal("AF 0000 BC 0000 DE 0000 HL 0000 SP 0000 PC 0000  ZNHC 0000", zero.Registers);
        Assert.Equal("LY   0 Mode 0 Dot   0 SCX   0 SCY   0 IME 0 EI 0 HALT 0 STOP 0", zero.Video);
        Assert.Equal("T=0  Delay 0 ms  Rewind 0 s", zero.Snapshot);
    }
}
