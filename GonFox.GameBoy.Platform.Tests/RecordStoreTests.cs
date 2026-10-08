namespace GonFox.GameBoy.Platform;

using System.IO;

// Tests the contract of both record stores, then the file store's own behaviour.
public sealed class RecordStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GonFox.GameBoy-Tests", Guid.NewGuid().ToString("N"));

    public static TheoryData<string> Kinds => ["file", "memory"];

    private IRecordStore Create(string kind) => kind == "file" ? new FileRecordStore(directory) : new MemoryRecordStore();

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ReadWriteReplaceKeepCopiesAndThePreviousVersion(string kind)
    {
        var store = Create(kind);
        Assert.Null(await store.ReadAsync("a.sav", 100));
        byte[] first = [1, 2, 3];
        await store.WriteAsync("a.sav", first, "a.sav.bak");
        first[0] = 9; // The store copied it.
        Assert.Equal([1, 2, 3], await store.ReadAsync("a.sav", 100));
        Assert.Null(await store.ReadAsync("a.sav.bak", 100)); // Nothing was replaced.
        await store.WriteAsync("a.sav", [4, 5], "a.sav.bak");
        Assert.Equal([4, 5], await store.ReadAsync("a.sav", 100));
        Assert.Equal([1, 2, 3], await store.ReadAsync("a.sav.bak", 100));
        await store.WriteAsync("a.sav", [6]);
        Assert.Equal([1, 2, 3], await store.ReadAsync("a.sav.bak", 100)); // No backup name: not kept.
        var read = (await store.ReadAsync("a.sav", 100))!;
        read[0] = 0;
        Assert.Equal([6], await store.ReadAsync("a.sav", 100));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync("a.sav.bak", 2));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ListAndDeleteWorkByName(string kind)
    {
        var store = Create(kind);
        Assert.Empty(await store.ListAsync("x"));
        await store.WriteAsync("x.slot2.state", [1, 2]);
        await store.WriteAsync("x.slot1.state", [1]);
        await store.WriteAsync("y.slot1.state", [1, 2, 3]);
        var listed = await store.ListAsync("x.");
        Assert.Equal(["x.slot1.state", "x.slot2.state"], listed.Select(r => r.Name));
        Assert.Equal([1L, 2L], listed.Select(r => r.Length));
        Assert.All(listed, r => Assert.InRange(r.Written, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5)));
        Assert.True(await store.DeleteAsync("x.slot1.state"));
        Assert.False(await store.DeleteAsync("x.slot1.state"));
        Assert.Equal(["x.slot2.state"], (await store.ListAsync("x.")).Select(r => r.Name));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task NamesCannotLeaveTheStore(string kind)
    {
        var store = Create(kind);
        foreach (var name in new[] { string.Empty, "..", "../a", "a/b", "a\\b", ".hidden", "c:a", "a b", "名前", new string('a', 161) })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync(name, [1]));
            await Assert.ThrowsAsync<ArgumentException>(() => store.ReadAsync(name, 10));
            Assert.Throws<ArgumentException>(() => store.Describe(name));
        }
        await Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync("ok", [1], "../escape"));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ALockIsExclusiveUntilReleased(string kind)
    {
        var store = Create(kind);
        var held = await store.LockAsync("a.lock");
        var error = await Assert.ThrowsAsync<IOException>(() => store.LockAsync("a.lock"));
        Assert.StartsWith("In use", error.Message, StringComparison.Ordinal);
        using (await store.LockAsync("b.lock"))
        {
            // Another name is free.
        }

        held.Dispose();
        held.Dispose(); // Twice is harmless.
        using (await store.LockAsync("a.lock"))
        {
        }
    }

    [Fact]
    public async Task FilesGoToTheCallersDirectoryWithoutTemporaryFiles()
    {
        var store = new FileRecordStore(directory);
        Assert.Equal(Path.Combine(directory, "a.sav"), store.Describe("a.sav"));
        Assert.Empty(await store.ListAsync(string.Empty)); // No directory yet.
        await store.WriteAsync("a.sav", [1]);
        await store.WriteAsync("a.sav", [2], "a.sav.bak");
        Assert.Equal(["a.sav", "a.sav.bak"], Directory.GetFiles(directory).Select(Path.GetFileName).Order());
        Assert.Equal([2], await File.ReadAllBytesAsync(Path.Combine(directory, "a.sav"), TestContext.Current.CancellationToken));
        File.WriteAllBytes(Path.Combine(directory, "a.sav.1234.tmp"), [0]); // Crash leftover, not listed.
        Assert.Equal(["a.sav", "a.sav.bak"], (await store.ListAsync("a.")).Select(r => r.Name));
        Assert.Equal("memory:a.sav", new MemoryRecordStore().Describe("a.sav"));
    }

    [Fact]
    public async Task MemoryRecordsCarryTheGivenClock()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        var store = new MemoryRecordStore(time);
        await store.WriteAsync("a", [1]);
        time.Now += TimeSpan.FromMinutes(3);
        await store.WriteAsync("b", [1]);
        Assert.Equal([time.Now - TimeSpan.FromMinutes(3), time.Now], (await store.ListAsync(string.Empty)).Select(r => r.Written));
    }

    internal sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
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
