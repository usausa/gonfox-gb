namespace GonFox.GameBoy.Platform;

using GonFox.GameBoy.Core;
using GonFox.GameBoy.Core.Devices;

public sealed class HostLogicTests
{
    [Fact]
    public void FractionAndInstructionOvershootAreCarriedForward()
    {
        var pacer = new EmulationPacer();
        pacer.Accrue(0.5 / GameBoySystem.CyclesPerSecond);
        Assert.Equal(0, pacer.NextBudget);
        pacer.Accrue(0.75 / GameBoySystem.CyclesPerSecond);
        Assert.Equal(1, pacer.NextBudget);
        pacer.Consume(4);
        Assert.Equal(-2.75, pacer.PendingTCycles);
        pacer.Accrue(3.0 / GameBoySystem.CyclesPerSecond);
        Assert.Equal(0.25, pacer.PendingTCycles);
        Assert.Equal(0, pacer.NextBudget);
    }

    [Fact]
    public void LongDelaysAreCappedWithoutSkippingEmulatedCycles()
    {
        var pacer = new EmulationPacer();
        pacer.Accrue(2);
        Assert.Equal(4096, pacer.NextBudget);
        Assert.Equal(1.9, pacer.DroppedSeconds, 10);
        Assert.Equal(0.1 * GameBoySystem.CyclesPerSecond, pacer.PendingTCycles);
        pacer.Reset();
        Assert.Equal(0, pacer.PendingTCycles);
        Assert.Equal(0, pacer.DroppedSeconds);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    public void ObservationFrequencyDoesNotChangeAccumulatedBudget(int calls)
    {
        var pacer = new EmulationPacer();
        long consumed = 0;
        for (var i = 0; i < calls; i++)
        {
            pacer.Accrue(1.0 / calls);
            while (pacer.NextBudget > 0)
            {
                var cycles = (pacer.NextBudget + 3) / 4 * 4;
                consumed += cycles;
                pacer.Consume(cycles);
            }
        }
        Assert.InRange(consumed, GameBoySystem.CyclesPerSecond - 4L, GameBoySystem.CyclesPerSecond + 4L);
        Assert.Equal(0, pacer.DroppedSeconds);
    }

    [Fact]
    public void FpsCountsCompletedFramesAndClearsOnPause()
    {
        var counter = new FrameRateCounter();
        counter.Reset(10);
        for (var i = 1; i <= 4; i++)
        {
            counter.Advance((ulong)(10 + (i * 15)), 0.25);
        }

        Assert.Equal(60, counter.Fps);
        counter.Advance(70, 1);
        Assert.Equal(0, counter.Fps);
        counter.Reset(70);
        Assert.Equal(0, counter.Fps);
        counter.Advance(130, 1);
        Assert.Equal(60, counter.Fps);
    }

    [Fact]
    public void AliasesMouseAndTouchOwnTheirPressIndependently()
    {
        var input = new ButtonInputState();
        Assert.True(input.Set("key:Z", JoypadButton.A, true));
        Assert.False(input.Set("key:Z", JoypadButton.A, true));
        input.Set("key:J", JoypadButton.A, true);
        input.Set("mouse:A", JoypadButton.A, true);
        input.Set("key:Z", JoypadButton.A, false);
        input.Set("key:J", JoypadButton.A, false);
        Assert.Equal(16, input.Mask);
        input.Set("touch:1", JoypadButton.Left, true);
        input.Set("touch:2", JoypadButton.B, true);
        input.Set("mouse:A", JoypadButton.A, false);
        Assert.Equal(34, input.Mask);
        input.Clear();
        Assert.Equal(0, input.Mask);
        Assert.False(input.Set("touch:1", JoypadButton.Left, false));
    }
}
