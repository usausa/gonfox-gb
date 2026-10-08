namespace GonFox.GameBoy.Platform;

using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

using GonFox.GameBoy.Core;

// Tests the MBC3 clock across sessions, backups, hash records, save locks and periodic saves.
public sealed class BatteryRobustnessTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GonFox.GameBoy-Tests", Guid.NewGuid().ToString("N"));
    private BatterySaveStore Store => new(directory);
    private static string Id(byte[] image) => Convert.ToHexStringLower(SHA256.HashData(image));
    private static readonly DateTimeOffset Start = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

    // Increments RAM byte 0 once per boot, shows it in SCX and halts.
    private static byte[] CounterRom(byte type = 3) => TestRom.CreateMbc1(type, 1, 3,
        0xF3, 0x3E, 10, 0xEA, 0, 0, 0xFA, 0, 0xA0, 0x3C, 0xEA, 0, 0xA0, 0xE0, 0x43, 0x76);

    private static byte[] ClockRom() => TestRom.CreateMbc3(0x10, 1, 3, 0xF3, 0x76); // MBC3 with clock: DI; HALT.

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10).ConfigureAwait(false);
        }

        Assert.True(condition(), what);
    }

    private static Task Booted(EmulationRunner runner, byte value) =>
        Until(() => runner.Status.Registers.IsHalted && runner.Status.Registers.ScrollX == value, $"boot count {value}");

    private BatteryContents ReadFile(byte[] image, bool clock) =>
        BatteryFile.Decode(File.ReadAllBytes(Store.GetPath(Id(image))), 32768, clock, "test");

    [Fact]
    public async Task ClockAdvancesByTheTimeBetweenSaveAndLoad()
    {
        var image = ClockRom();
        var time = new ManualTime(Start);
        using (var runner = new EmulationRunner())
        using (var session = new EmulationSession(runner, Store, time))
        {
            await session.LoadAsync(image, "clock");
            Assert.Equal(BatteryLoadNotes.NewFile, session.Notice.Notes);
            await session.SaveForCloseAsync();
        }
        var first = ReadFile(image, clock: true);
        Assert.Equal(Start.ToUnixTimeSeconds(), first.SavedUnixSeconds);
        Assert.Equal(0, first.Clock!.Value.Current.Hours);
        time.Now += TimeSpan.FromSeconds((3 * 86_400) + 3661);
        using (var runner = new EmulationRunner())
        using (var session = new EmulationSession(runner, Store, time))
        {
            await session.LoadAsync(image, "clock");
            Assert.Equal((BatteryLoadNotes.ClockRecord, 262_861L), (session.Notice.Notes, session.Notice.ClockSeconds));
            await session.SaveForCloseAsync();
        }
        var current = ReadFile(image, clock: true).Clock!.Value.Current;
        Assert.Equal((3, 0, 1, 1), (current.DayLow, current.DayHigh, current.Hours, current.Minutes));
        Assert.InRange(current.Seconds, 1, 3); // Plus emulated time.
    }

    [Fact]
    public async Task LegacyFutureAndClocklessFilesLoad()
    {
        var image = ClockRom();
        var time = new ManualTime(Start);
        var path = Store.GetPath(Id(image));
        Directory.CreateDirectory(directory);
        async Task<BatteryContents> LoadAndSave(byte[] file, BatteryLoadNotes notes, long seconds)
        {
            await File.WriteAllBytesAsync(path, file).ConfigureAwait(false);
            File.Delete(path + ".sha256");
            using var runner = new EmulationRunner();
            using var session = new EmulationSession(runner, Store, time);
            await session.LoadAsync(image, "clock").ConfigureAwait(false);
            Assert.Equal((notes, seconds), (session.Notice.Notes, session.Notice.ClockSeconds));
            await session.SaveForCloseAsync().ConfigureAwait(false);
            return ReadFile(image, clock: true);
        }
        var legacy = new byte[32768 + 44];
        legacy[32768] = 10;
        BitConverter.TryWriteBytes(legacy.AsSpan(32768 + 40), (uint)(Start.ToUnixTimeSeconds() - 100));
        var advanced = (await LoadAndSave(legacy, BatteryLoadNotes.ClockRecord, 100)).Clock!.Value.Current;
        Assert.Equal(1, advanced.Minutes);
        Assert.InRange(advanced.Seconds, 50, 52); // 10 s + 100 s.
        var future = BatteryFile.Encode(new(new byte[32768], new(new(10, 0, 0, 0, 0), default), Start.ToUnixTimeSeconds() + 1000));
        var kept = (await LoadAndSave(future, BatteryLoadNotes.ClockRecord, 0)).Clock!.Value.Current;
        Assert.Equal(0, kept.Minutes);
        Assert.InRange(kept.Seconds, 10, 12); // Future save adds nothing.
        var fresh = (await LoadAndSave(new byte[32768], BatteryLoadNotes.MissingClockRecord, 0)).Clock!.Value.Current;
        Assert.Equal(0, fresh.Minutes);
        Assert.InRange(fresh.Seconds, 0, 2);
    }

    [Fact]
    public async Task EachSaveKeepsThePreviousGenerationAndRecordsItsHash()
    {
        var image = CounterRom();
        var path = Store.GetPath(Id(image));
        using var runner = new EmulationRunner();
        using var session = new EmulationSession(runner, Store);
        await session.LoadAsync(image, "counter");
        await Booted(runner, 1);
        await session.SaveAsync();
        Assert.False(File.Exists(path + ".bak"));
        for (byte boot = 2; boot <= 3; boot++)
        {
            await runner.ResetAsync();
            await runner.ResumeAsync();
            await Booted(runner, boot);
            await session.SaveAsync();
            Assert.Equal(boot, (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken))[0]);
            Assert.Equal(boot - 1, (await File.ReadAllBytesAsync(path + ".bak", TestContext.Current.CancellationToken))[0]);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken))), await File.ReadAllTextAsync(path + ".sha256", TestContext.Current.CancellationToken));
        }
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task AFileChangedSinceTheLastSaveLoadsWithAWarning()
    {
        var image = CounterRom();
        var path = Store.GetPath(Id(image));
        using (var runner = new EmulationRunner())
        using (var session = new EmulationSession(runner, Store))
        {
            await session.LoadAsync(image, "counter");
            await Booted(runner, 1);
            await session.SaveForCloseAsync();
        }
        var changed = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        changed[0] = 42;
        await File.WriteAllBytesAsync(path, changed, TestContext.Current.CancellationToken); // Same length.
        using (var runner = new EmulationRunner())
        using (var session = new EmulationSession(runner, Store))
        {
            await session.LoadAsync(image, "counter");
            await Booted(runner, 43);
            Assert.Equal((path, BatteryLoadNotes.ChangedOutside), (session.Notice.Path, session.Notice.Notes));
            await session.SaveForCloseAsync();
        }
        File.Delete(path + ".sha256"); // Unrecorded files are not judged.
        using (var runner = new EmulationRunner())
        using (var session = new EmulationSession(runner, Store))
        {
            await session.LoadAsync(image, "counter");
            await Booted(runner, 44);
            Assert.Equal(BatteryLoadNotes.None, session.Notice.Notes);
        }
    }

    [Fact]
    public async Task ASecondWindowCannotUseTheSameSaveUntilTheFirstLetsGo()
    {
        byte[] image = CounterRom(), other = TestRom.Create(0x18, 0xFE);
        using var firstRunner = new EmulationRunner();
        using var first = new EmulationSession(firstRunner, Store);
        using var secondRunner = new EmulationRunner();
        using var second = new EmulationSession(secondRunner, Store);
        await first.LoadAsync(image, "counter");
        await Booted(firstRunner, 1);
        await second.LoadAsync(other, "loop");
        var error = await Assert.ThrowsAsync<IOException>(() => second.LoadAsync(image, "counter"));
        Assert.StartsWith("In use", error.Message, StringComparison.Ordinal);
        Assert.True(secondRunner.Status.Running);
        Assert.False(secondRunner.Status.HasBattery);
        await first.LoadAsync(image, "counter again");
        await Booted(firstRunner, 2); // Own lock allows reload.
        await first.LoadAsync(other, "loop"); // Releases the lock.
        await second.LoadAsync(image, "counter");
        await Booted(secondRunner, 3);
        await Assert.ThrowsAsync<IOException>(() => first.LoadAsync(image, "counter"));
    }

    [Fact]
    public async Task PeriodicSaveWaitsForSteadyRamAndNeverPauses()
    {
        var image = CounterRom();
        var path = Store.GetPath(Id(image));
        var time = new ManualTime(Start);
        using var runner = new EmulationRunner();
        using var session = new EmulationSession(runner, Store, time);
        await session.LoadAsync(image, "counter");
        await Booted(runner, 1);
        await session.AutoSaveAsync();
        Assert.False(File.Exists(path));
        time.Now += AutoSavePolicy.Interval;
        await session.AutoSaveAsync();
        Assert.Equal(1, (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken))[0]);
        Assert.True(runner.Status.Running);
        Assert.Equal((SessionEvent.Saved, true, path), (session.Notice.Event, session.Notice.Automatic, session.Notice.Path));
        File.Delete(path);
        time.Now += AutoSavePolicy.Interval;
        await session.AutoSaveAsync();
        Assert.False(File.Exists(path));
        var quiet = TestRom.CreateMbc1(3, 1, 3, 0x18, 0xFE); // Never writes its RAM.
        await session.LoadAsync(quiet, "quiet"); // Saves the counter ROM.
        for (var check = 0; check < 3; check++)
        {
            time.Now += AutoSavePolicy.Interval;
            await session.AutoSaveAsync();
        }
        Assert.False(File.Exists(Store.GetPath(Id(quiet))));
        await session.LoadAsync(TestRom.Create(0x18, 0xFE), "no battery");
        File.Delete(path);
        var files = Directory.GetFiles(directory);
        time.Now += AutoSavePolicy.Interval;
        await session.AutoSaveAsync();
        time.Now += AutoSavePolicy.Interval;
        await session.AutoSaveAsync();
        Assert.Equal(files, Directory.GetFiles(directory));
        Assert.True(runner.Status.Running);
    }

    [Fact]
    public async Task APeriodicSaveFailureKeepsRunningAndRetries()
    {
        var image = CounterRom();
        var path = Store.GetPath(Id(image));
        var time = new ManualTime(Start);
        using var runner = new EmulationRunner();
        using var session = new EmulationSession(runner, Store, time);
        await session.LoadAsync(image, "counter");
        await Booted(runner, 1);
        Directory.CreateDirectory(path); // Blocks the save.
        await session.AutoSaveAsync();
        time.Now += AutoSavePolicy.Interval;
        await session.AutoSaveAsync();
        Assert.Equal((SessionEvent.SaveFailed, true), (session.Notice.Event, session.Notice.Automatic));
        Assert.True(session.HasPendingSave);
        Assert.True(runner.Status.Running);
        Directory.Delete(path);
        time.Now += AutoSavePolicy.Interval;
        await session.AutoSaveAsync();
        Assert.False(session.HasPendingSave);
        Assert.Equal(1, (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken))[0]);
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
