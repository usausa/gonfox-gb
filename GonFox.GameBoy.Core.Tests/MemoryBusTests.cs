namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Memory;

[Trait("Category", "Unit")]
public sealed class MemoryBusTests
{
    [Theory]
    [InlineData(0xC000, 0xE000)]
    [InlineData(0xC123, 0xE123)]
    [InlineData(0xDDFF, 0xFDFF)]
    public void EchoRamSharesStorageInBothDirections(ushort workAddress, ushort echoAddress)
    {
        var bus = new MemoryBus();
        bus.WriteByte(workAddress, 0x12);
        Assert.Equal(0x12, bus.ReadByte(echoAddress));
        bus.WriteByte(echoAddress, 0x34);
        Assert.Equal(0x34, bus.ReadByte(workAddress));
    }

    [Fact]
    public void WorkRamTailAndHighRamHaveDistinctBoundaries()
    {
        var bus = new MemoryBus();
        bus.WriteByte(0xDDFF, 0x11);
        bus.WriteByte(0xDE00, 0x22);
        bus.WriteByte(0xDFFF, 0x33);
        bus.WriteByte(0xFF80, 0x44);
        bus.WriteByte(0xFFFE, 0x55);
        Assert.Equal(0x11, bus.ReadByte(0xFDFF));
        Assert.Equal(0x22, bus.ReadByte(0xDE00));
        Assert.Equal(0x33, bus.ReadByte(0xDFFF));
        Assert.Equal(0x44, bus.ReadByte(0xFF80));
        Assert.Equal(0x55, bus.ReadByte(0xFFFE));
        Assert.Equal(0xFF, bus.ReadByte(0xFE00));
        Assert.Equal(0xFF, bus.ReadByte(0xFF7F));
        Assert.Equal(0xFF, bus.ReadByte(0xFFFF));
    }

    [Theory]
    [InlineData(0x0000)] // No cartridge attached.
    [InlineData(0x7FFF)]
    [InlineData(0x8000)]
    [InlineData(0x9FFF)]
    [InlineData(0xA000)]
    [InlineData(0xBFFF)]
    [InlineData(0xFE00)]
    [InlineData(0xFE9F)]
    [InlineData(0xFEA0)]
    [InlineData(0xFF00)]
    [InlineData(0xFF7F)]
    [InlineData(0xFFFF)]
    public void UnconnectedMemoryReadsFfAndIgnoresWrites(ushort address)
    {
        var bus = new MemoryBus();
        bus.WriteByte(address, 0x42);
        Assert.Equal(0xFF, bus.ReadByte(address));
        Assert.Equal(0xFF, bus.PeekByte(address));
    }

    [Fact]
    public void CartridgeReceivesConsoleAddressesForBothWindows()
    {
        var cartridge = new RecordingCartridge();
        var bus = new MemoryBus();
        bus.ConnectCartridge(cartridge);
        ushort[] addresses = [0x0000, 0x7FFF, 0xA000, 0xBFFF];
        foreach (var address in addresses)
        {
            Assert.Equal(0x42, bus.ReadByte(address));
            bus.WriteByte(address, 0x56);
        }

        Assert.Equal(addresses, cartridge.Reads);
        Assert.Equal(addresses.Select(address => (address, (byte)0x56)), cartridge.Writes);
    }

    [Fact]
    public void ResetClearsEveryInternalRamByteAndKeepsCartridge()
    {
        var bus = new MemoryBus();
        bus.ConnectCartridge(CartridgeLoader.Load(TestRom.Create()).Cartridge);
        for (var address = 0xC000; address <= 0xDFFF; address++)
        {
            bus.WriteByte((ushort)address, 0x12);
        }

        for (var address = 0xFF80; address <= 0xFFFE; address++)
        {
            bus.WriteByte((ushort)address, 0x34);
        }

        bus.Reset();

        for (var address = 0xC000; address <= 0xFDFF; address++)
        {
            Assert.Equal(0, bus.ReadByte((ushort)address));
        }

        for (var address = 0xFF80; address <= 0xFFFE; address++)
        {
            Assert.Equal(0, bus.ReadByte((ushort)address));
        }

        Assert.Equal(0xC3, bus.ReadByte(0x100));
    }

    private sealed class RecordingCartridge : ICartridge
    {
        internal List<ushort> Reads { get; } = [];
        internal List<(ushort Address, byte Value)> Writes { get; } = [];

        public byte Read(ushort address)
        {
            Reads.Add(address);
            return 0x42;
        }

        public void Write(ushort address, byte value) => Writes.Add((address, value));

        public void ResetController()
        {
        }
    }
}
