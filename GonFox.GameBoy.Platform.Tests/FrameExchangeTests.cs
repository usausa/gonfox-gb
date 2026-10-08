namespace GonFox.GameBoy.Platform;

using GonFox.GameBoy.Core;
using GonFox.GameBoy.Core.Video;

// Tests the frame exchange, the rule for drawing every frame and the rumble motor in the status.
public sealed class FrameExchangeTests
{
    private static void Write(FrameExchange exchange, ulong sequence)
    {
        var slot = exchange.Back;
        Array.Fill(slot.Pixels, (byte)sequence);
        slot.Info = new VideoFrameInfo(160, 144, 640, sequence, true);
        exchange.Publish();
    }

    [Fact]
    public void TheNewestImageWinsAndTheShownOneIsNotTakenAgain()
    {
        var exchange = new FrameExchange();
        Assert.Null(exchange.TryTake(ulong.MaxValue));
        Write(exchange, 1);
        var first = exchange.TryTake(ulong.MaxValue)!;
        Assert.Equal(1UL, first.Info.Sequence);
        Assert.All(first.Pixels, value => Assert.Equal(1, value));
        Assert.Null(exchange.TryTake(1)); // Already shown.
        Assert.Same(first, exchange.TryTake(0));
        Write(exchange, 2);
        Write(exchange, 3);
        Write(exchange, 4);
        var newest = exchange.TryTake(1)!;
        Assert.Equal(4UL, newest.Info.Sequence);
        Assert.All(newest.Pixels, value => Assert.Equal(4, value));
        Assert.Null(exchange.TryTake(4));
    }

    [Fact]
    public void TheWriterNeverTouchesTheSlotTheReaderHolds()
    {
        var exchange = new FrameExchange();
        Write(exchange, 7);
        var held = exchange.TryTake(ulong.MaxValue)!;
        for (ulong sequence = 8; sequence < 20; sequence++)
        {
            Write(exchange, sequence);
        }

        Assert.Equal(7UL, held.Info.Sequence);
        Assert.All(held.Pixels, value => Assert.Equal(7, value));
        Assert.Equal(19UL, exchange.TryTake(7)!.Info.Sequence);
    }

    // Each taken slot holds one whole image, sequences only grow, and the last image is found.
    [Fact]
    public async Task AWriterAndAReaderTogetherNeverTearAnImage()
    {
        var exchange = new FrameExchange();
        const ulong images = 200_000;
        var writer = Task.Run(() =>
        {
            for (ulong sequence = 1; sequence <= images; sequence++)
            {
                Write(exchange, sequence);
            }
        }, TestContext.Current.CancellationToken);
        ulong shown = 0;
        long taken = 0;
        while (shown < images)
        {
            var finished = writer.IsCompleted; // Read before taking.
            if (exchange.TryTake(shown) is not { } slot)
            {
                if (finished)
                {
                    break;
                }

                continue;
            }
            Assert.True(slot.Info.Sequence > shown);
            var expected = (byte)slot.Info.Sequence;
            Assert.True(slot.Pixels.AsSpan().IndexOfAnyExcept(expected) < 0, $"image {slot.Info.Sequence} is torn");
            shown = slot.Info.Sequence;
            taken++;
        }
        await writer.ConfigureAwait(true);
        Assert.Equal(images, shown);
        Assert.True(taken > 1);
    }

    [Theory]
    [InlineData(false, true, true, false, true)] // Running and visible.
    [InlineData(true, true, true, false, false)] // Closed.
    [InlineData(false, false, true, false, false)] // Minimized or hidden.
    [InlineData(false, true, false, false, false)] // Paused.
    [InlineData(false, true, true, true, false)] // Waiting in STOP.
    public void EveryFrameIsDrawnOnlyWhileImagesComeAndCanBeSeen(bool closed, bool visible, bool running, bool stopped, bool expected)
    {
        var status = new EmulationStatus(true, running, stopped, null, string.Empty, null, string.Empty, default, 0, 0);
        Assert.Equal(expected, DisplayPolicy.WantsEveryFrame(closed, visible, status));
    }

    // Reset stops the motor, and a board without one never reports it.
    [Fact]
    public async Task TheRumbleMotorShowsInTheStatus()
    {
        using var runner = new EmulationRunner();

        // Turns the motor on and loops (LD A,08; LD (4000),A; JR -2).
        await runner.LoadAsync(TestRom.CreateMbc5(0x1C, 1, 0, 0x3E, 0x08, 0xEA, 0x00, 0x40, 0x18, 0xFE), "rumble");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!runner.Status.Rumble && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(runner.Status.Rumble);
        await runner.PauseAsync();
        await runner.ResetAsync();
        Assert.False(runner.Status.Rumble);
        await runner.LoadAsync(TestRom.Create(0x3E, 0x08, 0xEA, 0x00, 0x40, 0x18, 0xFE), "plain");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(runner.Status.Rumble);
    }
}
