namespace GonFox.GameBoy.Core;

// Serial transfers whose clock toggles at each fall of DIV counter bit 7, every 256 T.
[Trait("Category", "Unit")]
public sealed class SerialTests
{
    [Fact]
    public void SerialShiftsAt512TCyclesAndCompletesAt4096()
    {
        var m = new PeripheralTestMachine();
        m.Serial.WriteData(0x42);
        Assert.False(m.Serial.TryReadTransmittedByte(out _));
        m.Serial.WriteControl(0x83); // DMG ignores CGB fast bit.
        Assert.Equal(0xFF, m.Serial.Control);
        m.TickTimer(511);
        Assert.Equal(0x42, m.Serial.Data);
        m.TickTimer(1);
        Assert.Equal(0x85, m.Serial.Data);
        m.TickTimer(4095 - 512);
        Assert.False(m.Serial.TryReadTransmittedByte(out _));
        Assert.Equal(0xE0, m.Interrupts.Flags);
        m.TickTimer(1);
        Assert.Equal(0xFF, m.Serial.Data);
        Assert.Equal(0x7F, m.Serial.Control);
        Assert.Equal(0xE8, m.Interrupts.Flags);
        Assert.True(m.Serial.TryReadTransmittedByte(out var sent));
        Assert.Equal(0x42, sent);
        Assert.False(m.Serial.TryReadTransmittedByte(out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(300)]
    [InlineData(511)]
    [InlineData(4000)]
    public void CompletionFollowsTheCounterPhaseRatherThanTheWrite(int phase)
    {
        var m = new PeripheralTestMachine();
        m.TickTimer(phase);
        m.Serial.WriteData(0x42);
        m.Serial.WriteControl(0x81);
        var end = 256 - (phase % 256) + 3840;
        m.TickTimer(end - 1);
        Assert.Equal(0xE0, m.Interrupts.Flags);
        Assert.Equal(0xFF, m.Serial.Control); // Still running.
        m.TickTimer(1);
        Assert.Equal(0xE8, m.Interrupts.Flags);
        Assert.Equal(0x7F, m.Serial.Control);
        Assert.True(m.Serial.TryReadTransmittedByte(out var sent));
        Assert.Equal(0x42, sent);
    }

    [Fact]
    public void ADividerResetIsACounterEdge()
    {
        var m = new PeripheralTestMachine();
        m.Serial.WriteData(0x80);
        m.Serial.WriteControl(0x81); // Clock taken low.
        m.TickTimer(384); // Clock high, bit 7 set again.
        Assert.Equal(0x80, m.Serial.Data);
        m.Timer.WriteRegister(0xFF04, 0); // Bit 7 falls: first shift.
        Assert.Equal(0x01, m.Serial.Data);
        m.TickTimer(255);
        Assert.Equal(0x01, m.Serial.Data);
        m.TickTimer(1 + 256); // Second bit at 512.
        Assert.Equal(0x03, m.Serial.Data);
    }

    [Fact]
    public void RewritingScWhileTheClockIsHighShiftsTheRunningTransferOnce()
    {
        var m = new PeripheralTestMachine();
        m.Serial.WriteData(0x80);
        m.Serial.WriteControl(0x81);
        m.TickTimer(256); // Clock high, no shift yet.
        Assert.Equal(0x80, m.Serial.Data);
        m.Serial.WriteControl(0x81); // Takes the clock low: one bit.
        Assert.Equal(0x01, m.Serial.Data);
        m.TickTimer(3583); // Seven more bits.
        Assert.Equal(0xE0, m.Interrupts.Flags);
        m.TickTimer(1);
        Assert.Equal(0xE8, m.Interrupts.Flags);
        Assert.True(m.Serial.TryReadTransmittedByte(out var sent));
        Assert.Equal(0x80, sent);
    }

    [Fact]
    public void ExternalClockAndAbortDoNotCompleteOrRequestInterrupts()
    {
        var m = new PeripheralTestMachine();
        m.Serial.WriteData(0x42);
        m.Serial.WriteControl(0x80);
        m.TickTimer(10000);
        Assert.Equal(0x42, m.Serial.Data);
        Assert.Equal(0xFE, m.Serial.Control);
        m.Serial.WriteControl(0x81);
        m.TickTimer(512);
        m.Serial.WriteControl(0);
        m.TickTimer(10000);
        Assert.False(m.Serial.TryReadTransmittedByte(out _));
        Assert.Equal(0xE0, m.Interrupts.Flags);
    }

    [Fact]
    public void RestartDropsIncompleteTransferAndSbWritesAffectRemainingBits()
    {
        var m = new PeripheralTestMachine();
        m.Serial.WriteData(0xFF);
        m.Serial.WriteControl(0x81);
        m.TickTimer(512);
        m.Serial.WriteData(0);
        m.Serial.WriteControl(0x81); // The clock is already low here.
        m.TickTimer(4 * 512);
        m.Serial.WriteData(0xF0);
        m.TickTimer(4 * 512);
        Assert.True(m.Serial.TryReadTransmittedByte(out var sent));
        Assert.Equal(0x0F, sent); // Four zero bits, then four one bits.
        Assert.False(m.Serial.TryReadTransmittedByte(out _));
    }

    [Fact]
    public void ObservationQueueIsBoundedAndResetClearsIt()
    {
        var m = new PeripheralTestMachine();
        for (var i = 0; i < 257; i++)
        {
            m.Serial.WriteData((byte)i);
            m.Serial.WriteControl(0x81);
            m.TickTimer(4096);
        }
        Assert.Equal(1, m.Serial.DroppedByteCount);
        Assert.True(m.Serial.TryReadTransmittedByte(out var oldest));
        Assert.Equal(1, oldest);
        m.Serial.Reset();
        Assert.False(m.Serial.TryReadTransmittedByte(out _));
        Assert.Equal(0, m.Serial.DroppedByteCount);
        Assert.Equal(0x7E, m.Serial.Control);
    }

    [Fact]
    public void CpuRomPollsRealScUntilTheSerialTransferCompletes()
    {
        var system = TestRom.Start(
            0x3E, 0x42, 0xE0, 0x01, 0x3E, 0x81, 0xE0, 0x02,
            0xF0, 0x02, 0xCB, 0x7F, 0x20, 0xFA, 0x76);
        system.RunForTCycles(4200);
        Assert.True(system.IsHalted);
        Assert.True(system.Serial.TryReadTransmittedByte(out var sent));
        Assert.Equal(0x42, sent);
        Assert.False(system.Serial.TryReadTransmittedByte(out _));
        var io = new byte[2];
        system.CopyMemory(0xFF01, io);
        Assert.Equal(new byte[] { 0xFF, 0x7F }, io);
        Assert.Equal(0xE9, system.GetDebugSnapshot().InterruptFlags);
        var saved = system.CaptureState(); // Bit count back to 0.
        Assert.Equal(0, saved.Serial.Bits);
        system.RestoreState(saved);
    }
}
