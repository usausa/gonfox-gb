namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Cpu;
using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Video;

[Trait("Category", "Unit")]
public sealed class StateTests
{
    internal static void EqualState(GameBoyState expected, GameBoyState actual)
    {
        Assert.Equal(expected.FormatVersion, actual.FormatVersion);
        Assert.Equal(expected.Model, actual.Model);
        Assert.Equal(expected.BootProfile, actual.BootProfile);
        Assert.Equal(expected.RomSha256, actual.RomSha256);
        Assert.Equal(expected.CartridgeType, actual.CartridgeType);
        Assert.Equal(expected.TotalTCycles, actual.TotalTCycles);
        Assert.Equal(expected.Cpu, actual.Cpu);
        Assert.Equal(expected.Interrupts, actual.Interrupts);
        Assert.Equal(expected.Timer, actual.Timer);
        Assert.Equal(expected.Joypad, actual.Joypad);
        Assert.Equal(expected.Dma, actual.Dma);
        Assert.Equal(expected.Memory.WorkRam, actual.Memory.WorkRam);
        Assert.Equal(expected.Memory.HighRam, actual.Memory.HighRam);
        Assert.Equal(expected.Cartridge.Ram, actual.Cartridge.Ram);
        CameraState? camera = expected.Cartridge.Camera, actualCamera = actual.Cartridge.Camera;
        Assert.Equal(camera?.Registers, actualCamera?.Registers);
        Assert.Equal(camera?.Picture, actualCamera?.Picture);
        Assert.Equal(expected.Cartridge, actual.Cartridge with
        {
            Ram = expected.Cartridge.Ram,
            Camera = camera is not null && actualCamera is not null ? actualCamera with { Registers = camera.Registers, Picture = camera.Picture } : actualCamera
        });
        Assert.Equal(expected.Serial.Completed, actual.Serial.Completed);
        Assert.Equal(expected.Serial, actual.Serial with { Completed = expected.Serial.Completed });
        Assert.Equal(expected.Apu.Registers, actual.Apu.Registers);
        Assert.Equal(expected.Apu.WaveRam, actual.Apu.WaveRam);
        Assert.Equal(expected.Apu, actual.Apu with { Registers = expected.Apu.Registers, WaveRam = expected.Apu.WaveRam });
        Assert.Equal(expected.Audio.Queued, actual.Audio.Queued);
        Assert.Equal(expected.Audio.Dropped, actual.Audio.Dropped);
        Assert.Equal(expected.Ppu.Vram, actual.Ppu.Vram);
        Assert.Equal(expected.Ppu.Oam, actual.Ppu.Oam);
        Assert.Equal(expected.Ppu.Sprites, actual.Ppu.Sprites);
        Assert.Equal(expected.Ppu.LineShades, actual.Ppu.LineShades);
        Assert.Equal(expected.Ppu, actual.Ppu with { Vram = expected.Ppu.Vram, Oam = expected.Ppu.Oam, Sprites = expected.Ppu.Sprites, LineShades = expected.Ppu.LineShades });
        Assert.Equal(expected.Video.Drawing, actual.Video.Drawing);
        Assert.Equal(expected.Video.Published, actual.Video.Published);
        Assert.Equal(expected.Video.Palette, actual.Video.Palette);
        Assert.Equal(expected.Video.LcdEnabled, actual.Video.LcdEnabled);
    }

    internal static void ReplayTwice(GameBoySystem system, Action<GameBoySystem> future)
    {
        var debug = system.GetDebugSnapshot();
        ulong sequence = system.Video.Sequence, frames = system.Video.CompletedFrameCount;
        var saved = system.CaptureState();
        Assert.Equal(debug, system.GetDebugSnapshot());
        Assert.Equal(sequence, system.Video.Sequence);
        future(system);
        var expected = system.CaptureState();
        var produced = system.Video.CompletedFrameCount - frames;
        for (var repeat = 0; repeat < 2; repeat++)
        {
            sequence = system.Video.Sequence;
            frames = system.Video.CompletedFrameCount;
            system.RestoreState(saved);
            Assert.Equal(sequence + 1, system.Video.Sequence);
            Assert.Equal(frames, system.Video.CompletedFrameCount);
            EqualState(saved, system.CaptureState());
            Assert.Equal(debug, system.GetDebugSnapshot());
            future(system);
            EqualState(expected, system.CaptureState());
            Assert.Equal(produced, system.Video.CompletedFrameCount - frames);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MbcControlAndEveryRamBankAreOwnedAndRestoreWithoutBusSideEffects(byte type)
    {
        var cartridge = CartridgeLoader.Load(TestRom.CreateMbc1(type, 4, type == 1 ? (byte)0 : (byte)3, 0x18, 0xFE)).Cartridge;
        var system = new GameBoySystem();
        system.InsertCartridge(cartridge);
        cartridge.Write(0, 10);
        cartridge.Write(0x6000, 1);
        for (var bank = 0; bank < 4; bank++)
        {
            cartridge.Write(0x4000, (byte)bank);
            cartridge.Write(0xA000, (byte)(bank + 31));
            cartridge.Write(0xBFFF, (byte)(bank + 71));
        }
        cartridge.Write(0x2000, 0); // Raw zero, not bank 1.
        var saved = system.CaptureState();
        Assert.Equal(0, saved.Cartridge.Bank);
        Assert.Equal(3, saved.Cartridge.Upper);
        cartridge.Write(0xBFFF, 255);
        cartridge.Write(0x2000, 5);
        cartridge.Write(0x6000, 0);
        system.RunForTCycles(100_000);
        system.RestoreState(saved);
        EqualState(saved, system.CaptureState());
        Assert.Equal(1, system.GetDebugSnapshot().RomBank1);
        for (var bank = 0; bank < 4; bank++)
        {
            cartridge.Write(0x4000, (byte)bank);
            Assert.Equal(type == 1 ? 255 : bank + 31, cartridge.Read(0xA000));
            Assert.Equal(type == 1 ? 255 : bank + 71, cartridge.Read(0xBFFF));
        }
        system.RestoreState(saved);
        EqualState(saved, system.CaptureState());
    }

    [Theory]
    [InlineData("ei")]
    [InlineData("halt-bug")]
    [InlineData("halt")]
    [InlineData("stop")]
    public void CpuBoundaryFlagsAndInputWakeReplay(string phase)
    {
        byte[] program = phase switch
        {
            "ei" => [0xFB, 0, 0x18, 0xFE],
            "halt-bug" => [0x3E, 1, 0xE0, 0x0F, 0xEA, 0xFF, 0xFF, 0x76, 0x04, 0, 0x18, 0xFE],
            "halt" => [0x76, 0, 0x18, 0xFE],
            _ => [0x3E, 0x10, 0xE0, 0x00, 0x10, 0, 0x04, 0x18, 0xFE] // P1=$10, STOP.
        };
        var system = TestRom.Start(program);
        for (var i = 0; i < phase switch { "halt-bug" => 4, "stop" => 3, _ => 1 }; i++)
        {
            system.StepInstruction();
        }

        var state = system.CaptureState();
        Assert.True(phase switch
        {
            "ei" => state.Cpu.EiPending,
            "halt-bug" => state.Cpu.HaltBug,
            "halt" => state.Cpu.Halted,
            _ => state.Cpu.Stopped
        });
        ReplayTwice(system, s =>
        {
            s.Joypad.SetButtonState(JoypadButton.A, true);
            for (var instruction = 0; instruction < 300; instruction++)
            {
                s.StepInstruction();
            }

            s.RunForTCycles(160_000);
            Assert.False(s.IsStopped);
        });
    }

    [Fact]
    public void FaultCanBeSavedAndHealthyStateCanRecoverIt()
    {
        var system = TestRom.Start(0xD3);
        var healthy = system.CaptureState();
        system.StepInstruction();
        Assert.True(system.IsFaulted);
        var fault = system.CaptureState();
        system.Reset();
        system.RestoreState(fault);
        Assert.True(system.IsFaulted);
        Assert.Equal(new CpuFault(0x150, 0xD3), system.Fault);
        Assert.Equal(4, system.StepInstruction().ExecutedTCycles); // Only time passes.
        Assert.Equal(fault.TotalTCycles + 4, system.TotalTCycles);
        system.RestoreState(healthy);
        Assert.False(system.IsFaulted);
        system.StepInstruction();
        EqualState(fault, system.CaptureState());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialDmaTimerSerialPpuSpritesAndBothImagesReplay(bool reloadWindow)
    {
        var system = TestRom.Start(0x18, 0xFE);
        var seed = system.CaptureState();
        seed.Memory.HighRam[0] = 0x18;
        seed.Memory.HighRam[1] = 0xFE; // CPU loops in HRAM.
        for (var i = 0; i < 160; i++)
        {
            seed.Memory.WorkRam[i] = (byte)(i ^ 0x5A);
        }

        for (var i = 0; i < seed.Ppu.Vram.Length; i++)
        {
            seed.Ppu.Vram[i] = (byte)(i * 7);
        }

        seed.Ppu.Sprites[0] = new(24, 21, 1, 0);
        seed.Ppu.Sprites[1] = new(40, 17, 2, 0x20); // Rows 3 and 7 of line 8.
        seed.Apu.WaveRam[7] = 0xA5;
        seed.Video.Drawing[0] = 21;
        seed.Video.Published[1] = 79;
        seed = seed with
        {
            Cpu = seed.Cpu with { PC = 0xFF80 },
            Timer = seed.Timer with { Tima = 0, Tma = 7, Tac = 5, ReloadDelay = reloadWindow ? 0 : 1, ReloadWindow = reloadWindow ? 4 : 0 },
            Serial = new(Data: 0xAA, Control: 0x81, Sent: 5, Clock: true, Bits: 3, Completed: [17, 23], Dropped: 4),
            Dma = seed.Dma with { Active = true, SourcePage = 0xC0, Register = 0xC0, Index = 17, PendingCycles = 1 },

            // Mode 3 has just begun on line 8 and the next window row is 5.
            Ppu = seed.Ppu with
            {
                DmaActive = true,
                Lcdc = 0xF3,
                Line = 8,
                Dot = 84,
                Mode = 3,
                Wx = 7,
                Pipe = seed.Ppu.Pipe with { WindowY = 4 },
                WindowYTriggered = true,
                SpriteCount = 2,
                LycFlag = false,
                LycSignal = false
            }
        };
        system.RestoreState(seed);
        ReplayTwice(system, s =>
        {
            s.RunForTCycles(160_000);
            var oam = new byte[160];
            s.CopyMemory(0xFE00, oam);
            Assert.Equal(seed.Memory.WorkRam[..160], oam);
            Assert.False(s.GetDebugSnapshot().DmaActive);
            Assert.True(s.Serial.TryReadTransmittedByte(out var first));
            Assert.Equal(17, first);
        });
    }

    // Line 153's LYC match, a line start, a late HALT wake and LCD-off latches replay exactly.
    [Theory]
    [InlineData("line153")]
    [InlineData("line-start")]
    [InlineData("halt-late")]
    [InlineData("lcd-off")]
    public void BoundaryPhasesAndLatchesReplay(string phase)
    {
        byte[] program = phase switch
        {
            "line153" => [0x3E, 0x40, 0xE0, 0x41, 0x3E, 0x99, 0xE0, 0x45], // STAT=LYC source, LYC=153, NOPs.
            "line-start" => [0x3E, 0x20, 0xE0, 0x41], // OAM source, NOPs.
            "halt-late" => [0x3E, 0x04, 0xE0, 0xFF, 0x3E, 0xFF, 0xE0, 0x05, 0x3E, 0x05, 0xE0, 0x07, 0x76, 0x3C],
            _ => [0x3E, 0x40, 0xE0, 0x41, 0xAF, 0xE0, 0x40, 0x3E, 0x01, 0xE0, 0x45, 0x3E, 0x91, 0xE0, 0x40]
        };
        var system = TestRom.Start(program);
        Func<DebugSnapshot, bool> reached = phase switch
        {
            "line153" => s => s.TotalTCycles >= (456 * 153) + 60, // Line 153, Dot 4.
            "line-start" => s => s.TotalTCycles >= 56 + 456, // Line 1, Dot 0.
            "halt-late" => s => s.IsHalted && (s.InterruptFlags & 4) != 0,
            _ => s => s.LcdControl == 0 && s.PC == 0x15B // LCD off, LYC changed to 1.
        };
        while (!reached(system.GetDebugSnapshot()))
        {
            system.StepInstruction();
        }

        var state = system.CaptureState();
        switch (phase)
        {
            case "line153":
                Assert.Equal((153, 4, 0), (state.Ppu.Line, state.Ppu.Dot, system.GetDebugSnapshot().Ly));
                Assert.True(state.Ppu.LycFlag); Assert.True(state.Ppu.StatLine);
                break;
            case "line-start":
                Assert.Equal((1, 0, 0), (state.Ppu.Line, state.Ppu.Dot, state.Ppu.Mode));
                Assert.True(state.Ppu.StatLine); Assert.False(state.Ppu.LycFlag); Assert.True(state.Ppu.LycSignal);
                break;
            case "halt-late":
                Assert.True(state.Cpu.Halted); // Wakes at the next sample.
                break;
            default:
                Assert.Equal(0x01, state.Ppu.Lyc); Assert.True(state.Ppu.LycFlag); Assert.True(state.Ppu.StatLine);
                break;
        }
        ReplayTwice(system, s =>
        {
            for (var i = 0; i < 40; i++)
            {
                s.StepInstruction();
            }

            s.RunForTCycles(150_000);
        });
    }

    [Fact]
    public void LcdOffAndPendingStopWakeAreRestored()
    {
        var system = TestRom.Start(0xAF, 0xE0, 0x40, 0xE0, 0x00, 0x10, 0, 0x18, 0xFE); // LCD off, P1=$00, STOP.
        for (var i = 0; i < 4; i++)
        {
            system.StepInstruction();
        }

        Assert.True(system.IsStopped);
        system.Joypad.SetButtonState(JoypadButton.A, true);
        Assert.False(system.CaptureState().Video.LcdEnabled);
        ReplayTwice(system, s =>
        {
            s.RunForTCycles(1024);
            Assert.False(s.IsStopped);
        });
    }

    [Fact]
    public void SameContentCanRestoreIntoAnotherSystemButOtherRomCannot()
    {
        var image = TestRom.Create(0x18, 0xFE);
        var first = new GameBoySystem();
        first.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        first.RunForTCycles(8192);
        var second = new GameBoySystem();
        second.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        second.RestoreState(first.CaptureState());
        EqualState(first.CaptureState(), second.CaptureState());
        image[^1] = 1;
        second.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        var before = second.CaptureState();
        Assert.Throws<ArgumentException>(() => second.RestoreState(first.CaptureState()));
        EqualState(before, second.CaptureState());
    }

    [Theory]
    [InlineData("first-frame")]
    [InlineData("just-halted")]
    [InlineData("lcd-off-stat-line")]
    [InlineData("lcd-on-line-objects")]
    public void InvalidLcdAndHaltFlagsAreRejected(string kind)
    {
        var system = TestRom.Start(0xAF, 0xE0, 0x40, 0x3E, 0x91, 0xE0, 0x40, 0x18, 0xFE);
        system.StepInstruction();
        system.StepInstruction(); // LCD off.
        var off = system.CaptureState();
        system.StepInstruction();
        system.StepInstruction(); // LCD on: line 0 begins.
        var on = system.CaptureState();
        Assert.True(on.Ppu.LcdOnLine && on.Video.FirstFrame);
        var bad = kind switch
        {
            "first-frame" => off with { Video = off.Video with { FirstFrame = true } },
            "just-halted" => on with { Cpu = on.Cpu with { JustHalted = true } },
            "lcd-off-stat-line" => off with { Ppu = off.Ppu with { StatLine = !off.Ppu.StatLine } },
            _ => on with { Ppu = on.Ppu with { SpriteCount = 1 } }
        };
        system.RestoreState(off);
        system.RestoreState(on); // Both valid as captured.
        Assert.Throws<ArgumentException>(() => system.RestoreState(bad));
        EqualState(on, system.CaptureState());
    }

    [Theory]
    [InlineData("version")]
    [InlineData("model")]
    [InlineData("boot")]
    [InlineData("type")]
    [InlineData("clock")]
    [InlineData("cpu")]
    [InlineData("wram")]
    [InlineData("boot-rom")]
    [InlineData("cart")]
    [InlineData("cart-camera")]
    [InlineData("cart-huc3")]
    [InlineData("cart-clock")]
    [InlineData("irq")]
    [InlineData("timer")]
    [InlineData("serial")]
    [InlineData("joypad")]
    [InlineData("apu")]
    [InlineData("apu-dac")]
    [InlineData("apu-skip")]
    [InlineData("apu-sweep")]
    [InlineData("apu-wave")]
    [InlineData("apu-noise")]
    [InlineData("apu-mixer")]
    [InlineData("audio")]
    [InlineData("dma")]
    [InlineData("vram")]
    [InlineData("ppu-phase")]
    [InlineData("pixels")]
    [InlineData("alpha")]
    [InlineData("palette")]
    [InlineData("lcd-connection")]
    [InlineData("dma-connection")]
    [InlineData("ppu-line")]
    [InlineData("ppu-mode")]
    [InlineData("lyc-flag")]
    [InlineData("stat-line")]
    [InlineData("lcd-off-phase")]
    public void InvalidStateNeverChangesAnyBlockOrPublishedSequence(string kind)
    {
        var system = TestRom.Start(0x18, 0xFE);
        var saved = system.CaptureState();
        var bad = kind switch
        {
            "version" => saved with { FormatVersion = 1 },
            "model" => saved with { Model = "CGB" },
            "boot" => saved with { BootProfile = "other" },
            "type" => saved with { CartridgeType = 3 },
            "clock" => saved with { TotalTCycles = 1 },
            "cpu" => saved with { Cpu = saved.Cpu with { AF = 1 } },
            "wram" => saved with { Memory = saved.Memory with { WorkRam = [] } },
            "boot-rom" => saved with { Memory = saved.Memory with { BootRom = new byte[3] } }, // 256 bytes while mapped.
            "cart" => saved with { Cartridge = saved.Cartridge with { Bank = 9 } },

            // A ROM Only state names no camera, HuC3 MCU or clock.
            "cart-camera" => saved with { Cartridge = saved.Cartridge with { Camera = new(new byte[0x36], false, 0, new byte[0xE00]) } },
            "cart-huc3" => saved with { Cartridge = saved.Cartridge with { Huc3 = new(new byte[0x100], 0, 0, 0, 0, 0) } },
            "cart-clock" => saved with { Cartridge = saved.Cartridge with { Rtc = new(new(0, 0, 0, 0, 0), new(0, 0, 0, 0, 0), 0, false) } },
            "irq" => saved with { Interrupts = saved.Interrupts with { Requested = 255 } },
            "timer" => saved with { Timer = saved.Timer with { ReloadDelay = 5 } },
            "serial" => saved with { Serial = saved.Serial with { Bits = 8 } }, // Completed transfers count from 0.
            "joypad" => saved with { Joypad = saved.Joypad with { Selection = 1 } },
            "apu" => saved with { Apu = saved.Apu with { WaveRam = [] } },
            "apu-dac" => saved with { Apu = saved.Apu with { Pulse1 = saved.Apu.Pulse1 with { Envelope = saved.Apu.Pulse1.Envelope with { Register = 0x07 } } } }, // On with its DAC off.
            "apu-skip" => saved with { Apu = saved.Apu with { SkipStep = true } }, // DIV bit 12 is clear here.
            "apu-sweep" => saved with { Apu = saved.Apu with { PendingSweepAt = saved.TotalTCycles + 5 } }, // Due 4 T after an edge at most.
            "apu-wave" => saved with { Apu = saved.Apu with { Wave = saved.Apu.Wave with { Shift = 3 } } },
            "apu-noise" => saved with { Apu = saved.Apu with { Noise = saved.Apu.Noise with { Lfsr = 0x8000 } } },
            "apu-mixer" => saved with { Apu = saved.Apu with { Mixer = saved.Apu.Mixer with { Phase = saved.Apu.Mixer.Phase + 48_000 } } }, // Not the clock's phase.
            "audio" => saved with { Audio = saved.Audio with { Queued = new short[3] } }, // Half a frame.
            "dma" => saved with { Dma = saved.Dma with { Index = 161 } },
            "vram" => saved with { Ppu = saved.Ppu with { Vram = [] } },
            "ppu-phase" => saved with { Ppu = saved.Ppu with { Dot = 456 } },
            "pixels" => saved with { Video = saved.Video with { Drawing = [] } },
            "alpha" => saved with { Video = saved.Video with { Published = new byte[VideoOutput.BufferSize] } },
            "palette" => saved with { Video = saved.Video with { Palette = [1, 2, 3, 4] } },
            "lcd-connection" => saved with { Video = saved.Video with { LcdEnabled = false } },
            "ppu-line" => saved with { Ppu = saved.Ppu with { Line = 154 } },
            "ppu-mode" => saved with { Ppu = saved.Ppu with { Mode = 3 } },
            "lyc-flag" => saved with { Ppu = saved.Ppu with { LycFlag = !saved.Ppu.LycFlag } },
            "stat-line" => saved with { Ppu = saved.Ppu with { StatLine = !saved.Ppu.StatLine } },
            "lcd-off-phase" => saved with { Ppu = saved.Ppu with { Lcdc = 0 }, Video = saved.Video with { LcdEnabled = false } },
            _ => saved with { Ppu = saved.Ppu with { DmaActive = true } }
        };
        system.RunForTCycles(1234);
        var before = system.CaptureState();
        var sequence = system.Video.Sequence;
        Assert.Throws<ArgumentException>(() => system.RestoreState(bad));
        EqualState(before, system.CaptureState());
        Assert.Equal(sequence, system.Video.Sequence);
        Assert.Throws<ArgumentNullException>(() => system.RestoreState(null!));
        EqualState(before, system.CaptureState());
    }

    [Fact]
    public void UnsupportedCartridgeIsRejectedWithoutChangingExecution()
    {
        var system = TestRom.Start(0x18, 0xFE);
        var saved = system.CaptureState();
        system.InsertCartridge(new CustomCartridge());
        var before = system.GetDebugSnapshot();
        Assert.Throws<NotSupportedException>(system.CaptureState);
        Assert.Throws<NotSupportedException>(() => system.RestoreState(saved));
        Assert.Equal(before, system.GetDebugSnapshot());
        Assert.Throws<NotSupportedException>(() => new GameBoySystem().CaptureState());
    }

    private sealed class CustomCartridge : ICartridge
    {
        public byte Read(ushort address) => 0; public void Write(ushort address, byte value)
{
}

        public void ResetController()
        {
        }
    }
}
