namespace GonFox.GameBoy.Platform;

using System.Diagnostics;

using GonFox.GameBoy.Core;
using GonFox.GameBoy.Core.Video;

public sealed class DebuggingTests
{
    private static async Task Until(Func<bool> predicate)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate() && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10).ConfigureAwait(false);
        }

        Assert.True(predicate(), "Worker did not reach the expected state within five seconds.");
    }

    [Fact]
    public async Task SavePausesAndRestorePublishesSameRegistersMemoryAndImageWithNewSequence()
    {
        using var runner = new EmulationRunner();
        await runner.LoadAsync(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "background-demo.gb"), TestContext.Current.CancellationToken), "demo");
        await Until(() => runner.Status.Registers.TotalTCycles > 300_000);
        await runner.CaptureStateAsync();
        var saved = runner.Status.Registers;
        Assert.False(runner.Status.Running);
        Assert.Equal(0, runner.Status.Fps);
        Assert.Equal(saved.TotalTCycles, runner.Status.SavedStateTCycles);
        var memory = await runner.ReadMemoryAsync(0xFF00, 256);
        byte[] image = new byte[VideoOutput.BufferSize], restored = new byte[VideoOutput.BufferSize];
        Assert.True(runner.TryCopyFrame(ulong.MaxValue, image, out var frame));
        await runner.ResumeAsync();
        await Until(() => runner.Status.Registers.TotalTCycles > saved.TotalTCycles + 100_000);
        await runner.PauseAsync();
        await runner.ResetAsync();
        for (var repeat = 0; repeat < 2; repeat++)
        {
            Assert.True(runner.TryCopyFrame(ulong.MaxValue, restored, out var previous));
            await runner.RestoreStateAsync();
            Assert.False(runner.Status.Running);
            Assert.Equal(saved, runner.Status.Registers);
            Assert.Equal(memory.Bytes, (await runner.ReadMemoryAsync(0xFF00, 256)).Bytes);
            Assert.True(runner.TryCopyFrame(previous.Sequence, restored, out var current));
            Assert.True(current.Sequence > previous.Sequence && current.Sequence > frame.Sequence);
            Assert.Equal(image, restored);
            await runner.StepAsync();
            Assert.False(runner.Status.Running);
            Assert.True(runner.Status.Registers.TotalTCycles > saved.TotalTCycles);
        }
        await runner.CaptureStateAsync();
        var replaced = runner.Status.Registers;
        await runner.ResetAsync();
        await runner.RestoreStateAsync();
        Assert.Equal(replaced, runner.Status.Registers);
    }

    [Fact]
    public async Task OnlySuccessfulRomLoadClearsTheStateSlot()
    {
        using var runner = new EmulationRunner();
        await Assert.ThrowsAsync<NotSupportedException>(runner.CaptureStateAsync);
        await Assert.ThrowsAsync<InvalidOperationException>(runner.RestoreStateAsync);
        Assert.Null(runner.Status.SavedStateTCycles);
        await runner.LoadAsync(TestRom.Create(0x18, 0xFE), "loop");
        await runner.CaptureStateAsync();
        var saved = runner.Status.Registers;
        await Assert.ThrowsAnyAsync<Exception>(() => runner.LoadAsync([0], "invalid"));
        Assert.Equal(saved.TotalTCycles, runner.Status.SavedStateTCycles);
        await runner.ResetAsync();
        await runner.RestoreStateAsync();
        Assert.Equal(saved, runner.Status.Registers);
        await runner.LoadAsync(TestRom.Create(0x18, 0xFE), "same ROM reloaded");
        Assert.Null(runner.Status.SavedStateTCycles);
        await Assert.ThrowsAsync<InvalidOperationException>(runner.RestoreStateAsync);
    }

    [Fact]
    public async Task RestoredInputIsPreservedUntilResumeThenOldStopWakeIsCleared()
    {
        using var runner = new EmulationRunner();

        // Selects the button row and STOPs; a new press wakes it to increment B and loop.
        await runner.LoadAsync(TestRom.Create(0x3E, 0x10, 0xE0, 0, 0x10, 0, 0x04, 0x18, 0xFE), "stop");
        await Until(() => runner.Status.Stopped);
        await runner.PauseAsync();
        await runner.SetButtonsAsync(16);
        await runner.CaptureStateAsync();
        var stopped = runner.Status.Registers;
        await runner.ResumeAsync();
        await Until(() => runner.Status.Registers.B == 1);
        await runner.RestoreStateAsync();
        await runner.SetButtonsAsync(0);
        Assert.Equal(stopped, runner.Status.Registers);
        Assert.Equal(0xDE, (await runner.ReadMemoryAsync(0xFF00, 1)).Bytes[0]);
        await runner.ResumeAsync(0);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await runner.PauseAsync();
        Assert.Equal(stopped, runner.Status.Registers);
        Assert.Equal(0xDF, (await runner.ReadMemoryAsync(0xFF00, 1)).Bytes[0]);
        await runner.ResumeAsync();
        await runner.SetButtonsAsync(16);
        await Until(() => runner.Status.Registers.B == 1 && !runner.Status.Stopped);
        await runner.RestoreStateAsync();
        await runner.ResumeAsync(16); // Held now: a new press.
        await Until(() => runner.Status.Registers.B == 1 && !runner.Status.Stopped);
    }

    [Fact]
    public async Task FaultStateRetainsTheFaultAndHealthyStateCanRecoverExecution()
    {
        using var runner = new EmulationRunner();
        await runner.LoadAsync(TestRom.Create(0xD3), "fault");
        await Until(() => runner.Status.Faulted);
        var fault = runner.Status.Fault;
        await runner.CaptureStateAsync();
        await runner.ResetAsync();
        Assert.False(runner.Status.Faulted);
        await runner.RestoreStateAsync();
        Assert.True(runner.Status.Faulted);
        Assert.Equal(fault, runner.Status.Fault);
        var locked = runner.Status.Registers; // A step adds one M-cycle.
        await runner.StepAsync();
        Assert.Equal(locked.TotalTCycles + 4, runner.Status.Registers.TotalTCycles);
        Assert.Equal(locked.PC, runner.Status.Registers.PC);
        await runner.ResetAsync();
        await runner.CaptureStateAsync();
        await runner.ResumeAsync();
        await Until(() => runner.Status.Faulted);
        await runner.RestoreStateAsync();
        Assert.False(runner.Status.Faulted);
        Assert.Null(runner.Status.Error);
        await runner.StepAsync();
        Assert.Equal(0x150, runner.Status.Registers.PC);
    }

    [Fact]
    public async Task MemoryCopyIsOwnedAndReadOrInvalidRangeDoesNotAdvanceTheMachine()
    {
        using var runner = new EmulationRunner();
        await runner.LoadAsync(TestRom.Create(0x18, 0xFE), "loop");
        await runner.PauseAsync();
        var before = runner.Status.Registers;
        var memory = await runner.ReadMemoryAsync(0x100, 17);
        Assert.Equal(before, memory.Registers);
        Assert.Equal(0xC3, memory.Bytes[0]);
        memory.Bytes[0] = 0;
        Assert.Equal(0xC3, (await runner.ReadMemoryAsync(0x100, 1)).Bytes[0]);
        Assert.Single((await runner.ReadMemoryAsync(0xFFFF, 1)).Bytes);
        foreach (var length in new[] { -1, 0, 257 })
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => runner.ReadMemoryAsync(0, length));
        }

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => runner.ReadMemoryAsync(0xFFFF, 2));
        Assert.Equal(before, runner.Status.Registers);
    }

    [Theory]
    [InlineData("0xFF00", "256", 0xFF00, 256)]
    [InlineData(" ffff ", " 1 ", 0xFFFF, 1)]
    [InlineData("0000", "16", 0, 16)]
    public void MemoryRangeAcceptsHexAddressesAndDecimalLengths(string address, string count, int start, int length)
    {
        Assert.True(MemoryView.TryParseRange(address, count, out var parsedStart, out var parsedLength));
        Assert.Equal(start, parsedStart);
        Assert.Equal(length, parsedLength);
    }

    [Theory]
    [InlineData("FFFF", "2")]
    [InlineData("10000", "1")]
    [InlineData("-1", "1")]
    [InlineData("garbage", "16")]
    [InlineData("C000", "0")]
    [InlineData("C000", "257")]
    [InlineData("C000", "0x10")]
    [InlineData("", "")]
    public void InvalidMemoryRangeIsRejected(string address, string count) =>
        Assert.False(MemoryView.TryParseRange(address, count, out _, out _));

    [Fact]
    public void MemoryRowsKeepTheRealAddressAndShortFinalRow()
    {
        var snapshot = new MemorySnapshot(0xFEF7, Enumerable.Range(0, 17).Select(i => (byte)i).ToArray(), default);
        Assert.Equal($"FEF7: 00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F{Environment.NewLine}FF07: 10{Environment.NewLine}", MemoryView.Format(snapshot));
    }
}
