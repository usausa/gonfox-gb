namespace Benchmark;

using System.Security.Cryptography;
using System.Text.Json;

using GonFox.GameBoy.Core;
using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Memory;
using GonFox.GameBoy.Core.Video;

public enum ExecutionCase
{
    Alu,
    MemoryBranch,
    TimerDma,
    Background,
    Mbc1Background,
    Acid2,
    MegaWave,
    MegaPlasma,
    MegaOrbit,
    Sound
}

public enum PpuCase
{
    Background,
    Window,
    Sprites
}

// Runs a scenario from a prepared state; shared with Core.Tests, so no BenchmarkDotNet types.
internal sealed class ExecutionWorkload
{
    internal const int Cycles = 4 * GameBoySystem.CyclesPerSecond;
    internal GameBoySystem System { get; } = new();
    internal GameBoyState Initial { get; }
    internal string RomHash { get; }

    internal ExecutionWorkload(ExecutionCase scenario)
    {
        var rom = scenario switch
        {
            ExecutionCase.Background => ReadRom("Roms/BackgroundDemo/background-demo.gb", "9053de305c39aed9780a74057e1fc44e99d9cc8ac3a62b5eb7d4ccdd82e9c0f8"),
            ExecutionCase.Mbc1Background => ReadRom("Roms/BackgroundDemo/mbc1-demo.gb", "2bdc7882666d81731b8a9936670049e8a4e1b8feee07f1f73ffa27f12bb8c812"),
            ExecutionCase.Acid2 => ReadRom("Roms/External/dmg-acid2/dmg-acid2.gb", "464e14b7d42e7feea0b7ede42be7071dc88913f75b9ffa444299424b63d1dff1"),
            ExecutionCase.MegaWave or ExecutionCase.MegaPlasma or ExecutionCase.MegaOrbit =>
                ReadRom("Roms/MegaDemo/megademo.gb", "d1c2a4987088f97c051ecd8fe7bb008ebf9bb2c180556ee7e267a2809beea41f"),
            _ => CpuRom(scenario)
        };
        RomHash = Hash(rom);
        System.InsertCartridge(CartridgeLoader.Load(rom).Cartridge);
        System.RunForTCycles(GameBoySystem.CyclesPerSecond); // Boot, outside timing.
        if (scenario is ExecutionCase.Background or ExecutionCase.Mbc1Background)
        {
            System.Joypad.SetButtonState(JoypadButton.Right, true);
        }

        if (scenario >= ExecutionCase.MegaWave)
        {
            // Selects the effect and raises its amount, then lets it animate without input.
            for (var scene = 0; scene < scenario - ExecutionCase.MegaWave; scene++)
            {
                Press(JoypadButton.Select, 400_000);
            }

            Press(JoypadButton.Up, 4_000_000);
        }
        Initial = System.CaptureState();
    }

    private void Press(JoypadButton button, int heldTCycles)
    {
        System.Joypad.SetButtonState(button, true);
        System.RunForTCycles(heldTCycles);
        System.Joypad.SetButtonState(button, false);
        System.RunForTCycles(1_000_000);
    }

    internal void Reset() => System.RestoreState(Initial);

    internal long Run(int chunk = Cycles)
    {
        ulong start = System.TotalTCycles, target = start + Cycles;
        while (System.TotalTCycles < target)
        {
            var result = System.RunForTCycles((int)Math.Min((ulong)chunk, target - System.TotalTCycles));
            if (result.IsStopped || result.ExecutedTCycles == 0)
            {
                throw new InvalidOperationException("Workload stopped.");
            }
        }
        return (long)(System.TotalTCycles - start);
    }

    internal static string Fingerprint(GameBoyState s) => Hash(JsonSerializer.SerializeToUtf8Bytes(new
    {
        s.FormatVersion,
        s.Model,
        s.BootProfile,
        s.RomSha256,
        s.CartridgeType,
        s.TotalTCycles,
        s.Cpu,
        s.Memory,
        s.Cartridge,
        s.Interrupts,
        s.Timer,
        s.Serial,
        s.Joypad,
        s.Apu,
        s.Audio,
        s.Dma,
        s.Ppu,
        s.Video
    }));

    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] ReadRom(string relative, string hash)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GonFox.GameBoy.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Run from a checkout containing GonFox.GameBoy.slnx.");
        }

        var bytes = File.ReadAllBytes(Path.Combine(directory.FullName, relative));
        if (Hash(bytes) != hash)
        {
            throw new InvalidDataException($"ROM hash mismatch: {relative}");
        }

        return bytes;
    }

    // Routes all four channels to both sides at fixed volumes, so every step reaches the mix.
    private static readonly (byte Register, byte Value)[] SoundRegisters =
    [
        (0x24, 0x77), (0x25, 0xFF),
        .. new byte[] { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF, 0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10 }
            .Select((value, i) => ((byte)(0x30 + i), value)),
        (0x10, 0x00), (0x11, 0x80), (0x12, 0xF0), (0x13, 0xD6), (0x14, 0x86), // CH1: 1,192 T per duty step.
        (0x16, 0x40), (0x17, 0xC0), (0x18, 0x39), (0x19, 0x87),               // CH2: 796 T.
        (0x1A, 0x80), (0x1C, 0x20), (0x1D, 0xAC), (0x1E, 0x85),               // CH3: 1,192 T per sample.
        (0x21, 0xA0), (0x22, 0x24), (0x23, 0x80)                             // CH4: an LFSR clock every 256 T.
    ];

    // Builds a small original program of defined SM83 instructions, without the header logo.
    internal static byte[] CpuRom(ExecutionCase scenario)
    {
        var rom = new byte[0x8000];
        rom[0x100] = 0xC3;
        rom[0x101] = 0x50;
        rom[0x102] = 0x01;
        var setup = new List<byte> { 0xF3, 0xAF, 0xE0, 0x40, 0x21, 0x00, 0xC0 }; // DI; LCD off; HL=C000.
        byte[] loop = scenario switch
        {
            ExecutionCase.Alu => [0x80, 0xA8, 0x07, 0xCB, 0x37, 0xFE, 0x80, 0x20, 0, 0xC3, 0, 2],
            ExecutionCase.MemoryBranch => [0x34, 0x7E, 0xEA, 0, 0xC1, 0xFA, 0, 0xC1, 0x2C, 0x20, 0, 0xC3, 0, 2],

            // HRAM: DMA C000; wait >648 T; repeat. No ROM fetches during DMA.
            ExecutionCase.TimerDma => [0x3E, 0xC0, 0xE0, 0x46, 0x06, 0x40, 0x05, 0x20, 0xFD, 0x18, 0xF5],

            // Counts B down, then sets NR13 from DIV, moving CH1's pitch every 4,100 T or so.
            ExecutionCase.Sound => [0x06, 0x00, 0x05, 0x20, 0xFD, 0xF0, 0x04, 0xE0, 0x13, 0x18, 0xF5],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        if (scenario == ExecutionCase.TimerDma)
        {
            setup.AddRange([0x36, 0x5A, 0x3E, 0x05, 0xE0, 0x07]); // DMA source marker; timer on, divider bit 3.
            for (var i = 0; i < loop.Length; i++)
            {
                setup.AddRange([0x3E, loop[i], 0xE0, (byte)(0x80 + i)]);
            }

            setup.AddRange([0xC3, 0x80, 0xFF]);
        }
        else
        {
            if (scenario == ExecutionCase.Sound)
            {
                foreach (var (register, value) in SoundRegisters) setup.AddRange([0x3E, value, 0xE0, register]);
            }

            setup.AddRange([0x06, 3, 0x3E, 0x5A, 0xC3, 0, 2]);
            loop.CopyTo(rom, 0x200);
        }
        setup.CopyTo(rom, 0x150);
        byte checksum = 0;
        for (var i = 0x134; i <= 0x14C; i++)
        {
            checksum = unchecked((byte)(checksum - rom[i] - 1));
        }

        rom[0x14D] = checksum;
        return rom;
    }
}

internal sealed class PpuWorkload
{
    internal const int Frames = 240;
    internal const int Cycles = 70_224 * Frames;
    internal VideoOutput Video { get; } = new();
    internal Ppu Ppu { get; }
    private readonly Ppu.State initial;
    private readonly VideoOutput.State videoInitial;

    internal PpuWorkload(PpuCase scenario)
    {
        Ppu = new(new Interrupts(), Video);
        Ppu.Reset();
        Ppu.WriteRegister(0xFF40, 0);
        Ppu.WriteRegister(0xFF47, 0xE4);
        Ppu.WriteRegister(0xFF48, 0xE4);

        // Writes constant-color tiles: BG=1, Window=2, OBJ=3.
        for (var shade = 1; shade <= 3; shade++)
        {
            for (var row = 0; row < 8; row++)
            {
                Ppu.WriteMemory((ushort)(0x8000 + (shade * 16) + (row * 2)), (byte)((shade & 1) != 0 ? 255 : 0));
                Ppu.WriteMemory((ushort)(0x8001 + (shade * 16) + (row * 2)), (byte)((shade & 2) != 0 ? 255 : 0));
            }
        }

        for (var i = 0; i < 1024; i++)
        {
            Ppu.WriteMemory((ushort)(0x9800 + i), 1);
            Ppu.WriteMemory((ushort)(0x9C00 + i), 2);
        }

        // Window at x=80, y=32; set only here, as even a disabled window can insert a DMG pixel.
        if (scenario == PpuCase.Window)
        {
            Ppu.WriteRegister(0xFF4A, 32);
            Ppu.WriteRegister(0xFF4B, 87);
        }
        for (var i = 0; i < 40; i++)
        {
            Ppu.WriteMemory((ushort)(0xFE00 + (i * 4)), (byte)(16 + (i / 10 * 32)));
            Ppu.WriteMemory((ushort)(0xFE01 + (i * 4)), (byte)(8 + ((i % 10) * 16)));
            Ppu.WriteMemory((ushort)(0xFE02 + (i * 4)), 3);
        }
        Ppu.WriteRegister(0xFF40, scenario switch { PpuCase.Background => 0x91, PpuCase.Window => 0xF1, _ => 0x93 });
        initial = Ppu.CaptureState();
        videoInitial = Video.CaptureState();
    }

    internal void Reset()
    {
        Ppu.RestoreState(initial);
        Video.RestoreState(videoInitial);
    }

    internal ulong Run()
    {
        var frames = Video.CompletedFrameCount;
        for (var i = 0; i < Cycles; i++)
        {
            Ppu.Tick();
        }

        return Video.CompletedFrameCount - frames;
    }

    internal byte[] Pixels()
    {
        var result = new byte[VideoOutput.BufferSize];
        Video.CopyLatestFrame(result);
        return result;
    }

    internal static byte Expected(PpuCase scenario, int x, int y) => scenario switch
    {
        PpuCase.Window when x >= 80 && y >= 32 => 85,
        PpuCase.Sprites when y < 104 && y % 32 < 8 && x % 16 < 8 => 0,
        _ => 170
    };
}

internal sealed class BusWorkload
{
    internal MemoryBus Bus { get; } = new();

    internal BusWorkload(bool mbc1)
    {
        var rom = new byte[mbc1 ? 0x10000 : 0x8000];
        for (var bank = 0; bank < rom.Length / 0x4000; bank++)
        {
            Array.Fill(rom, (byte)bank, bank * 0x4000, 0x4000);
        }

        Bus.ConnectCartridge(mbc1 ? new Mbc1Cartridge(rom, 0x8000) : new RomOnlyCartridge(rom));
        Bus.WriteByte(0, 0x0A);
    }

    internal int Run()
    {
        var sum = 0;
        for (var i = 0; i < 4096; i++)
        {
            Bus.WriteByte(0x2000, (byte)(1 + (i % 3)));
            Bus.WriteByte((ushort)(0xC000 + (i & 8191)), (byte)i);
            sum += Bus.ReadByte(0x4000) + Bus.ReadByte((ushort)(0xE000 + (i & 4095)));
        }
        return sum;
    }
}
