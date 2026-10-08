namespace GonFox.GameBoy.Core;

[Trait("Category", "Unit")]
public sealed class CpuOpcodeCoverageTests
{
    // T-cycles per base opcode with Z=C=0; 0 marks an undefined opcode and CB counts as CB 00.
    private const string Cycles = """
        4 12 8 8 4 4 8 4 20 8 8 8 4 4 8 4
        4 12 8 8 4 4 8 4 12 8 8 8 4 4 8 4
        12 12 8 8 4 4 8 4 8 8 8 8 4 4 8 4
        12 12 8 8 12 12 12 4 8 8 8 8 4 4 8 4
        4 4 4 4 4 4 8 4 4 4 4 4 4 4 8 4
        4 4 4 4 4 4 8 4 4 4 4 4 4 4 8 4
        4 4 4 4 4 4 8 4 4 4 4 4 4 4 8 4
        8 8 8 8 8 8 4 8 4 4 4 4 4 4 8 4
        4 4 4 4 4 4 8 4 4 4 4 4 4 4 8 4
        4 4 4 4 4 4 8 4 4 4 4 4 4 4 8 4
        4 4 4 4 4 4 8 4 4 4 4 4 4 4 8 4
        4 4 4 4 4 4 8 4 4 4 4 4 4 4 8 4
        20 12 16 16 24 16 8 16 8 16 12 8 12 24 8 16
        20 12 16 0 24 16 8 16 8 16 12 0 12 0 8 16
        12 12 8 0 0 16 8 16 16 4 16 0 0 0 8 16
        12 12 8 4 0 16 8 16 12 8 16 4 0 0 8 16
        """;

    public static IEnumerable<object[]> DefinedOpcodes()
    {
        var cycles = Cycles.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        for (var opcode = 0; opcode < 256; opcode++)
        {
            if (cycles[opcode] != 0)
            {
                yield return [(byte)opcode, cycles[opcode]];
            }
        }
    }

    [Theory]
    [MemberData(nameof(DefinedOpcodes))]
    public void EveryDefinedBaseOpcodeExecutesInTheSpecifiedTime(byte opcode, int expectedCycles)
    {
        var m = new CpuTestMachine([opcode, 0, 0], af: 0);
        Assert.Equal(expectedCycles, m.Cpu.StepInstruction());
        Assert.Equal((ulong)expectedCycles, m.State.TotalTCycles);
        Assert.Equal(0, m.State.F & 0x0F);
        Assert.False(m.Cpu.IsFaulted);
    }

    // Expected CB results per group and operation for a target of 81 (H holds C1).
    private static readonly byte[][] Results81 =
    [
        [0x03, 0xC0, 0x03, 0xC0, 0x02, 0xC0, 0x18, 0x40], // Rotate/shift
        [0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81], // BIT
        [0x80, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x01], // RES
        [0x81, 0x83, 0x85, 0x89, 0x91, 0xA1, 0xC1, 0x81] // SET
    ];
    private static readonly byte[][] ResultsC1 =
    [
        [0x83, 0xE0, 0x83, 0xE0, 0x82, 0xE0, 0x1C, 0x60],
        [0xC1, 0xC1, 0xC1, 0xC1, 0xC1, 0xC1, 0xC1, 0xC1],
        [0xC0, 0xC1, 0xC1, 0xC1, 0xC1, 0xC1, 0x81, 0x41],
        [0xC1, 0xC3, 0xC5, 0xC9, 0xD1, 0xE1, 0xC1, 0xC1]
    ];
    private static readonly byte[][] Flags81 =
    [
        [0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0, 0x10],
        [0x30, 0xB0, 0xB0, 0xB0, 0xB0, 0xB0, 0xB0, 0x30],
        [0xF0, 0xF0, 0xF0, 0xF0, 0xF0, 0xF0, 0xF0, 0xF0],
        [0xF0, 0xF0, 0xF0, 0xF0, 0xF0, 0xF0, 0xF0, 0xF0]
    ];

    public static IEnumerable<object[]> CbOpcodes()
    {
        for (var group = 0; group < 4; group++)
        {
            for (var operation = 0; operation < 8; operation++)
            {
                for (var register = 0; register < 8; register++)
                {
                    var flags = register == 4 && group == 1 && operation == 6 ? (byte)0x30 : Flags81[group][operation];
                    yield return [(byte)((group * 64) + (operation * 8) + register), register,
                (register == 4 ? ResultsC1 : Results81)[group][operation], flags,
                register == 6 ? (group == 1 ? 12 : 16) : 8];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(CbOpcodes))]
    public void EveryCbOpcodeChangesOnlyItsTargetAndSpecifiedFlags(byte opcode, int register, byte result, byte flags, int cycles)
    {
        var m = new CpuTestMachine([0xCB, opcode], af: 0x81F0, bc: 0x8181, de: 0x8181, hl: 0xC181);
        m.Bus.WriteByte(0xC181, 0x81);
        var expected = m.State with { AF = (ushort)(0x8100 | flags), PC = 0x202, TotalTCycles = (ulong)cycles };
        expected = register switch
        {
            0 => expected with { BC = (ushort)((result << 8) | 0x81) },
            1 => expected with { BC = (ushort)(0x8100 | result) },
            2 => expected with { DE = (ushort)((result << 8) | 0x81) },
            3 => expected with { DE = (ushort)(0x8100 | result) },
            4 => expected with { HL = (ushort)((result << 8) | 0x81) },
            5 => expected with { HL = (ushort)(0xC100 | result) },
            7 => expected with { AF = (ushort)((result << 8) | flags) },
            _ => expected
        };
        Assert.Equal(cycles, m.Cpu.StepInstruction());
        Assert.Equal(expected, m.State);
        Assert.Equal(register == 6 ? result : 0x81, m.Bus.ReadByte(0xC181));
    }
}
