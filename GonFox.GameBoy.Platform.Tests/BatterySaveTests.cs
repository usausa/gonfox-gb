namespace GonFox.GameBoy.Platform;

using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

using GonFox.GameBoy.Core;

public sealed class BatterySaveTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GonFox.GameBoy-Tests", Guid.NewGuid().ToString("N"));
    private BatterySaveStore Store => new(directory);
    private static string Id(byte[] image) => Convert.ToHexStringLower(SHA256.HashData(image));

    private static byte[] CounterRom(byte type = 3) => TestRom.CreateMbc1(type, 1, 3,
        0xF3, 0x3E, 10, 0xEA, 0, 0, // Enable cartridge RAM.
        0xFA, 0, 0xA0, 0x3C, 0xEA, 0, 0xA0, 0xE0, 0x43, 0x76); // Count boots in SCX, HALT.

    private static async Task Booted(EmulationRunner runner, byte value)
    {
        var watch = Stopwatch.StartNew();
        while ((!runner.Status.Registers.IsHalted || runner.Status.Registers.ScrollX != value) && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10).ConfigureAwait(false);
        }

        Assert.True(runner.Status.Registers.IsHalted);
        Assert.Equal(value, runner.Status.Registers.ScrollX);
    }

    [Fact]
    public async Task MachineRestoreRewindsLiveRamWithoutWritingBatteryFile()
    {
        var image = CounterRom();
        using var runner = new EmulationRunner();
        var store = Store;
        using var session = new EmulationSession(runner, store);
        await session.LoadAsync(image, "counter");
        await Booted(runner, 1);
        await runner.CaptureStateAsync();
        await runner.ResetAsync();
        await runner.ResumeAsync();
        await Booted(runner, 2);
        await session.SaveForCloseAsync();
        var path = store.GetPath(Id(image));
        var savedFile = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(2, savedFile[0]);
        await runner.RestoreStateAsync();
        Assert.False(runner.Status.Running);
        Assert.Equal(1, runner.Status.Registers.ScrollX);
        Assert.Equal(1, (await runner.ReadMemoryAsync(0xA000, 1)).Bytes[0]);
        Assert.Equal(savedFile, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        await session.SaveForCloseAsync();
        Assert.Equal(1, (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken))[0]);
    }

    [Fact]
    public async Task SaveThenNewSessionRestoresRamAndCounterAdvances()
    {
        var image = CounterRom();
        var store = Store;
        using (var runner = new EmulationRunner())
        {
            using var session = new EmulationSession(runner, store);
            await session.LoadAsync(image, "counter");
            await Booted(runner, 1);
            await session.SaveForCloseAsync();
            Assert.False(runner.Status.Running);
            Assert.False(session.HasPendingSave);
            Assert.Equal(1, (await File.ReadAllBytesAsync(store.GetPath(Id(image)), TestContext.Current.CancellationToken))[0]);
        }
        using var reopened = new EmulationRunner();
        using var other = new EmulationSession(reopened, store);
        await other.LoadAsync(image, "renamed.gb");
        await Booted(reopened, 2);
        await other.SaveForCloseAsync();
        Assert.Equal(2, (await File.ReadAllBytesAsync(store.GetPath(Id(image)), TestContext.Current.CancellationToken))[0]);
        Assert.Single(Directory.GetFiles(directory, "*.sav"));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task ResetAndSameRomReloadCarryLatestRamRatherThanOldSave()
    {
        var image = CounterRom();
        using var runner = new EmulationRunner();
        using var session = new EmulationSession(runner, Store);
        await session.LoadAsync(image, "counter");
        await Booted(runner, 1);
        await session.SaveAsync();
        Assert.True(runner.Status.Running);
        await runner.ResetAsync();
        await Booted(runner, 2); // The file still holds 1.
        await session.LoadAsync(image, "counter");
        await Booted(runner, 3);
        await session.SaveForCloseAsync();
        Assert.Equal(3, (await File.ReadAllBytesAsync(Store.GetPath(Id(image)), TestContext.Current.CancellationToken))[0]);
    }

    [Fact]
    public async Task SameNameDifferentContentHasSeparateFilesAndSwitchSavesOldRom()
    {
        byte[] first = CounterRom(), second = CounterRom();
        second[^1] ^= 1;
        using var runner = new EmulationRunner();
        using var session = new EmulationSession(runner, Store);
        await session.LoadAsync(first, "same.gb");
        await Booted(runner, 1);
        await session.LoadAsync(second, "same.gb");
        await Booted(runner, 1);
        Assert.Equal(1, (await File.ReadAllBytesAsync(Store.GetPath(Id(first)), TestContext.Current.CancellationToken))[0]);
        await session.SaveForCloseAsync();
        Assert.Equal(2, Directory.GetFiles(directory, "*.sav").Length);
        await session.LoadAsync(first, "new-name.gb");
        await Booted(runner, 2);
    }

    [Fact]
    public async Task FailedSwitchKeepsOriginalFileLiveCartridgeAndDetachedRamForRetry()
    {
        var image = CounterRom();
        using var runner = new EmulationRunner();
        var store = Store;
        using var session = new EmulationSession(runner, store);
        await session.LoadAsync(image, "counter");
        await Booted(runner, 1);
        await session.SaveAsync();
        await runner.ResetAsync();
        await Booted(runner, 2);
        var path = store.GetPath(Id(image));
        await using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => session.LoadAsync(TestRom.Create(0x18, 0xFE), "other"));
            Assert.False(runner.Status.Running);
            Assert.True(runner.Status.HasBattery);
            Assert.True(session.HasPendingSave);
            Assert.Equal(1, (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken))[0]);
            Assert.Equal(2, (await runner.CaptureSaveAsync()).Ram![0]);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        await session.LoadAsync(TestRom.Create(0x18, 0xFE), "other");
        Assert.False(session.HasPendingSave);
        Assert.False(runner.Status.HasBattery);
        Assert.Equal(2, (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken))[0]);
    }

    [Fact]
    public async Task FailedCloseCanRetryWithoutLosingRamOrDisposingRunner()
    {
        var image = CounterRom();
        using var runner = new EmulationRunner();
        var store = Store;
        using var session = new EmulationSession(runner, store);
        await session.LoadAsync(image, "counter");
        await Booted(runner, 1);
        Directory.CreateDirectory(store.GetPath(Id(image))); // Blocks the save.
        await Assert.ThrowsAnyAsync<IOException>(session.SaveForCloseAsync);
        Assert.True(session.HasPendingSave);
        Assert.False(runner.Status.Running);
        Assert.Equal(1, (await runner.CaptureSaveAsync()).Ram![0]);
        Directory.Delete(store.GetPath(Id(image)));
        await session.SaveForCloseAsync();
        Assert.False(session.HasPendingSave);
        Assert.Equal(1, (await File.ReadAllBytesAsync(store.GetPath(Id(image)), TestContext.Current.CancellationToken))[0]);
    }

    [Fact]
    public async Task InvalidSaveCannotReplaceCurrentRomOrOverwriteBadFile()
    {
        var image = CounterRom();
        using var runner = new EmulationRunner();
        var store = Store;
        using var session = new EmulationSession(runner, store);
        await session.LoadAsync(TestRom.Create(0x18, 0xFE), "old");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(store.GetPath(Id(image)), [42], TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => session.LoadAsync(image, "counter"));
        Assert.False(runner.Status.HasBattery);
        Assert.False(runner.Status.Running);
        Assert.Equal("*"u8.ToArray(), await File.ReadAllBytesAsync(store.GetPath(Id(image)), TestContext.Current.CancellationToken));
        Assert.False(session.HasPendingSave);
    }

    [Fact]
    public async Task InvalidRomDoesNotPauseOrSaveTheCurrentCartridge()
    {
        using var runner = new EmulationRunner();
        using var session = new EmulationSession(runner, Store);
        await session.LoadAsync(CounterRom(), "counter");
        await Booted(runner, 1);
        await Assert.ThrowsAnyAsync<Exception>(() => session.LoadAsync([0], "bad"));
        Assert.True(runner.Status.Running);
        Assert.True(runner.Status.HasBattery);
        Assert.Empty(Directory.GetFiles(directory, "*.sav"));
    }

    [Fact]
    public async Task NonBatteryRamDoesNotReadOrCreateSaveFiles()
    {
        var image = CounterRom(2);
        var store = Store;
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(store.GetPath(Id(image)), [42], TestContext.Current.CancellationToken);
        using var runner = new EmulationRunner();
        using var session = new EmulationSession(runner, store);
        await session.LoadAsync(image, "volatile");
        await Booted(runner, 1);
        await session.SaveForCloseAsync();
        Assert.False(runner.Status.HasBattery);
        Assert.Equal("*"u8.ToArray(), await File.ReadAllBytesAsync(store.GetPath(Id(image)), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAndCloseTransactionsAreSerialized()
    {
        var image = CounterRom();
        using var runner = new EmulationRunner();
        using var session = new EmulationSession(runner, Store);
        var loading = session.LoadAsync(image, "counter");
        var closing = session.SaveForCloseAsync();
        await Task.WhenAll(loading, closing);
        Assert.False(runner.Status.Running);
        Assert.Equal(32768, (await File.ReadAllBytesAsync(Store.GetPath(Id(image)), TestContext.Current.CancellationToken)).Length);
        Assert.Equal((await runner.CaptureSaveAsync()).Ram, await File.ReadAllBytesAsync(Store.GetPath(Id(image)), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StoreRejectsBadIdentifiersAndUnreadablePaths()
    {
        var store = Store;
        Assert.Throws<ArgumentException>(() => store.GetPath("../other"));
        Assert.Null(await store.ReadAsync(new string('0', 64), 8192));
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        await File.WriteAllTextAsync(directory, "not a directory", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => store.ReadAsync(new string('0', 64), 8192));
        File.Delete(directory);
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
