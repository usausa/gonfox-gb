namespace GonFox.GameBoy.Core;

[Trait("Category", "Unit")]
public sealed class CpuControlFlowTests
{
    public static IEnumerable<object[]> ConditionalBranches()
    {
        (string Kind, byte[] Opcodes, int Taken, int NotTaken)[] groups =
        [
            // ReSharper disable once UseUtf8StringLiteral
            ("JR", [0x20, 0x28, 0x30, 0x38], 12, 8),
            ("JP", [0xC2, 0xCA, 0xD2, 0xDA], 16, 12),
            ("CALL", [0xC4, 0xCC, 0xD4, 0xDC], 24, 12),
            ("RET", [0xC0, 0xC8, 0xD0, 0xD8], 20, 8)
        ];
        byte[] passing = [0, 0x80, 0, 0x10];
        byte[] failing = [0x80, 0, 0x10, 0];
        foreach (var (kind, opcodes, taken, notTaken) in groups)
            for (var condition = 0; condition < 4; condition++)
            {
                yield return [kind, opcodes[condition], passing[condition], true, taken];
                yield return [kind, opcodes[condition], failing[condition], false, notTaken];
            }
    }

    [Theory]
    [MemberData(nameof(ConditionalBranches))]
    public void ConditionalBranchesHaveCorrectPcStackAndTiming(string kind, byte opcode, byte flags, bool taken, int cycles)
    {
        var m = new CpuTestMachine([opcode, 0xFE, 0x02], af: flags);
        m.Bus.WriteByte(0xD000, 0xFE);
        m.Bus.WriteByte(0xD001, 0x02);
        m.Bus.WriteByte(0xCFFE, 0xAA);
        m.Bus.WriteByte(0xCFFF, 0xBB);
        var expectedPc = kind switch
        {
            "JR" => taken ? 0x200 : 0x202,
            "RET" => taken ? 0x2FE : 0x201,
            _ => taken ? 0x2FE : 0x203
        };
        var expectedSp = taken && kind == "CALL" ? 0xCFFE : taken && kind == "RET" ? 0xD002 : 0xD000;

        Assert.Equal(cycles, m.Cpu.StepInstruction());
        Assert.Equal(expectedPc, m.State.PC);
        Assert.Equal(expectedSp, m.State.SP);
        Assert.Equal(flags, m.State.F);
        Assert.Equal(taken && kind == "CALL" ? 0x03 : 0xAA, m.Bus.ReadByte(0xCFFE));
        Assert.Equal(taken && kind == "CALL" ? 0x02 : 0xBB, m.Bus.ReadByte(0xCFFF));
    }

    [Fact]
    public void CallThenReturnResumesAfterTheThreeByteCall()
    {
        var m = new CpuTestMachine([0xCD, 0x08, 0x02, 0x3E, 0x42, 0x76, 0, 0, 0x3E, 0x99, 0xC9]);
        Assert.Equal(24, m.Cpu.StepInstruction());
        Assert.Equal(0x208, m.State.PC);
        Assert.Equal(8, m.Cpu.StepInstruction());
        Assert.Equal(0x99, m.State.A);
        Assert.Equal(16, m.Cpu.StepInstruction());
        Assert.Equal(0x203, m.State.PC);
        Assert.Equal(0xD000, m.State.SP);
        Assert.Equal(8, m.Cpu.StepInstruction());
        Assert.Equal(0x42, m.State.A);
        Assert.Equal(4, m.Cpu.StepInstruction());
        Assert.True(m.State.IsHalted);
        Assert.Equal(60UL, m.State.TotalTCycles);
    }

    [Theory]
    [InlineData(0xC7, 0x00)]
    [InlineData(0xCF, 0x08)]
    [InlineData(0xD7, 0x10)]
    [InlineData(0xDF, 0x18)]
    [InlineData(0xE7, 0x20)]
    [InlineData(0xEF, 0x28)]
    [InlineData(0xF7, 0x30)]
    [InlineData(0xFF, 0x38)]
    public void RstPushesNextAddressAndSelectsItsVector(byte opcode, ushort vector)
    {
        var m = new CpuTestMachine([opcode]);
        Assert.Equal(16, m.Cpu.StepInstruction());
        Assert.Equal(vector, m.State.PC);
        Assert.Equal(0xCFFE, m.State.SP);
        Assert.Equal(0x01, m.Bus.ReadByte(0xCFFE));
        Assert.Equal(0x02, m.Bus.ReadByte(0xCFFF));
    }

    [Theory]
    [InlineData(0x00, 0x202)]
    [InlineData(0x7F, 0x281)]
    [InlineData(0x80, 0x182)]
    [InlineData(0xFE, 0x200)]
    public void RelativeJumpIsSignedAndRelativeToNextInstruction(byte offset, ushort pc)
    {
        var m = new CpuTestMachine([0x18, offset]);
        Assert.Equal(12, m.Cpu.StepInstruction());
        Assert.Equal(pc, m.State.PC);
    }

    [Fact]
    public void JumpHlUsesRegisterValueWithoutReadingTargetMemory()
    {
        var m = new CpuTestMachine([0xE9], hl: 0xA100);
        Assert.Equal(4, m.Cpu.StepInstruction());
        Assert.Equal(0xA100, m.State.PC);
        Assert.Single(m.Accesses); // Opcode fetch only.
    }

    [Fact]
    public void CallAndReturnTransferStackBytesInBusOrder()
    {
        var m = new CpuTestMachine([0xCD, 0x08, 0x02, 0, 0, 0, 0, 0, 0xC9], sp: 0xA002);
        m.Cpu.StepInstruction();
        m.Cpu.StepInstruction();
        Assert.Equal(new (bool, ushort, byte, ulong)[]
        {
            (false, 0x200, 0xCD, 0), (false, 0x201, 0x08, 4), (false, 0x202, 0x02, 8),
            (true, 0xA001, 0x02, 16), (true, 0xA000, 0x03, 20),
            (false, 0x208, 0xC9, 24), (false, 0xA000, 0x03, 28), (false, 0xA001, 0x02, 32)
        }, m.Accesses);
        Assert.Equal(40UL, m.State.TotalTCycles);
        Assert.Equal(0x203, m.State.PC);
        Assert.Equal(0xA002, m.State.SP);
    }
}
