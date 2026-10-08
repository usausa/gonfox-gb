namespace GonFox.GameBoy.Core;

[Trait("Category", "Unit")]
public sealed class CpuArithmeticTests
{
    [Theory]
    [InlineData(0xCE, 0x0F10, 0x00, 0x10, 0x20)]
    [InlineData(0xCE, 0xFF10, 0x00, 0x00, 0xB0)]
    [InlineData(0xCE, 0x0000, 0x00, 0x00, 0x80)]
    [InlineData(0xCE, 0x0010, 0xFF, 0x00, 0xB0)]
    [InlineData(0xD6, 0x1010, 0x01, 0x0F, 0x60)]
    [InlineData(0xD6, 0x0010, 0x01, 0xFF, 0x70)]
    [InlineData(0xD6, 0x0110, 0x01, 0x00, 0xC0)]
    [InlineData(0xD6, 0x8100, 0x01, 0x80, 0x40)]
    [InlineData(0xDE, 0x1010, 0x0F, 0x00, 0xE0)]
    [InlineData(0xDE, 0x0010, 0xFF, 0x00, 0xF0)]
    [InlineData(0xDE, 0x1000, 0x0F, 0x01, 0x60)]
    [InlineData(0xDE, 0x8010, 0x00, 0x7F, 0x60)]
    [InlineData(0xE6, 0xF0F0, 0x0F, 0x00, 0xA0)]
    [InlineData(0xE6, 0xF0F0, 0xF0, 0xF0, 0x20)]
    [InlineData(0xEE, 0xAAF0, 0xAA, 0x00, 0x80)]
    [InlineData(0xEE, 0xAAF0, 0x55, 0xFF, 0x00)]
    [InlineData(0xF6, 0x00F0, 0x00, 0x00, 0x80)]
    [InlineData(0xF6, 0xAAF0, 0x55, 0xFF, 0x00)]
    [InlineData(0xFE, 0x1010, 0x01, 0x10, 0x60)]
    [InlineData(0xFE, 0x0010, 0x01, 0x00, 0x70)]
    [InlineData(0xFE, 0x4210, 0x42, 0x42, 0xC0)]
    public void ImmediateAluUsesCorrectCarryBorrowAndFlags(byte opcode, ushort af, byte operand, byte result, byte flags)
    {
        var m = new CpuTestMachine([opcode, operand], af);
        Assert.Equal(8, m.Cpu.StepInstruction());
        Assert.Equal(result, m.State.A);
        Assert.Equal(flags, m.State.F);
        Assert.Equal(0x202, m.State.PC);
    }

    [Theory]
    [InlineData(0x80, 0x11, 0x00)]
    [InlineData(0x88, 0x12, 0x00)]
    [InlineData(0x90, 0x0F, 0x60)]
    [InlineData(0x98, 0x0E, 0x60)]
    [InlineData(0xA0, 0x00, 0xA0)]
    [InlineData(0xA8, 0x11, 0x00)]
    [InlineData(0xB0, 0x11, 0x00)]
    [InlineData(0xB8, 0x10, 0x60)]
    public void AluGroupsAgreeForRegisterAndIndirectOperand(byte opcode, byte result, byte flags)
    {
        foreach (var form in new[] { opcode, (byte)(opcode + 6) })
        {
            var m = new CpuTestMachine([form], af: 0x1010, bc: 0x0100);
            m.Bus.WriteByte(0xC100, 1);
            Assert.Equal(form == opcode ? 4 : 8, m.Cpu.StepInstruction());
            Assert.Equal(result, m.State.A);
            Assert.Equal(flags, m.State.F);
            Assert.Equal(1, m.Bus.ReadByte(0xC100));
        }
    }

    [Theory]
    [InlineData(0x04, 0x0F, 0x10, 0x30)]
    [InlineData(0x04, 0xFF, 0x00, 0xB0)]
    [InlineData(0x04, 0x7F, 0x80, 0x30)]
    [InlineData(0x05, 0x00, 0xFF, 0x70)]
    [InlineData(0x05, 0x01, 0x00, 0xD0)]
    [InlineData(0x05, 0x10, 0x0F, 0x70)]
    public void IncDecPreserveCarryForRegistersAndMemory(byte opcode, byte value, byte result, byte flags)
    {
        foreach (var form in new[] { opcode, (byte)(opcode + 0x30) })
        {
            var m = new CpuTestMachine([form], af: 0x00F0, bc: (ushort)(value << 8));
            m.Bus.WriteByte(0xC100, value);
            Assert.Equal(form == opcode ? 4 : 12, m.Cpu.StepInstruction());
            Assert.Equal(result, form == opcode ? m.State.B : m.Bus.ReadByte(0xC100));
            Assert.Equal(flags, m.State.F);
        }
    }

    [Theory]
    [InlineData(0x0FFF, 0x0001, 0x1000, 0xA0)]
    [InlineData(0xFFFF, 0x0001, 0x0000, 0xB0)]
    [InlineData(0x8000, 0x8000, 0x0000, 0x90)]
    [InlineData(0x0001, 0x0001, 0x0002, 0x80)]
    public void AddHlUsesBit11HalfCarryAndPreservesZero(ushort hl, ushort operand, ushort result, byte flags)
    {
        var m = new CpuTestMachine([0x09], af: 0x00F0, bc: operand, hl: hl);
        Assert.Equal(8, m.Cpu.StepInstruction());
        Assert.Equal(result, m.State.HL);
        Assert.Equal(flags, m.State.F);
    }

    [Theory]
    [InlineData(0x09, 0x2345)]
    [InlineData(0x19, 0x3456)]
    [InlineData(0x29, 0x2222)]
    [InlineData(0x39, 0x5678)]
    public void AddHlSelectsAllFourWordSources(byte opcode, ushort expected)
    {
        var m = new CpuTestMachine([opcode], af: 0, bc: 0x1234, de: 0x2345, hl: 0x1111, sp: 0x4567);
        Assert.Equal(8, m.Cpu.StepInstruction());
        Assert.Equal(expected, m.State.HL);
        Assert.Equal(0, m.State.F);
        m.Reset([0x09], af: 0, bc: 1, hl: 0xFFFF);
        m.Cpu.StepInstruction();
        Assert.Equal(0, m.State.HL);
        Assert.Equal(0x30, m.State.F); // Z stays clear.
    }

    [Theory]
    [InlineData(0x0000, 0xFF, 0xFFFF, 0x00)]
    [InlineData(0x0001, 0xFF, 0x0000, 0x30)]
    [InlineData(0xFFF8, 0x08, 0x0000, 0x30)]
    [InlineData(0x00FF, 0x01, 0x0100, 0x30)]
    [InlineData(0x000F, 0x01, 0x0010, 0x20)]
    [InlineData(0x0080, 0x80, 0x0000, 0x10)]
    [InlineData(0x8000, 0x7F, 0x807F, 0x00)]
    [InlineData(0x8000, 0x80, 0x7F80, 0x00)]
    public void SignedSpArithmeticHasUnsignedLowByteFlags(ushort sp, byte offset, ushort result, byte flags)
    {
        foreach (var opcode in new byte[] { 0xE8, 0xF8 })
        {
            var m = new CpuTestMachine([opcode, offset], af: 0x00F0, sp: sp);
            Assert.Equal(opcode == 0xE8 ? 16 : 12, m.Cpu.StepInstruction());
            Assert.Equal(result, opcode == 0xE8 ? m.State.SP : m.State.HL);
            Assert.Equal(opcode == 0xE8 ? 0xC100 : sp, opcode == 0xE8 ? m.State.HL : m.State.SP);
            Assert.Equal(flags, m.State.F);
        }
    }

    [Theory]
    [InlineData(0x9A00, 0x0090)]
    [InlineData(0x0020, 0x0600)]
    [InlineData(0xFA40, 0xFA40)] // Invalid BCD with N: unchanged.
    [InlineData(0x0060, 0xFA40)]
    [InlineData(0x0050, 0xA050)]
    [InlineData(0x0070, 0x9A50)]
    [InlineData(0x9910, 0xF910)]
    public void DaaHandlesExplicitFlagCombinations(ushort af, ushort expected)
    {
        var m = new CpuTestMachine([0x27], af);
        Assert.Equal(4, m.Cpu.StepInstruction());
        Assert.Equal(expected, m.State.AF);
    }

    [Fact]
    public void DaaMatchesDecimalArithmeticForAllTwoDigitOperands()
    {
        var m = new CpuTestMachine([0]);
        foreach (var subtract in new[] { false, true })
        {
            foreach (var carry in new[] { 0, 1 })
            {
                for (var left = 0; left < 100; left++)
                {
                    for (var right = 0; right < 100; right++)
                    {
                        var decimalResult = subtract ? left - right - carry : left + right + carry;
                        var wrapped = (decimalResult + 100) % 100;
                        var expected = PackedBcd(wrapped);
                        var flags = (subtract ? 0x40 : 0) | (wrapped == 0 ? 0x80 : 0) |
                            (decimalResult < 0 || decimalResult >= 100 ? 0x10 : 0);
                        m.Reset([(byte)(subtract ? 0xDE : 0xCE), PackedBcd(right), 0x27],
                            (ushort)((PackedBcd(left) << 8) | (carry << 4)));
                        m.Cpu.StepInstruction();
                        Assert.Equal(4, m.Cpu.StepInstruction());
                        Assert.Equal((ushort)((expected << 8) | flags), m.State.AF);
                    }
                }
            }
        }
    }

    private static byte PackedBcd(int value) => (byte)(((value / 10) * 16) + (value % 10));
}
