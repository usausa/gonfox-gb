namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Memory;
using GonFox.GameBoy.Core.Video;

[Trait("Category", "Unit")]
public sealed class OamDmaTests
{
    private readonly MemoryBus bus = new();
    private readonly VideoOutput video = new();
    private readonly Ppu ppu;
    private readonly OamDma dma;

    public OamDmaTests()
    {
        ppu = new(new Interrupts(), video);
        ppu.Reset();
        ppu.WriteRegister(0xFF40, 0);
        bus.ConnectPpu(ppu);
        dma = new(bus, ppu);
        bus.ConnectDma(dma);
        for (var i = 0; i < 160; i++)
        {
            bus.WriteByte((ushort)(0xC000 + i), (byte)(i + 1));
            bus.WriteByte((ushort)(0xD000 + i), (byte)(255 - i));
        }
    }

    private void Tick(int count)
    {
        for (var i = 0; i < count; i++)
        {
            dma.Tick();
        }
    }

    [Fact]
    public void TransferHasStartupThenOneByteEveryFourTicksAndStopsAfter160()
    {
        bus.WriteByte(0xFF46, 0xC0);
        Tick(4);
        Assert.False(dma.Active);
        Assert.Equal(0, bus.ReadByte(0xFE00));
        Tick(4);
        Assert.True(dma.Active);
        Assert.Equal(0, bus.PeekByte(0xFE00));
        Tick(3);
        Assert.Equal(0, bus.PeekByte(0xFE00));
        Tick(1);
        Assert.Equal(1, bus.PeekByte(0xFE00));
        Assert.Equal(0, bus.PeekByte(0xFE01));
        Tick(4 * 158);
        Assert.True(dma.Active);
        Assert.Equal(159, bus.PeekByte(0xFE9E));
        Tick(3);
        Assert.Equal(0, bus.PeekByte(0xFE9F));
        Tick(1);
        Assert.False(dma.Active);
        Assert.Equal(160, bus.ReadByte(0xFE9F));
        bus.WriteByte(0xFE00, 0x44);
        Tick(8);
        Assert.Equal(0x44, bus.ReadByte(0xFE00));
    }

    // On the cartridge/WRAM bus the CPU reads the DMA's byte; its writes go to OAM, ANDed with it.
    [Fact]
    public void DmaFromWorkRamSharesTheCartridgeBusWithTheCpu()
    {
        bus.ConnectCartridge(CartridgeLoader.Load(TestRom.Create()).Cartridge);
        ppu.WriteMemory(0x8000, 0x66);
        bus.WriteByte(0xC000, 0x0F);
        bus.WriteByte(0xFF46, 0xC0);
        Tick(8);
        foreach (var address in new ushort[] { 0x0100, 0x7FFF, 0xA000, 0xC123, 0xDFFF, 0xE000, 0xFDFF })
        {
            Assert.Equal(0x0F, bus.ReadByte(address));
        }

        Assert.Equal(0xFF, bus.ReadByte(0xFE00));
        Assert.Equal(0xFF, bus.ReadByte(0xFEA0));
        Assert.Equal(0x66, bus.ReadByte(0x8000));
        Assert.Equal(0xC0, bus.ReadByte(0xFF46));
        bus.WriteByte(0xC123, 0x3C);
        bus.WriteByte(0xFE00, 0x55);
        bus.WriteByte(0x8001, 0x77);
        bus.WriteByte(0xFF47, 0xE4);
        bus.WriteByte(0xFF80, 0x12);
        bus.WriteByte(0xFFFE, 0x34);
        Assert.Equal(0, bus.PeekByte(0xC123));
        Assert.Equal(0, bus.PeekByte(0xFE00));
        Assert.Equal(0x77, bus.PeekByte(0x8001));
        Assert.Equal(0xE4, bus.ReadByte(0xFF47));
        Assert.Equal(0x12, bus.ReadByte(0xFF80));
        Assert.Equal(0x34, bus.ReadByte(0xFFFE));
        Tick(4);
        Assert.Equal(0x0F & 0x3C, bus.PeekByte(0xFE00));
        Assert.Equal(2, bus.ReadByte(0x4000)); // The next byte, C001.
        Tick(4);
        Assert.Equal(2, bus.PeekByte(0xFE01));
    }

    // On the VRAM bus a CPU write replaces the DMA's byte; the cartridge bus stays free.
    [Fact]
    public void DmaFromVramSharesTheVideoBusWithTheCpu()
    {
        bus.ConnectCartridge(CartridgeLoader.Load(TestRom.Create()).Cartridge);
        ppu.WriteMemory(0x8000, 0x66);
        bus.WriteByte(0xFF46, 0x80);
        Tick(8);
        Assert.Equal(0x66, bus.ReadByte(0x9FFF));
        Assert.Equal(0xFF, bus.ReadByte(0xFE00));
        Assert.Equal(0xC3, bus.ReadByte(0x0100));
        Assert.Equal(1, bus.ReadByte(0xC000));
        Assert.Equal(1, bus.ReadByte(0xE000));
        bus.WriteByte(0xC000, 0x44);
        bus.WriteByte(0x9000, 0x3C);
        Assert.Equal(0x44, bus.ReadByte(0xC000));
        Assert.Equal(0, bus.PeekByte(0x9000));
        Tick(4);
        Assert.Equal(0x3C, bus.PeekByte(0xFE00));
        Tick(636);
        Assert.False(dma.Active);
        Assert.Equal(0x3C, bus.ReadByte(0xFE00));
        Assert.Equal(0x66, bus.PeekByte(0x8000));
    }

    // A conflicting write replaces cartridge and VRAM bytes but is ANDed with WRAM and its mirrors.
    [Theory]
    [InlineData(0x00, 0x3C)]
    [InlineData(0x80, 0x3C)]
    [InlineData(0xA0, 0x3C)]
    [InlineData(0xC0, 0x0C)]
    [InlineData(0xE0, 0x0C)]
    [InlineData(0xFE, 0x0C)]
    [InlineData(0xFF, 0x0C)]
    public void ConflictingWriteReplacesOrAndsByWhatTheTransferReads(int page, int stored)
    {
        var image = TestRom.CreateMbc1(3, 1, 2);
        image[0x0000] = 0x0F;
        bus.ConnectCartridge(CartridgeLoader.Load(image).Cartridge);
        bus.WriteByte(0x0000, 0x0A); // Cartridge RAM on.
        bus.WriteByte((ushort)((page >= 0xE0 ? page - 0x20 : page) << 8), 0x0F); // ROM keeps its own 0F.
        bus.WriteByte(0xFF46, (byte)page);
        Tick(8);
        bus.WriteByte(page is >= 0x80 and <= 0x9F ? (ushort)0x9123 : (ushort)0xC123, 0x3C);
        Tick(4);
        Assert.Equal(stored, bus.PeekByte(0xFE00));
    }

    // The register write's M-cycle and the one after it are still free of conflicts.
    [Fact]
    public void ConflictsLastFromTheFirstByteToTheLast()
    {
        bus.ConnectCartridge(CartridgeLoader.Load(TestRom.Create()).Cartridge);
        bus.WriteByte(0xFF46, 0xC0);
        Tick(4);
        Assert.Equal(0xC3, bus.ReadByte(0x0100));
        Tick(4);
        Assert.Equal(1, bus.ReadByte(0x0100));
        Tick(4 * 159);
        Assert.Equal(160, bus.ReadByte(0x0100));
        Tick(4);
        Assert.False(dma.Active);
        Assert.Equal(0xC3, bus.ReadByte(0x0100));
    }

    // During a restart's startup the old transfer goes on from the new page, then it starts over at 0.
    [Fact]
    public void RestartContinuesFromTheNewPageThenStartsOver()
    {
        bus.WriteByte(0xFF46, 0xC0);
        Tick(8 + 40);
        bus.WriteByte(0xFF46, 0xD0);
        Tick(8);
        Assert.True(dma.Active);
        Assert.Equal(10, bus.PeekByte(0xFE09));
        Assert.Equal(1, bus.PeekByte(0xFE00));
        Assert.Equal(245, bus.PeekByte(0xFE0A));
        Assert.Equal(244, bus.PeekByte(0xFE0B));
        Tick(4);
        Assert.Equal(255, bus.PeekByte(0xFE00));
        Assert.Equal(2, bus.PeekByte(0xFE01));
        Tick(636);
        Assert.False(dma.Active);
        for (var i = 0; i < 160; i++)
        {
            Assert.Equal(255 - i, bus.ReadByte((ushort)(0xFE00 + i)));
        }
    }

    // Restarting from WRAM instead of VRAM moves the conflict to the cartridge/WRAM bus at once.
    [Fact]
    public void RestartMovesTheConflictToTheNewBusAtOnce()
    {
        bus.ConnectCartridge(CartridgeLoader.Load(TestRom.Create()).Cartridge);
        ppu.WriteMemory(0x8000, 0x66);
        bus.WriteByte(0xFF46, 0x80);
        Tick(8 + 40);
        Assert.Equal(0xC3, bus.ReadByte(0x0100));
        Assert.Equal(0, bus.ReadByte(0x8000)); // 800A, transferred now.
        bus.WriteByte(0xFF46, 0xC0);
        Assert.Equal(11, bus.ReadByte(0x0100)); // C00A: index 10 from the new page.
        Assert.Equal(0x66, bus.ReadByte(0x8000));
    }

    // Resetting an idle DMA in the boot state's mode 2 leaves the OAM scan where it is.
    [Fact]
    public void ResettingAnIdleTransferLeavesTheOamScan()
    {
        var system = TestRom.Start();
        Assert.Equal(0, system.CaptureState().Ppu.ScanIndex);
    }

    // The transfer pauses while held, as it is during HALT.
    [Fact]
    public void HeldTransferWaits()
    {
        bus.WriteByte(0xFF46, 0xC0);
        Tick(8 + 4);
        dma.Held = true;
        Tick(40);
        Assert.True(dma.Active);
        Assert.Equal(1, bus.PeekByte(0xFE00));
        Assert.Equal(0, bus.PeekByte(0xFE01));
        dma.Held = false;
        Tick(4);
        Assert.Equal(2, bus.PeekByte(0xFE01));
    }

    [Theory]
    [InlineData(0x00, 0x0000)]
    [InlineData(0x7F, 0x7F00)]
    [InlineData(0x80, 0x8000)]
    [InlineData(0x9F, 0x9F00)]
    [InlineData(0xDF, 0xDF00)]
    [InlineData(0xE0, 0xC000)]
    [InlineData(0xFE, 0xDE00)]
    [InlineData(0xFF, 0xDF00)]
    public void DmgSourceRoutingIncludesVramCartridgeAndHighWramMirrors(int page, int storage)
    {
        var image = TestRom.Create();
        for (var i = 0; i < 160; i++)
        {
            if (storage < 0x8000)
            {
                image[storage + i] = (byte)(i ^ 0x5A);
            }
            else
            {
                bus.WriteByte((ushort)(storage + i), (byte)(i ^ 0x5A));
            }
        }
        bus.ConnectCartridge(CartridgeLoader.Load(image).Cartridge);
        bus.WriteByte(0xFF46, (byte)page);
        Tick(648);
        for (var i = 0; i < 160; i++)
        {
            Assert.Equal(i ^ 0x5A, bus.ReadByte((ushort)(0xFE00 + i)));
        }
    }

    [Fact]
    public void UnavailableCartridgeRamReturnsFfAndResetCancelsPendingAndActiveTransfers()
    {
        bus.ConnectCartridge(CartridgeLoader.Load(TestRom.Create()).Cartridge);
        bus.WriteByte(0xFF46, 0xA0);
        Tick(648);
        Assert.Equal(0xFF, bus.ReadByte(0xFE9F));
        bus.WriteByte(0xFF46, 0xC0);
        Tick(12);
        bus.WriteByte(0xFF46, 0xD0);
        dma.Reset();
        Tick(1000);
        Assert.False(dma.Active);
        Assert.Equal(0xFF, dma.Register);
        Assert.Equal(1, bus.ReadByte(0xFE00));
        Assert.Equal(0xFF, bus.ReadByte(0xFE01));
    }

    [Fact]
    public void ActiveDmaHidesSpritesAtOamSelectionButBypassesCpuOamLock()
    {
        for (var i = 0; i < 16; i += 2)
        {
            bus.WriteByte((ushort)(0x8000 + i), 0xFF);
        }

        bus.WriteByte(0xFF48, 0xE4);
        bus.WriteByte(0xC000, 16);
        bus.WriteByte(0xC001, 8);
        bus.WriteByte(0xC002, 0);
        bus.WriteByte(0xC003, 0);
        bus.WriteByte(0xFF40, 0x92);

        // Skips the first frame, which is not shown, to start the transfer in line 0's OAM scan.
        for (var i = 0; i < 70224; i++)
        {
            ppu.Tick();
        }

        bus.WriteByte(0xFF46, 0xC0);
        for (var i = 0; i < 80; i++)
        {
            dma.Tick();
            ppu.Tick();
        }
        Assert.Equal(0xFF, bus.ReadByte(0xFE00));
        Assert.Equal(16, bus.PeekByte(0xFE00));
        Tick(568);
        Assert.False(dma.Active);
        Assert.Equal(16, bus.PeekByte(0xFE00));
        for (var i = 80; i < 456 * 144; i++)
        {
            ppu.Tick();
        }

        var pixels = new byte[VideoOutput.BufferSize];
        video.CopyLatestFrame(pixels);
        Assert.Equal(255, pixels[0]);
        Assert.Equal(170, pixels[640]);
    }

    // While DMA holds OAM the scan sees every entry as off screen; entries scanned outside that span show.
    [Theory]
    [InlineData(true, 0, 3)]
    [InlineData(true, 4, 5)]
    [InlineData(true, 8, 7)]
    [InlineData(false, 272, 7)]
    [InlineData(false, 276, 5)]
    [InlineData(false, 280, 3)]
    public void TransferHidesTheEntriesTheScanReachesMeanwhile(bool start, int writeDot, int visible)
    {
        for (var i = 0; i < 16; i += 2)
        {
            bus.WriteByte((ushort)(0x8000 + i), 0xFF);
        }

        bus.WriteByte(0xFF48, 0xE4);
        for (var i = 0; i < 160; i++)
        {
            int field = i & 3, entry = i >> 2;
            var value = entry >= 10 ? (byte)0 : field == 0 ? (byte)16 : field == 1 ? (byte)(8 + (8 * entry)) : (byte)0;
            bus.WriteByte((ushort)(0xC000 + i), value);
            ppu.WriteMemory((ushort)(0xFE00 + i), value);
        }
        bus.WriteByte(0xFF40, 0x92);
        for (var i = 0; i < 70224; i++)
        {
            Step(); // The first frame is not shown.
        }

        while (ppu.Ly != 1 || ppu.Dot != writeDot)
        {
            Step();
        }

        bus.WriteByte(0xFF46, 0xC0);
        for (var i = 0; i < 456 * 144; i++)
        {
            Step();
        }

        var pixels = new byte[VideoOutput.BufferSize];
        video.CopyLatestFrame(pixels);
        int line = start ? 1 : 3, first = start ? 0 : 10 - visible;
        for (var i = 0; i < 10; i++)
        {
            Assert.True((i >= first && i < first + visible ? 170 : 255) == pixels[((line * 160) + (8 * i)) * 4], $"object {i}");
        }
    }

    private void Step()
    {
        dma.Tick();
        ppu.Tick();
    }

    // HALT right after starting a transfer: one more byte moves, then the rest waits for the wake-up.
    [Fact]
    public void HaltHoldsTheTransferFromItsSecondWaitingCycleUntilTheCpuWakes()
    {
        var image = TestRom.Create(0x21, 0x00, 0xC0, 0x3E, 0x01, 0x06, 0xA0, 0x22, 0x3C, 0x05, 0x20, 0xFB, // C000-C09F = 1..160
            0xF3, 0x3E, 0x10, 0xE0, 0xFF, 0xE0, 0x00, 0xAF, 0xE0, 0x0F, // IE and P1: buttons; IF clear
                                                                        // Copies LDH (46),A; HALT; JR -2 to FF80 and jumps there with A = C0.
            0x21, 0x80, 0xFF, 0x3E, 0xE0, 0x22, 0x3E, 0x46, 0x22, 0x3E, 0x76, 0x22, 0x3E, 0x18, 0x22, 0x3E, 0xFE, 0x22,
            0x3E, 0xC0, 0xC3, 0x80, 0xFF);
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        while (!system.IsHalted)
        {
            system.StepInstruction();
        }

        system.RunForTCycles(4_000);
        var oam = new byte[160];
        system.CopyMemory(0xFE00, oam);
        Assert.Equal(1, oam[0]);
        Assert.Equal(0, oam[1]);
        system.Joypad.SetButtonState(JoypadButton.A, true);
        system.RunForTCycles(1_000);
        Assert.False(system.IsHalted);
        system.CopyMemory(0xFE00, oam);
        for (var i = 0; i < 160; i++)
        {
            Assert.Equal(i + 1, oam[i]);
        }
    }

    // The hold is not saved: a restored halted CPU holds the transfer again and finishes it after waking.
    [Fact]
    public void RestoredHaltKeepsHoldingTheTransfer()
    {
        var image = TestRom.Create(0x21, 0x00, 0xC0, 0x3E, 0x01, 0x06, 0xA0, 0x22, 0x3C, 0x05, 0x20, 0xFB,
            0xF3, 0x3E, 0x10, 0xE0, 0xFF, 0xE0, 0x00, 0xAF, 0xE0, 0x0F,
            0x21, 0x80, 0xFF, 0x3E, 0xE0, 0x22, 0x3E, 0x46, 0x22, 0x3E, 0x76, 0x22, 0x3E, 0x18, 0x22, 0x3E, 0xFE, 0x22,
            0x3E, 0xC0, 0xC3, 0x80, 0xFF);
        var first = new GameBoySystem();
        first.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        while (!first.IsHalted)
        {
            first.StepInstruction();
        }

        first.RunForTCycles(400);
        var state = first.CaptureState();
        var second = new GameBoySystem();
        second.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        second.RestoreState(state);
        second.RunForTCycles(4_000);
        var oam = new byte[160];
        second.CopyMemory(0xFE00, oam);
        Assert.Equal(1, oam[0]);
        Assert.Equal(0, oam[1]);
        second.Joypad.SetButtonState(JoypadButton.A, true);
        second.RunForTCycles(1_000);
        second.CopyMemory(0xFE00, oam);
        for (var i = 0; i < 160; i++)
        {
            Assert.Equal(i + 1, oam[i]);
        }
    }

    [Fact]
    public void CpuExecutesHramDmaWaitRoutineAndReturnsToRom()
    {
        var image = TestRom.Create(0xF3, 0x3E, 0, 0xE0, 0x40,
            0x21, 0, 0xC0, 0x06, 0xA0, 0x78, 0x22, 0x05, 0x20, 0xFB,
            0x21, 0, 3, 0x11, 0x80, 0xFF, 0x06, 12, 0x2A, 0x12, 0x13, 0x05, 0x20, 0xFA,
            0xC3, 0x80, 0xFF);
        new byte[] { 0x3E, 0xC0, 0xE0, 0x46, 0x3E, 40, 0x3D, 0x20, 0xFD, 0xC3, 0, 4 }.CopyTo(image, 0x300);
        image[0x400] = 0x76;
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        system.RunForTCycles(10_000);
        Assert.True(system.IsHalted);
        Assert.Equal(0x401, system.GetDebugSnapshot().PC);
        var oam = new byte[160];
        system.CopyMemory(0xFE00, oam);
        for (var i = 0; i < 160; i++)
        {
            Assert.Equal(160 - i, oam[i]);
        }
    }
}
