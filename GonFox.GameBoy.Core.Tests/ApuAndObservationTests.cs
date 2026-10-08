namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Devices;

[Trait("Category", "Unit")]
public sealed class ApuAndObservationTests
{
    [Theory]
    [InlineData(0xFF10, 0x80)]
    [InlineData(0xFF11, 0x3F)]
    [InlineData(0xFF12, 0x00)]
    [InlineData(0xFF13, 0xFF)]
    [InlineData(0xFF14, 0xBF)]
    [InlineData(0xFF1A, 0x7F)]
    [InlineData(0xFF1C, 0x9F)]
    [InlineData(0xFF20, 0xFF)]
    [InlineData(0xFF24, 0x00)]
    [InlineData(0xFF25, 0x00)]
    [InlineData(0xFF15, 0xFF)]
    [InlineData(0xFF1F, 0xFF)]
    [InlineData(0xFF27, 0xFF)]
    public void SoundRegistersHaveIndividualReadMasks(ushort address, byte mask)
    {
        var m = new PeripheralTestMachine();
        m.Bus.WriteByte(address, 0);
        Assert.Equal(mask, m.Bus.ReadByte(address));
        m.Bus.WriteByte(address, 0xFF);
        Assert.Equal(0xFF, m.Bus.ReadByte(address));
    }

    [Fact]
    public void PowerOffClearsRegistersButPreservesWaveRam()
    {
        var m = new PeripheralTestMachine();
        m.Bus.WriteByte(0xFF30, 0x12);
        m.Bus.WriteByte(0xFF3F, 0x34);
        m.Bus.WriteByte(0xFF26, 0);
        m.Bus.WriteByte(0xFF12, 0xFF);
        m.Bus.WriteByte(0xFF11, 0xFF);
        Assert.Equal(0x70, m.Bus.ReadByte(0xFF26));
        Assert.Equal(0, m.Bus.ReadByte(0xFF12));
        Assert.Equal(0x3F, m.Bus.ReadByte(0xFF11));
        Assert.Equal(0x12, m.Bus.ReadByte(0xFF30));
        Assert.Equal(0x34, m.Bus.ReadByte(0xFF3F));
        m.Bus.WriteByte(0xFF26, 0xFF);
        Assert.Equal(0xF0, m.Bus.ReadByte(0xFF26)); // Powering off stopped every channel.
        m.Bus.WriteByte(0xFF12, 0x42);
        Assert.Equal(0x42, m.Bus.ReadByte(0xFF12));
    }

    [Fact]
    public void DebugReadOfIoDoesNotAdvanceOrAcknowledgeAnything()
    {
        var system = TestRom.Start(0x3E, 4, 0xE0, 0x0F);
        system.RunForTCycles(20);
        var before = system.GetDebugSnapshot();
        var io = new byte[0x80];
        system.CopyMemory(0xFF00, io);
        system.CopyMemory(0xFF00, io);
        Assert.Equal(before, system.GetDebugSnapshot());
        Assert.Equal(0xE4, io[0x0F]);
        Assert.Equal(before.TimerControl, io[7]);
        Assert.Equal(before.DividerCounter >> 8, io[4]);
    }

    [Fact]
    public void ResetClearsPeripheralStateAndRestoresBootProfile()
    {
        var system = TestRom.Start(0x3E, 0x81, 0xE0, 0x02, 0xE0, 0x07, 0xE0, 0x30);
        system.RunForTCycles(44);
        system.Joypad.SetButtonState(JoypadButton.Start, true);
        system.Reset();
        var io = new byte[0x40];
        system.CopyMemory(0xFF00, io);
        Assert.Equal(0xCF, io[0]); // Both rows selected, nothing pressed.
        Assert.Equal(0, io[1]);
        Assert.Equal(0x7E, io[2]);
        Assert.Equal(0xAB, io[4]);
        Assert.Equal(0, io[5]);
        Assert.Equal(0xF8, io[7]);
        Assert.Equal(0xE1, io[0x0F]);
        Assert.Equal(0, io[0x30]);
        Assert.Equal(0xF1, io[0x26]); // The boot sound leaves CH1 on.
        Assert.False(system.Serial.TryReadTransmittedByte(out _));
        Assert.Equal(0UL, system.TotalTCycles);
    }
}
