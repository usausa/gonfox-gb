namespace GonFox.GameBoy.Platform;

using System.Diagnostics;

using GonFox.GameBoy.Core;
using GonFox.GameBoy.Core.Cpu;
using GonFox.GameBoy.Core.Video;

public sealed class EmulationRunnerTests
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
    public async Task TheThreadStartActionRunsFirstOnTheRunThread()
    {
        var started = new List<(string? Name, int Thread)>();
        using var runner = new EmulationRunner(threadStarted: () =>
        {
            lock (started)
            {
                started.Add((Thread.CurrentThread.Name, Environment.CurrentManagedThreadId));
            }
        });
        await runner.LoadAsync(TestRom.Create(0x18, 0xFE), "loop"); // Runs after the action.
        lock (started)
        {
            Assert.Equal([(EmulationRunner.ThreadName, started[0].Thread)], started);
            Assert.NotEqual(Environment.CurrentManagedThreadId, started[0].Thread);
        }
    }

    [Fact]
    public async Task PauseStepResumeAndInvalidLoadPreserveSerializedState()
    {
        using var runner = new EmulationRunner();
        await runner.LoadAsync(TestRom.Create(0x18, 0xFE), "loop");
        await Until(() => runner.Status.Registers.TotalTCycles > 10_000);
        await runner.PauseAsync();
        var paused = runner.Status.Registers;
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.Equal(paused, runner.Status.Registers);
        Assert.Equal(0, runner.Status.Fps);
        await Assert.ThrowsAnyAsync<Exception>(() => runner.LoadAsync([0], "invalid"));
        Assert.Equal(paused, runner.Status.Registers);
        Assert.True(runner.Status.RomLoaded);
        await runner.StepAsync();
        Assert.Equal(paused.TotalTCycles + 12, runner.Status.Registers.TotalTCycles);
        await runner.ResumeAsync();
        await Until(() => runner.Status.Registers.TotalTCycles > paused.TotalTCycles + 20_000);
        await runner.PauseAsync();
        await runner.ResetAsync();
        Assert.Equal(0UL, runner.Status.Registers.TotalTCycles);
        Assert.False(runner.Status.Running);
    }

    [Fact]
    public async Task StopWaitsWithoutAdvancingAndNewInputWakesIt()
    {
        using var runner = new EmulationRunner();
        await runner.LoadAsync(TestRom.Create(0x3E, 0x10, 0xE0, 0x00, 0x10, 0x00, 0x04, 0x18, 0xFE), "stop"); // P1=$10, STOP.
        await Until(() => runner.Status.Stopped);
        var stopped = runner.Status.Registers;
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.Equal(stopped, runner.Status.Registers);
        Assert.Equal(0, runner.Status.Fps);
        await runner.SetButtonsAsync(16);
        await Until(() => !runner.Status.Stopped && runner.Status.Registers.B == 1);
        await runner.PauseAsync();
        Assert.False(runner.Status.Stopped);
    }

    // With P1=$30 no press can end STOP, yet the worker keeps taking commands.
    [Fact]
    public async Task AStopThatNoInputCanEndKeepsTakingCommands()
    {
        using var runner = new EmulationRunner();
        await runner.LoadAsync(TestRom.Create(0x3E, 0x30, 0xE0, 0x00, 0x10, 0x00, 0x04, 0x18, 0xFE), "stop"); // P1=$30, STOP.
        await Until(() => runner.Status.Stopped);
        var stopped = runner.Status.Registers;
        for (var mask = 1; mask < 256; mask <<= 1)
        {
            await runner.SetButtonsAsync((byte)mask);
            await runner.SetButtonsAsync(0);
        }
        await runner.ResumeAsync(0xFF);
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.True(runner.Status.Stopped);
        Assert.Equal(stopped, runner.Status.Registers);
        Assert.Equal(0, runner.Status.Fps);
        await runner.PauseAsync();
        await runner.StepAsync();
        Assert.Equal(stopped, runner.Status.Registers);
        await runner.ResetAsync();
        Assert.False(runner.Status.Stopped);
        Assert.Equal(0UL, runner.Status.Registers.TotalTCycles);
    }

    [Fact]
    public async Task ALockedCpuKeepsTheMachineRunningAndResetRecovers()
    {
        using var runner = new EmulationRunner();
        await runner.LoadAsync(TestRom.Create(0xD3), "fault");
        await Until(() => runner.Status.Faulted);
        Assert.True(runner.Status.Running);
        Assert.Equal(new CpuFault(0x0150, 0xD3), runner.Status.Fault);
        Assert.Null(runner.Status.Error);
        var locked = runner.Status.Registers.TotalTCycles;
        await Until(() => runner.Status.Registers.TotalTCycles > locked + 70_224); // One frame.
        Assert.Equal(0x151, runner.Status.Registers.PC);
        await runner.PauseAsync();
        await runner.ResumeAsync();
        Assert.True(runner.Status.Running);
        await runner.PauseAsync(); // Step from the entry.
        await runner.ResetAsync();
        Assert.False(runner.Status.Faulted);
        Assert.Null(runner.Status.Error);
        await runner.StepAsync();
        Assert.Equal(0x150, runner.Status.Registers.PC);
    }

    [Fact]
    public async Task FrameCopiesAreIndependentAndReadingThemDoesNotExecuteCpu()
    {
        using var runner = new EmulationRunner();
        await runner.LoadAsync(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "background-demo.gb"), TestContext.Current.CancellationToken), "demo");
        await Until(() => runner.Status.Registers.TotalTCycles > 250_000);
        await runner.PauseAsync();
        var paused = runner.Status.Registers;
        byte[] first = new byte[VideoOutput.BufferSize], second = new byte[VideoOutput.BufferSize];
        Assert.True(runner.TryCopyFrame(ulong.MaxValue, first, out var info));
        Assert.Contains((byte)0, first);
        Array.Fill(first, (byte)0x42);
        Assert.True(runner.TryCopyFrame(ulong.MaxValue, second, out var other));
        Assert.Equal(info, other);
        Assert.DoesNotContain((byte)0x42, second);
        for (var i = 0; i < 144; i++)
        {
            Assert.False(runner.TryCopyFrame(info.Sequence, first, out _));
        }

        Assert.Equal(paused, runner.Status.Registers);
        Assert.Throws<ArgumentException>(() => runner.TryCopyFrame(0, [], out _));
    }

    // Null goes back to the post-boot state at 0100; a boot ROM of another size is refused.
    [Fact]
    public async Task ABootRomStartsTheNextLoadAndResetFromPowerOn()
    {
        using var runner = new EmulationRunner();
        var boot = new byte[256];
        boot[0] = 0x18;
        boot[1] = 0xFE; // JR -2.
        await Assert.ThrowsAsync<ArgumentException>(() => runner.UseBootRomAsync(new byte[255]));
        await runner.UseBootRomAsync(boot);
        await runner.LoadAsync(TestRom.Create(0x18, 0xFE), "loop");
        await Until(() => runner.Status.Registers.TotalTCycles > 10_000);
        await runner.PauseAsync();
        Assert.Equal((0x0000, true), (runner.Status.Registers.PC, runner.Status.BootRomMapped));
        await runner.UseBootRomAsync(null);
        Assert.True(runner.Status.BootRomMapped); // Until the next Reset.
        await runner.ResetAsync();
        Assert.Equal((0x0100, false), (runner.Status.Registers.PC, runner.Status.BootRomMapped));
    }

    [Fact]
    public async Task ShutdownCompletesQueuedCommandsAndRejectsNewOnes()
    {
        var runner = new EmulationRunner();
        var commands = Enumerable.Range(0, 30).Select(_ => runner.PauseAsync()).ToArray();
        runner.Dispose();
        runner.Dispose();
        foreach (var task in commands)
        {
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            }
            catch (ObjectDisposedException)
            {
            }
            Assert.True(task.IsCompleted);
        }
        await Assert.ThrowsAsync<ObjectDisposedException>(runner.ResumeAsync);
    }

    // The program captures once and loops at 0173; black reads back FF and flat grey 00.
    [Fact]
    public async Task TheCameraImageReachesTheSensorOfACameraCartridge()
    {
        byte[] program =
        [
            0x3E, 0x10, 0xEA, 0x00, 0x40, // LD A,$10; LD ($4000),A
            0x21, 0x06, 0xA0, 0x06, 0x30, 0x3E, 0x80, // LD HL,$A006; LD B,48; LD A,$80
            0x22, 0x05, 0x20, 0xFC, // LD (HL+),A; DEC B; JR NZ,-4
            0x3E, 0x03, 0xEA, 0x02, 0xA0, 0xEA, 0x00, 0xA0, // LD A,3; LD ($A002),A; LD ($A000),A
            0xFA, 0x00, 0xA0, 0xE6, 0x01, 0x20, 0xF9, // LD A,($A000); AND 1; JR NZ,-7
            0xAF, 0xEA, 0x00, 0x40, 0x18, 0xFE // XOR A; LD ($4000),A; JR -2
        ];
        using var runner = new EmulationRunner();
        await runner.LoadAsync(TestRom.CreateMbc1(0xFC, 0, 2, program), "camera");
        static async Task<byte> CaptureAfterReset(EmulationRunner runner, Func<Task> setImage)
        {
            await runner.PauseAsync().ConfigureAwait(false);
            await setImage().ConfigureAwait(false);
            await runner.ResetAsync().ConfigureAwait(false);
            await runner.ResumeAsync().ConfigureAwait(false);
            await Until(() => runner.Status.Registers.PC == 0x173).ConfigureAwait(false);
            return (await runner.ReadMemoryAsync(0xA100, 1).ConfigureAwait(false)).Bytes[0];
        }

        var black = new byte[128 * 112];
        Assert.Equal(0xFF, await CaptureAfterReset(runner, () =>
        {
            var passed = runner.SetCameraImageAsync(black);
            Array.Fill(black, (byte)255); // Too late: already copied.
            return passed;
        }));
        Assert.Equal(0x00, await CaptureAfterReset(runner, () => runner.SetCameraImageAsync(null)));
        Assert.Throws<ArgumentException>(() => { _ = runner.SetCameraImageAsync(new byte[(128 * 112) + 1]); }); // At the call.
        await runner.LoadAsync(TestRom.Create(0x18, 0xFE), "plain");
        await runner.SetCameraImageAsync(black);
    }
}
