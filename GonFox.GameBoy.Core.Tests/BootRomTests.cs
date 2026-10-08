namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Memory;
using GonFox.GameBoy.Core.Video;

// Boot ROM mapping from power-on through the FF50 hand-over, with hand-written boot images.
[Trait("Category", "Unit")]
public sealed class BootRomTests
{
    // Builds a boot image that stores 42 at C000, runs the body and unmaps itself at 00FC.
    private static byte[] Image(params byte[] body)
    {
        var image = new byte[0x100];
        byte[] start = [0x31, 0xFE, 0xFF, 0x3E, 0x42, 0xEA, 0x00, 0xC0, .. body, 0xC3, 0xFC, 0x00];
        start.CopyTo(image, 0);
        image[0xFC] = 0x3E;
        image[0xFD] = 0x01;
        image[0xFE] = 0xE0;
        image[0xFF] = 0x50;
        return image;
    }

    private static GameBoySystem Start(byte[] bootRom, byte[]? cartridge = null)
    {
        var system = new GameBoySystem();
        system.UseBootRom(bootRom);
        system.InsertCartridge(CartridgeLoader.Load(cartridge ?? TestRom.Create(0x18, 0xFE)).Cartridge);
        return system;
    }

    private static byte Read(GameBoySystem system, ushort address)
    {
        var value = new byte[1];
        system.CopyMemory(address, value);
        return value[0];
    }

    private static void RunTo(GameBoySystem system, ushort pc)
    {
        for (var i = 0; i < 1000 && system.GetDebugSnapshot().PC != pc; i++)
        {
            system.StepInstruction();
        }

        Assert.Equal(pc, system.GetDebugSnapshot().PC);
    }

    [Fact]
    public void PowerOnStartsAtZeroWithTheBootRomOverTheCartridge()
    {
        var boot = Image();
        var system = Start(boot);
        var s = system.GetDebugSnapshot();
        Assert.Equal((0, 0, 0, 0, 0, 0), (s.AF, s.BC, s.DE, s.HL, s.SP, s.PC));
        Assert.Equal((0, 0x00, 0), (s.DividerCounter, s.LcdControl, s.PpuMode));
        Assert.True(system.UsesBootRom && system.IsBootRomMapped);
        Assert.Equal(boot[0], Read(system, 0x0000));
        Assert.Equal(boot[0xFF], Read(system, 0x00FF));
        Assert.Equal(0xC3, Read(system, 0x0100)); // The cartridge from 0100.
        Assert.Equal(0xFE, Read(system, 0xFF50));
        Assert.Equal(0x70, Read(system, 0xFF26)); // APU off.
        Assert.Equal(0xE0, Read(system, 0xFF0F));
        Assert.Equal(0xCF, Read(system, 0xFF00));
    }

    [Fact]
    public void WritingFf50BitZeroHandsOverToTheCartridgeAt0100()
    {
        var cartridge = TestRom.Create(0x18, 0xFE);
        var system = Start(Image(), cartridge);
        RunTo(system, 0x0100);
        Assert.False(system.IsBootRomMapped);
        var s = system.GetDebugSnapshot();
        Assert.Equal((0xFFFE, (byte)0x01), (s.SP, s.A));
        Assert.Equal(0x42, Read(system, 0xC000));
        Assert.Equal(cartridge[0], Read(system, 0x0000));
        Assert.Equal(cartridge[0xFF], Read(system, 0x00FF));
        Assert.Equal(0xFF, Read(system, 0xFF50));
        system.StepInstruction(); // The cartridge's JP 0150.
        Assert.Equal(0x0150, system.GetDebugSnapshot().PC);
    }

    [Theory]
    [InlineData(0x00, true)]
    [InlineData(0xFE, true)]
    [InlineData(0x01, false)]
    [InlineData(0xFF, false)]
    public void OnlyBitZeroOfFf50Unmaps(byte value, bool mapped)
    {
        // Builds a boot image that writes the value to FF50 and spins at 000C.
        var boot = new byte[0x100];
        byte[] code = [0x3E, value, 0xE0, 0x50, 0x18, 0xFE];
        code.CopyTo(boot, 0x08);
        boot[0] = 0xC3;
        boot[1] = 0x08;
        boot[2] = 0x00;
        var system = Start(boot);
        RunTo(system, 0x000C);
        Assert.Equal(mapped, system.IsBootRomMapped);
    }

    [Fact]
    public void UnmappingIsOneWayUntilThePowerIsCycled()
    {
        // The cartridge writes 00 and 01 to FF50; neither maps the boot ROM back.
        var system = Start(Image(), TestRom.Create(0x3E, 0x00, 0xE0, 0x50, 0x3E, 0x01, 0xE0, 0x50, 0x18, 0xFE));
        RunTo(system, 0x0158);
        Assert.False(system.IsBootRomMapped);
        Assert.Equal(0xFF, Read(system, 0xFF50));
        system.Reset();
        Assert.True(system.IsBootRomMapped);
        Assert.Equal(0x0000, system.GetDebugSnapshot().PC);
        system.UseBootBypass();
        system.Reset();
        Assert.False(system.IsBootRomMapped);
        Assert.Equal(0x0100, system.GetDebugSnapshot().PC);
    }

    [Fact]
    public void WritesWhileMappedReachTheCartridge()
    {
        // The boot ROM selects MBC1 ROM bank 2 while it is still mapped.
        var system = Start(Image(0x3E, 0x02, 0xEA, 0x00, 0x20), TestRom.CreateMbc1());
        RunTo(system, 0x00FC);
        Assert.True(system.IsBootRomMapped);
        Assert.Equal(2, system.GetDebugSnapshot().RomBank1);
    }

    [Fact]
    public void OamDmaFromPageZeroCopiesTheBootRom()
    {
        var bus = new MemoryBus();
        var ppu = new Ppu(new Interrupts(), new VideoOutput());
        ppu.Reset();
        ppu.WriteRegister(0xFF40, 0); // LCD off: OAM takes every byte.
        bus.ConnectPpu(ppu);
        var dma = new OamDma(bus, ppu);
        bus.ConnectDma(dma);
        bus.ConnectCartridge(CartridgeLoader.Load(TestRom.Create()).Cartridge);
        var boot = Image();
        bus.MapBootRom(boot);
        bus.WriteByte(0xFF46, 0x00);
        for (var i = 0; i < 4 * 162; i++)
        {
            dma.Tick();
        }

        var oam = new byte[160];
        for (var i = 0; i < oam.Length; i++)
        {
            oam[i] = bus.PeekByte((ushort)(0xFE00 + i));
        }

        Assert.Equal(boot.AsSpan(0, 160).ToArray(), oam);
    }

    [Fact]
    public void StateCarriesTheMappedBootRom()
    {
        var cartridge = TestRom.Create(0x18, 0xFE);
        var system = Start(Image(), cartridge);
        system.StepInstruction();
        system.StepInstruction(); // Mid-boot.
        var saved = system.CaptureState();
        Assert.Equal("BootRom-v1", saved.BootProfile);
        var other = new GameBoySystem(); // No boot ROM of its own.
        other.InsertCartridge(CartridgeLoader.Load(cartridge).Cartridge);
        other.RestoreState(GameBoyState.Deserialize(saved.Serialize()));
        Assert.True(other.IsBootRomMapped);
        RunTo(system, 0x0100);
        RunTo(other, 0x0100);
        StateTests.EqualState(system.CaptureState() with { BootProfile = string.Empty }, other.CaptureState() with { BootProfile = string.Empty });
        Assert.Equal("BootBypass-v2", other.CaptureState().BootProfile);
    }

    [Fact]
    public void RestoringAStateFromAfterTheHandOverUnmapsTheBootRom()
    {
        var cartridge = TestRom.Create(0x18, 0xFE);
        var system = Start(Image(), cartridge);
        RunTo(system, 0x0100);
        var after = system.CaptureState();
        system.Reset();
        Assert.True(system.IsBootRomMapped);
        system.RestoreState(after);
        Assert.False(system.IsBootRomMapped);
        Assert.Equal(cartridge[0], Read(system, 0x0000));
        Assert.Equal(0xFF, Read(system, 0xFF50));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    [InlineData(257)]
    public void BootRomMustBe256Bytes(int length)
    {
        var system = new GameBoySystem();
        Assert.Throws<ArgumentException>(() => system.UseBootRom(new byte[length]));
        Assert.False(system.UsesBootRom);
    }
}
