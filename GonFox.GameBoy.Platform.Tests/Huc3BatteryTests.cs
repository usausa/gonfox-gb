namespace GonFox.GameBoy.Platform;

using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

using GonFox.GameBoy.Core;

// Tests the HuC3 battery file, its clock across sessions and periodic saves of clock changes.
public sealed class Huc3BatteryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GonFox.GameBoy-Tests", Guid.NewGuid().ToString("N"));
    private BatterySaveStore Store => new(directory);
    private static string Id(byte[] image) => Convert.ToHexStringLower(SHA256.HashData(image));
    private static readonly DateTimeOffset Start = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero); // On a whole minute.

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    // Sends one MCU command: select $B, the command, select $D, clear the semaphore.
    private static byte[] Command(byte command) =>
        [0x3E, 0x0B, 0xEA, 0x00, 0x00, 0x3E, command, 0xEA, 0x00, 0xA0, 0x3E, 0x0D, 0xEA, 0x00, 0x00, 0x3E, 0xFE, 0xEA, 0x00, 0xA0];

    // A HuC3 board with 32 KiB RAM: DI, the program, HALT.
    private static byte[] Huc3Rom(params byte[][] program) => TestRom.CreateMbc1(0xFE, 1, 3, [0xF3, .. program.SelectMany(part => part), 0x76]);

    // Adds 1 to the MCU nibble at $80 once per boot.
    private static byte[] CountingRom() => Huc3Rom(Command(0x40), Command(0x58), Command(0x10),
        [0x3E, 0x0C, 0xEA, 0x00, 0x00, 0xFA, 0x00, 0xA0, 0x3C, 0xE6, 0x0F, 0xF6, 0x30, 0x47], // $C; INC; AND 0F; OR 30; LD B,A.
        Command(0x40), Command(0x58), [0x3E, 0x0B, 0xEA, 0x00, 0x00, 0x78, 0xEA, 0x00, 0xA0, 0x3E, 0x0D, 0xEA, 0x00, 0x00, 0x3E, 0xFE, 0xEA, 0x00, 0xA0]);

    private static int Nibbles(byte[] memory, int at) => memory[at] | (memory[at + 1] << 4) | (memory[at + 2] << 8);

    private static async Task Halted(EmulationRunner runner)
    {
        var watch = Stopwatch.StartNew();
        while (!runner.Status.Registers.IsHalted && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10).ConfigureAwait(false);
        }

        Assert.True(runner.Status.Registers.IsHalted, "the program halts");
    }

    private BatteryContents ReadFile(byte[] image) => BatteryFile.DecodeHuc3(File.ReadAllBytes(Store.GetPath(Id(image))), 32768, "test");

    // The footer packs two MCU nibbles per byte, low first, then the 64-bit minute start.
    [Fact]
    public void FooterFollowsMgbasLayoutAndRoundTrips()
    {
        byte[] ram = [1, 2, 3];
        var memory = Enumerable.Range(0, 256).Select(i => (byte)((i * 7) % 16)).ToArray();
        var file = BatteryFile.Encode(new(ram, SavedUnixSeconds: 1_700_000_000, Huc3: new(memory, 25)));
        Assert.Equal(3 + 136, file.Length);
        Assert.Equal(ram, file[..3]);
        for (var i = 0; i < 128; i++)
        {
            Assert.Equal(memory[i * 2] | (memory[(i * 2) + 1] << 4), file[3 + i]);
        }

        Assert.Equal(1_700_000_000 - 25, BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(3 + 128))); // The minute's start.
        var decoded = BatteryFile.DecodeHuc3(file, 3, "x.sav");
        Assert.Equal(ram, decoded.Ram);
        Assert.Equal(memory, decoded.Huc3!.Memory);
        Assert.Equal(0, decoded.Huc3.Seconds);
        Assert.Equal(1_700_000_000 - 25, decoded.SavedUnixSeconds);
        Assert.Null(decoded.Clock);
    }

    [Fact]
    public void SameBoysFooterFillsTheNibblesItsCommandsReach()
    {
        var file = new byte[8 + 17];
        var footer = file.AsSpan(8);
        BinaryPrimitives.WriteUInt64LittleEndian(footer, 1_700_000_123);
        BinaryPrimitives.WriteUInt16LittleEndian(footer[8..], 0x3C2); // Minute 962.
        BinaryPrimitives.WriteUInt16LittleEndian(footer[10..], 0x1234); // Day: only 3 nibbles kept.
        BinaryPrimitives.WriteUInt16LittleEndian(footer[12..], 0x123);
        BinaryPrimitives.WriteUInt16LittleEndian(footer[14..], 0xABCD);
        footer[16] = 1;
        var decoded = BatteryFile.DecodeHuc3(file, 8, "x.sav");
        var expected = new byte[256];
        new byte[] { 2, 0xC, 3, 4, 3, 2 }.CopyTo(expected, 0x10);
        new byte[] { 3, 2, 1, 0xD, 0xC, 0xB, 0xA, 1 }.CopyTo(expected, 0x58);
        Assert.Equal(new byte[8], decoded.Ram);
        Assert.Equal(expected, decoded.Huc3!.Memory);
        Assert.Equal(0, decoded.Huc3.Seconds);
        Assert.Equal(1_700_000_100, decoded.SavedUnixSeconds); // Start of the host's minute.
    }

    [Fact]
    public void RamAloneIsReadAndOtherLengthsAreRejected()
    {
        var plain = BatteryFile.DecodeHuc3(new byte[8], 8, "x.sav");
        Assert.Null(plain.Huc3);
        Assert.Null(plain.SavedUnixSeconds);
        Assert.Empty(BatteryFile.DecodeHuc3(new byte[136], 0, "x.sav").Ram); // MCU only, no RAM.
        foreach (var length in new[] { 7, 8 + 16, 8 + 18, 8 + 48, 8 + 135, 8 + 137 })
        {
            Assert.Contains("x.sav", Assert.Throws<InvalidDataException>(() => BatteryFile.DecodeHuc3(new byte[length], 8, "x.sav")).Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ClockAdvancesByTheTimeBetweenSaveAndLoad()
    {
        var image = Huc3Rom();
        var time = new ManualTime(Start);
        using (var runner = new EmulationRunner())
        using (var session = new EmulationSession(runner, Store, time))
        {
            await session.LoadAsync(image, "huc3");
            Assert.Equal(BatteryLoadNotes.NewFile, session.Notice.Notes);
            await session.SaveForCloseAsync();
        }
        var file = await File.ReadAllBytesAsync(Store.GetPath(Id(image)), TestContext.Current.CancellationToken);
        Assert.Equal(32768 + 136, file.Length);
        Assert.InRange(BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(32768 + 128)), Start.ToUnixTimeSeconds() - 2, Start.ToUnixTimeSeconds());
        time.Now += TimeSpan.FromSeconds((3 * 86_400) + 3661);
        using (var runner = new EmulationRunner())
        using (var session = new EmulationSession(runner, Store, time))
        {
            await session.LoadAsync(image, "huc3");
            Assert.InRange(session.Notice.ClockSeconds, 262_860, 262_869); // 262861 s plus part of a minute.
            await session.SaveForCloseAsync();
        }
        var memory = ReadFile(image).Huc3!.Memory;
        Assert.Equal((61, 3), (Nibbles(memory, 0x10), Nibbles(memory, 0x13))); // 3 days and 61 minutes.
    }

    [Fact]
    public async Task SameBoysFileAddsTheMinutesSameBoyWouldAndIsWrittenBackInMgbasLayout()
    {
        var image = Huc3Rom();
        var time = new ManualTime(Start);
        var path = Store.GetPath(Id(image));
        Directory.CreateDirectory(directory);
        var file = new byte[32768 + 17];
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(32768), (ulong)Start.ToUnixTimeSeconds() - 100); // Two host minutes ago.
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(32768 + 8), 1439);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(32768 + 10), 6);
        await File.WriteAllBytesAsync(path, file, TestContext.Current.CancellationToken);
        using (var runner = new EmulationRunner())
        using (var session = new EmulationSession(runner, Store, time))
        {
            await session.LoadAsync(image, "huc3");
            Assert.Equal(120, session.Notice.ClockSeconds);
            await session.SaveForCloseAsync();
        }
        Assert.Equal(32768 + 136, (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).Length);
        var memory = ReadFile(image).Huc3!.Memory;
        Assert.Equal((1, 7), (Nibbles(memory, 0x10), Nibbles(memory, 0x13))); // Day 6 23:59 plus 2 minutes.
    }

    [Fact]
    public async Task PeriodicSaveNoticesAChangeToTheClockMemory()
    {
        // Writes 7 to the alarm minute at $58, leaving the RAM untouched.
        var image = Huc3Rom(Command(0x48), Command(0x55), Command(0x37));
        var path = Store.GetPath(Id(image));
        var time = new ManualTime(Start);
        using var runner = new EmulationRunner();
        using var session = new EmulationSession(runner, Store, time);
        await session.LoadAsync(image, "alarm");
        await Halted(runner);
        await session.AutoSaveAsync();
        Assert.False(File.Exists(path));
        time.Now += AutoSavePolicy.Interval;
        await session.AutoSaveAsync();
        var file = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(new byte[32768], file[..32768]);
        Assert.Equal(7, file[32768 + (0x58 / 2)] & 15);
        File.Delete(path);
        for (var check = 0; check < 2; check++)
        {
            time.Now += AutoSavePolicy.Interval;
            await session.AutoSaveAsync();
        }
        Assert.False(File.Exists(path));
        var quiet = Huc3Rom(); // Changes nothing.
        await session.LoadAsync(quiet, "quiet");
        await Halted(runner);
        for (var check = 0; check < 3; check++)
        {
            time.Now += AutoSavePolicy.Interval;
            await session.AutoSaveAsync();
        }
        Assert.False(File.Exists(Store.GetPath(Id(quiet))));
    }

    [Fact]
    public async Task ReloadingAndReopeningCarryTheClockMemory()
    {
        var image = CountingRom();
        using (var runner = new EmulationRunner())
        using (var session = new EmulationSession(runner, Store))
        {
            await session.LoadAsync(image, "count");
            await Halted(runner);
            Assert.Equal(1, (await runner.CaptureSaveAsync()).Huc3!.Memory[0x80]);
            await session.LoadAsync(image, "count");
            await Halted(runner); // Live MCU carried over.
            Assert.Equal(2, (await runner.CaptureSaveAsync()).Huc3!.Memory[0x80]);
            await session.SaveForCloseAsync();
        }
        using (var runner = new EmulationRunner())
        using (var session = new EmulationSession(runner, Store))
        {
            await session.LoadAsync(image, "count");
            await Halted(runner); // From the file.
            Assert.Equal(3, (await runner.CaptureSaveAsync()).Huc3!.Memory[0x80]);
        }
    }

    public void Dispose()
    {
        // Deletes only this test's directory under the system temp root.
        var allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "GonFox.GameBoy-Tests")) + Path.DirectorySeparatorChar;
        var actual = Path.GetFullPath(directory);
        if (actual.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Directory.Exists(actual))
        {
            Directory.Delete(actual, recursive: true);
        }
    }
}
