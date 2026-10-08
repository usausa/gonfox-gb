namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Video;

// STAT and interrupt edges, on the PPU alone or with the CPU running a small HRAM program.
[Trait("Category", "Unit")]
public sealed class StatInterruptTimingTests
{
    private readonly Interrupts irq = new();
    private readonly VideoOutput video = new();
    private readonly Ppu ppu;

    public StatInterruptTimingTests()
    {
        ppu = new Ppu(irq, video);
        ppu.Reset();
    }

    private bool StatRequested => (irq.Flags & 2) != 0;

    // The line begun by enabling the LCD has no OAM scan; the next frame's line 0 draws the object.
    [Fact]
    public void TheLcdOnLineDrawsNoObjects()
    {
        ppu.WriteRegister(0xFF40, 0);
        ppu.WriteMemory(0xFE00, 16);
        ppu.WriteMemory(0xFE01, 8); // An object on line 0 at x=0.
        ppu.WriteRegister(0xFF40, 0x93);
        Assert.Equal(255, ModeZeroDot());
        while (ppu.Ly != 0 || ppu.Dot != 4)
        {
            ppu.Tick(); // The next frame's line 0 scans OAM.
        }

        Assert.True(ModeZeroDot() > 253);
    }

    private int ModeZeroDot()
    {
        while (ppu.Mode != 3)
        {
            ppu.Tick();
        }

        while (ppu.Mode == 3)
        {
            ppu.Tick();
        }

        return ppu.Dot;
    }

    // A LYC=143 match ends before mode 1, so mode 1 raises STAT again unless the OAM source bridges it.
    [Theory]
    [InlineData(0x50, true)]
    [InlineData(0x70, false)]
    public void ALyc143MatchEndsBeforeMode1(byte stat, bool requested)
    {
        ppu.WriteRegister(0xFF45, 143);
        ppu.WriteRegister(0xFF41, stat);
        while (ppu.Ly != 143 || ppu.Dot != 8)
        {
            ppu.Tick();
        }

        Assert.True(StatRequested);
        irq.WriteFlags(0);
        while (ppu.Ly != 144 || ppu.Dot != 2)
        {
            ppu.Tick();
        }

        Assert.Equal(requested, StatRequested);
        Assert.Equal(1, irq.Flags & 1); // VBlank at the same dot.
    }

    // The OAM source holds STAT from Dot 0 of line 144 into mode 1, so a cleared IF gets no new request.
    [Fact]
    public void TheVblankOamSourceHoldsTheLineUntilMode1()
    {
        ppu.WriteRegister(0xFF41, 0x30);
        while (ppu.Ly != 144 || ppu.Dot != 0)
        {
            ppu.Tick();
        }

        Assert.True(StatRequested);
        irq.WriteFlags(0);
        while (ppu.Dot != 2)
        {
            ppu.Tick();
        }

        Assert.False(StatRequested);
        Assert.Equal(1, irq.Flags & 1);
    }

    // At a line start the HBlank source still holds STAT, so a STAT write there raises no request.
    [Theory]
    [InlineData(0x48, 0x00, 5)] // Cleared at line 6's start.
    [InlineData(0x08, 0x40, 5)] // LYC match still held.
    public void AStatWriteAtALineStartAfterHblankRaisesNothing(byte before, byte written, byte lyc)
    {
        ppu.WriteRegister(0xFF45, lyc);
        ppu.WriteRegister(0xFF41, before);
        while (ppu.Ly != lyc + 1 || ppu.Dot != 0)
        {
            ppu.Tick();
        }

        irq.WriteFlags(0);
        ppu.WriteRegister(0xFF41, written);
        Assert.False(StatRequested);
        while (ppu.Dot != 8)
        {
            ppu.Tick();
        }

        Assert.False(StatRequested);
    }

    // Line 153 compares LY=153 from Dot 2 and LY=0 from Dot 10.
    [Theory]
    [InlineData(153, 2)]
    [InlineData(0, 10)]
    public void Line153ComparesTwoTCyclesIntoItsMCycles(byte lyc, int dot)
    {
        ppu.WriteRegister(0xFF45, lyc);
        ppu.WriteRegister(0xFF41, 0x40);
        while (ppu.Ly != 152 || ppu.Dot != 455)
        {
            ppu.Tick();
        }

        irq.WriteFlags(0);
        for (var at = 0; at < 456; at++)
        {
            ppu.Tick();
            if (StatRequested)
            {
                Assert.Equal(dot, ppu.Dot);
                return;
            }
        }
        Assert.Fail("no request on line 153");
    }

    // HALT at Dot 252 samples IF at 256, so the HBlank request at 257 wakes the CPU only at 264.
    [Fact]
    public void TheFirstMCycleAfterHaltSamplesIfAtItsStart()
    {
        // ReSharper disable once UseUtf8StringLiteral
        var system = At(252, [0x76, 0x00, 0x00], ime: false, ie: 0x02, flags: 0x00);
        system.StepInstruction(); // HALT
        Assert.True(system.IsHalted);
        while (system.IsHalted)
        {
            system.StepInstruction();
        }

        Assert.Equal((1, 264), (system.GetDebugSnapshot().Ly, system.GetDebugSnapshot().PpuDot));
    }

    // An IF write clears a request raised in the first T-cycle of its own M-cycle.
    [Theory]
    [InlineData(248, false)]
    [InlineData(244, true)]
    public void AnIfWriteOverwritesARequestOfItsFirstTCycle(int dot, bool survives)
    {
        var system = At(dot, [0xE0, 0x0F, 0x00, 0x00, 0x00], ime: false, ie: 0x02, flags: 0x00);
        system.StepInstruction(); // LDH (0F),A with A=0
        system.StepInstruction();
        system.StepInstruction(); // Past 260.
        Assert.Equal(survives, (system.GetDebugSnapshot().InterruptFlags & 2) != 0);
    }

    // Dispatch picks the vector when the low PC byte is pushed, so a late STAT request beats the timer.
    [Fact]
    public void TheDispatchChoosesFromIfAtTheLowerPush()
    {
        // ReSharper disable once UseUtf8StringLiteral
        var system = At(244, [0x00, 0x00], ime: true, ie: 0x06, flags: 0x04);
        Assert.Equal(20, system.StepInstruction().ExecutedTCycles);
        var state = system.GetDebugSnapshot();
        Assert.Equal(0x48, state.PC);
        Assert.Equal(0x04, state.InterruptFlags & 0x1F);
    }

    // Runs to (line 1, dot) with SCX=3 and the HBlank source, then continues in an HRAM program, A=0.
    private static GameBoySystem At(int dot, byte[] program, bool ime, byte ie, byte flags)
    {
        var code = new byte[0x2000];
        new byte[] { 0x3E, 0x03, 0xE0, 0x43, 0x3E, 0x08, 0xE0, 0x41 }.CopyTo(code, 0); // SCX=3, STAT=08, then NOPs.
        var system = TestRom.Start(code);
        while (system.GetDebugSnapshot() is var s && (s.Ly != 1 || s.PpuDot != dot))
        {
            system.StepInstruction();
        }

        var state = system.CaptureState();
        var high = state.Memory.HighRam.ToArray();
        program.CopyTo(high, 0);
        system.RestoreState(state with
        {
            Cpu = state.Cpu with { PC = 0xFF80, AF = 0x0000, Ime = ime, EiPending = false },
            Memory = state.Memory with { HighRam = high },
            Interrupts = new(Requested: flags, Enabled: ie)
        });
        return system;
    }
}
