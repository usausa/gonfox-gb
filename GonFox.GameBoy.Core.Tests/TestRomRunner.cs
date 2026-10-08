namespace GonFox.GameBoy.Core;

using System.Diagnostics;

using GonFox.GameBoy.Core.Cartridge;

internal sealed record TestRomResult(string Outcome, DebugSnapshot State, byte[] Serial, byte[] HighRam)
{
    public override string ToString() => $"{Outcome}: PC={State.PC:X4} AF={State.AF:X4} " +
        $"BC={State.BC:X4} DE={State.DE:X4} HL={State.HL:X4} T={State.TotalTCycles} " +
        $"serial={Convert.ToHexString(Serial)} HRAM[FF80]={Convert.ToHexString(HighRam)}";
}

internal static class TestRomRunner
{
    // Runs a ROM to its LD B,B verdict, from power-on through bootRom when one is given.
    internal static TestRomResult Run(byte[] image, ulong maxTCycles, TimeSpan timeout, byte[]? bootRom = null)
    {
        var system = new GameBoySystem();
        if (bootRom is not null)
        {
            system.UseBootRom(bootRom);
        }

        system.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        var watch = Stopwatch.StartNew();
        List<byte> serial = [];
        Span<byte> opcode = stackalloc byte[1];
        var instructions = 0;
        TestRomResult Finish(string outcome)
        {
            var highRam = new byte[8];
            system.CopyMemory(0xFF80, highRam);
            return new(outcome, system.GetDebugSnapshot(), serial.ToArray(), highRam);
        }

        while (system.TotalTCycles < maxTCycles)
        {
            if ((instructions++ & 1023) == 0 && watch.Elapsed >= timeout)
            {
                return Finish("TIMEOUT (host)");
            }

            var before = system.GetDebugSnapshot();
            system.CopyMemory(before.PC, opcode);
            var step = system.StepInstruction();
            if (system.IsFaulted)
            {
                return Finish("FAULT (undefined opcode)");
            }

            if (step.IsStopped)
            {
                return Finish("STOP (input required)");
            }

            var after = system.GetDebugSnapshot();
            if (opcode[0] == 0x40 && step.ExecutedTCycles == 4 && after.PC == unchecked((ushort)(before.PC + 1)))
            {
                if (before.BC == 0x4242 && before.DE == 0x4242 && before.HL == 0x4242)
                {
                    return Finish("FAIL");
                }

                if (before.BC == 0x0305 && before.DE == 0x080D && before.HL == 0x1522)
                {
                    return Finish("PASS");
                }
            }
            while (system.Serial.TryReadTransmittedByte(out var value))
            {
                if (serial.Count == 64)
                {
                    return Finish("FAIL (excess serial output)");
                }

                serial.Add(value);
            }
        }
        return Finish("TIMEOUT (T-cycles)");
    }

    // Runs a gbmicrotest ROM until FF82 reads 01 (pass) or FF (fail).
    internal static TestRomResult RunMicrotest(byte[] image, ulong maxTCycles, TimeSpan timeout)
    {
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        var watch = Stopwatch.StartNew();
        Span<byte> result = stackalloc byte[1];
        var instructions = 0;
        TestRomResult Finish(string outcome)
        {
            var highRam = new byte[8];
            system.CopyMemory(0xFF80, highRam);
            return new(outcome, system.GetDebugSnapshot(), [], highRam);
        }

        while (system.TotalTCycles < maxTCycles)
        {
            if ((instructions++ & 1023) == 0 && watch.Elapsed >= timeout)
            {
                return Finish("TIMEOUT (host)");
            }

            var step = system.StepInstruction();
            if (system.IsFaulted)
            {
                return Finish("FAULT (undefined opcode)");
            }

            if (step.IsStopped)
            {
                return Finish("STOP (input required)");
            }

            system.CopyMemory(0xFF82, result);
            if (result[0] == 0x01)
            {
                return Finish("PASS");
            }

            if (result[0] == 0xFF)
            {
                return Finish("FAIL");
            }
        }
        return Finish("TIMEOUT (T-cycles)");
    }
}
