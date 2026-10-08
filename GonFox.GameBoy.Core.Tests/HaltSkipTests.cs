namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;

// Skipping quiet HALT cycles in Run must give the same machine as stepping every M-cycle.
[Trait("Category", "Unit")]
public sealed class HaltSkipTests
{
    // Builds a HALT loop whose handlers count VBlank in D, STAT in B, timer in C and serial in E.
    private static byte[] Rom(params byte[] setup)
    {
        byte[] program = [.. setup, 0xFB, 0x76, 0x00, 0x18, 0xFC]; // EI; loop: HALT; NOP; JR loop
        var image = TestRom.Create(program);
        byte[][] handlers = [[0x14, 0xD9], [0x04, 0xD9], [0x0C, 0xD9], [0x1C, 0x3E, 0x81, 0xE0, 0x02, 0xD9], [0xD9]]; // INC D/B/C/E; RETI
        for (var i = 0; i < handlers.Length; i++)
        {
            handlers[i].CopyTo(image, 0x40 + (i * 8));
        }

        TestRom.UpdateHeaderChecksum(image);
        return image;
    }

    public static TheoryData<string, byte[]> Programs() => new()
    {
        // LCD on with HBlank and LYC STAT, VBlank, a fast timer and serial transfers.
        {
            "all sources", Rom(0x3E, 0x05, 0xE0, 0x07, 0x3E, 0xC0, 0xE0, 0x06, 0x3E, 0x48, 0xE0, 0x41, 0x3E, 0x50, 0xE0, 0x45,
            0x3E, 0x81, 0xE0, 0x02, 0x3E, 0x0F, 0xE0, 0xFF)
        },

        // SCX 3 makes the HBlank request get sampled one M-cycle later.
        { "hblank scx3", Rom(0x3E, 0x03, 0xE0, 0x43, 0x3E, 0x08, 0xE0, 0x41, 0x3E, 0x02, 0xE0, 0xFF) },

        // LCD off and timer stopped: only serial and APU frame sequencer edges bound the waits.
        { "long waits", Rom(0xAF, 0xE0, 0x40, 0x3E, 0x08, 0xE0, 0xFF) },
        { "slow timer", Rom(0x3E, 0x04, 0xE0, 0x07, 0x3E, 0x05, 0xE0, 0xFF) }
    };

    private static GameBoySystem Start(byte[] image)
    {
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        return system;
    }

    [Theory]
    [MemberData(nameof(Programs))]
    public void QuietHaltCyclesPassAsSteppingThemWould(string name, byte[] image)
    {
        _ = name;
        ulong target = (5 * 70_224) + 1_234;
        var stepped = Start(image);
        while (stepped.TotalTCycles < target)
        {
            stepped.StepInstruction();
        }

        var expected = stepped.CaptureState();
        Assert.True(expected.Cpu.Halted || expected.TotalTCycles >= target);

        foreach (var budget in new[] { int.MaxValue, 4096, 456, 70 })
        {
            var run = Start(image);
            while (run.TotalTCycles < target)
            {
                run.RunForTCycles((int)Math.Min((ulong)budget, target - run.TotalTCycles));
            }

            StateTests.EqualState(expected, run.CaptureState());
        }
    }

    [Fact]
    public void TheProgramsReallyWaitAndWake()
    {
        var system = Start(Programs().Select(row => row.Data.Item2).First());
        system.RunForTCycles(70_224 + 5_000); // About one frame; counters are bytes.
        var s = system.GetDebugSnapshot();
        Assert.True(s.D >= 1 && s.B >= 140 && s.C >= 60 && s.E >= 10, $"VBlank {s.D}, STAT {s.B}, timer {s.C}, serial {s.E}");
    }
}
