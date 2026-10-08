namespace GonFox.GameBoy.Platform;

using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

using GonFox.GameBoy.Core;

// Tests state slots on an in-memory record store, and rewind.
public sealed class StateSlotTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static string Id(byte[] image) => Convert.ToHexStringLower(SHA256.HashData(image));

    private static byte[] Loop() => TestRom.Create(0x18, 0xFE);

    // Clears WRAM C000, then increments it once per frame and shows it in SCX.
    private static byte[] Counter() => TestRom.Create(0xAF, 0xEA, 0x00, 0xC0, 0xF0, 0x44, 0xFE, 0x90, 0x20, 0xFA,
        0xF0, 0x44, 0xFE, 0x90, 0x28, 0xFA, 0xFA, 0x00, 0xC0, 0x3C, 0xEA, 0x00, 0xC0, 0xE0, 0x43, 0x18, 0xE9);

    private static async Task Until(Func<bool> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10).ConfigureAwait(false);
        }

        Assert.True(condition(), what);
    }

    private static GameBoyState State(byte[] image, int cycles = 10_000)
    {
        var system = new GameBoySystem();
        system.InsertCartridge(Core.Cartridge.CartridgeLoader.Load(image).Cartridge);
        system.RunForTCycles(cycles);
        return system.CaptureState();
    }

    [Fact]
    public void TheRecordCarriesTimeHashErrorAndTheCoreBytes()
    {
        var state = State(Loop());
        var record = StateSlotStore.Encode(new(state, "The runner stopped."), Now);
        Assert.Equal("SESTATE1"u8.ToArray(), record[..8]);
        Assert.Equal(Now, StateSlotStore.SavedAt(record));
        var back = StateSlotStore.Decode(record, "here");
        Assert.Equal("The runner stopped.", back.Error);
        Assert.Equal(state.Serialize(), back.State.Serialize());
        Assert.Null(StateSlotStore.Decode(StateSlotStore.Encode(new(state, null), Now), "here").Error);
    }

    [Fact]
    public void DamagedTruncatedOrOtherFormatRecordsAreRejected()
    {
        var record = StateSlotStore.Encode(new(State(Loop()), null), Now);
        byte[] Changed(Action<byte[]> change)
        {
            var copy = record.ToArray();
            change(copy);
            return copy;
        }
        Assert.Throws<InvalidDataException>(() => StateSlotStore.Decode(Changed(b => b[0] ^= 1), "x")); // Not a state record.
        Assert.Contains("hash mismatch", Assert.Throws<InvalidDataException>(() => StateSlotStore.Decode(Changed(b => b[^1] ^= 1), "x")).Message, StringComparison.Ordinal);
        foreach (var length in new[] { 0, 8, 51, 56, record.Length - 1 })
        {
            Assert.Throws<InvalidDataException>(() => StateSlotStore.Decode(record.AsSpan(0, length), "x"));
        }

        Assert.Throws<InvalidDataException>(() => StateSlotStore.Decode([.. record, 0], "x"));

        // Sets the Core's format version to 6 and fixes the record's hash to match.
        var older = Changed(b =>
        {
            var payload = 8 + 8 + 32 + 4 + 0 + 4;
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(payload + 8), 6);
            SHA256.HashData(b.AsSpan(payload)).CopyTo(b.AsSpan(16));
        });
        var error = Assert.Throws<NotSupportedException>(() => StateSlotStore.Decode(older, "x"));
        Assert.Contains("format 6", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SlotsAreListedPerRomAndCheckTheRom()
    {
        var records = new MemoryRecordStore();
        var slots = new StateSlotStore(records);
        byte[] loop = Loop(), counter = Counter();
        Assert.Empty(await slots.ListAsync(Id(loop)));
        await slots.SaveAsync(Id(loop), 3, new(State(loop), null), Now);
        await slots.SaveAsync(Id(loop), 9, new(State(loop), null), Now);
        await slots.SaveAsync(Id(counter), 1, new(State(counter), null), Now);
        await records.WriteAsync(Id(loop) + ".slot10.state", [1]);
        await records.WriteAsync(Id(loop) + ".slot1.state.bak", [1]);
        Assert.Equal([3, 9], (await slots.ListAsync(Id(loop))).Select(s => s.Slot));
        Assert.Null(await slots.LoadAsync(Id(loop), 1));
        Assert.Equal(Id(loop), (await slots.LoadAsync(Id(loop), 3))!.State.RomSha256);
        await records.WriteAsync(Id(loop) + ".slot4.state", (await records.ReadAsync(Id(counter) + ".slot1.state", 1 << 24))!);
        Assert.Contains("another ROM", (await Assert.ThrowsAsync<InvalidDataException>(() => slots.LoadAsync(Id(loop), 4))).Message, StringComparison.Ordinal);
        foreach (var slot in new[] { 0, 10 })
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => slots.SaveAsync(Id(loop), slot, new(State(loop), null), Now));
        }

        Assert.Equal("memory:" + Id(loop) + ".slot3.state", slots.Describe(Id(loop), 3));
    }

    [Fact]
    public async Task SessionSavesPausesAndRestoresAcrossSessions()
    {
        var records = new MemoryRecordStore();
        var image = Counter();
        DebugSnapshot saved;
        using (var runner = new EmulationRunner())
        using (var session = new EmulationSession(runner, new BatterySaveStore(new MemoryRecordStore()), states: new StateSlotStore(records)))
        {
            await session.LoadAsync(image, "counter");
            Assert.Empty(session.Slots);
            await Until(() => runner.Status.Registers.ScrollX >= 3, "the counter runs");
            await session.SaveStateAsync(2);
            Assert.False(runner.Status.Running);
            saved = runner.Status.Registers;
            Assert.Equal([2], session.Slots.Select(s => s.Slot));
            await runner.ResumeAsync();
            await Until(() => runner.Status.Registers.ScrollX > saved.ScrollX + 2, "the counter moves on");
            await session.LoadStateAsync(2);
            Assert.False(runner.Status.Running);
            Assert.Equal(saved, runner.Status.Registers);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.LoadStateAsync(5)); // Empty.
            Assert.Equal(saved, runner.Status.Registers);
        }
        using (var runner = new EmulationRunner())
        using (var session = new EmulationSession(runner, new BatterySaveStore(new MemoryRecordStore()), states: new StateSlotStore(records)))
        {
            await session.LoadAsync(image, "counter again");
            Assert.Equal([2], session.Slots.Select(s => s.Slot));
            await session.LoadStateAsync(2);
            Assert.Equal(saved, runner.Status.Registers);
            await session.LoadAsync(Loop(), "other ROM");
            Assert.Empty(session.Slots);
        }
    }

    [Fact]
    public async Task ARejectedSlotLeavesTheMachineRunning()
    {
        var records = new MemoryRecordStore();
        var image = Counter();
        var name = Id(image) + ".slot1.state";
        using var runner = new EmulationRunner();
        using var session = new EmulationSession(runner, new BatterySaveStore(new MemoryRecordStore()), states: new StateSlotStore(records));
        await session.LoadAsync(image, "counter");
        await records.WriteAsync(name, [1, 2, 3]);
        await Assert.ThrowsAsync<InvalidDataException>(() => session.LoadStateAsync(1));
        Assert.True(runner.Status.Running);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EmulationSession(runner, new BatterySaveStore(new MemoryRecordStore())).SaveStateAsync(1)); // No state store.
    }

    [Fact]
    public async Task AFaultedMachineKeepsItsErrorThroughASlot()
    {
        var records = new MemoryRecordStore();
        var image = TestRom.Create(0xD3);
        using var runner = new EmulationRunner();
        using var session = new EmulationSession(runner, new BatterySaveStore(new MemoryRecordStore()), states: new StateSlotStore(records));
        await session.LoadAsync(image, "fault");
        await Until(() => runner.Status.Faulted, "the fault");
        var error = runner.Status.Error;
        await session.SaveStateAsync(1);
        await runner.ResetAsync();
        Assert.False(runner.Status.Faulted);
        await session.LoadStateAsync(1);
        Assert.True(runner.Status.Faulted);
        Assert.Equal(error, runner.Status.Error);
    }

    [Fact]
    public async Task RewindGoesBackOneRecordAtATimeAndPauses()
    {
        var options = new RewindOptions((ulong)GameBoySystem.TCyclesPerSecond / 16, 4);
        using var runner = new EmulationRunner(rewind: options);
        await runner.LoadAsync(Counter(), "counter");
        await runner.PauseAsync();
        await runner.ResetAsync(); // Clears the records.
        Assert.False(runner.Status.RewindAvailable);
        Assert.Null(await runner.RewindAsync());
        await runner.ResumeAsync();
        await Until(() => runner.Status.Registers.TotalTCycles > options.IntervalTCycles * 8, "eight intervals");
        await runner.PauseAsync();
        Assert.True(runner.Status.RewindAvailable);
        Assert.InRange(runner.Status.RewindSeconds, 0, 1);
        var before = runner.Status.Registers.TotalTCycles;
        var back = await runner.RewindAsync();
        Assert.NotNull(back);
        Assert.InRange(back.Value, options.IntervalTCycles / 2, (options.IntervalTCycles * 3 / 2) + 8192);
        Assert.False(runner.Status.Running);
        Assert.Equal(before - back.Value, runner.Status.Registers.TotalTCycles);
        var steps = 1;
        for (ulong? step; steps < 20 && (step = await runner.RewindAsync()) is not null; steps++)
        {
            Assert.InRange(step.Value, options.IntervalTCycles - 8192, options.IntervalTCycles + 8192);
        }

        Assert.InRange(steps, options.Capacity - 1, options.Capacity); // Only the newest four kept.
        Assert.False(runner.Status.RewindAvailable);
        await runner.ResumeAsync();
        await Until(() => runner.Status.RewindAvailable, "a new record after resuming");
        await runner.PauseAsync();
        await runner.ResetAsync();
        Assert.False(runner.Status.RewindAvailable);
        Assert.Equal(0, runner.Status.RewindSeconds);
    }

    [Fact]
    public async Task SlotsAndLoadsClearTheRewindRecords()
    {
        var options = new RewindOptions((ulong)GameBoySystem.TCyclesPerSecond / 16, 4);
        using var runner = new EmulationRunner(rewind: options);
        using var session = new EmulationSession(runner, new BatterySaveStore(new MemoryRecordStore()), states: new StateSlotStore(new MemoryRecordStore()));
        await session.LoadAsync(Counter(), "counter");
        await Until(() => runner.Status.RewindAvailable, "records");
        await runner.PauseAsync();
        await session.SaveStateAsync(1);
        Assert.True(runner.Status.RewindAvailable); // Saving keeps them.
        await session.LoadStateAsync(1);
        Assert.False(runner.Status.RewindAvailable); // Another time line.
        await runner.ResumeAsync();
        await Until(() => runner.Status.RewindAvailable, "records again");
        await session.LoadAsync(Loop(), "loop");
        Assert.False(runner.Status.RewindAvailable);
        Assert.Equal(0, runner.Status.RewindSeconds);
        using var off = new EmulationRunner(rewind: options with { Capacity = 0 });
        await off.LoadAsync(Counter(), "counter");
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.False(off.Status.RewindAvailable);
    }
}
