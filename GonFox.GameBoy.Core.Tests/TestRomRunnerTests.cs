namespace GonFox.GameBoy.Core;

[Trait("Category", "Unit")]
public sealed class TestRomRunnerTests
{
    [Fact]
    public void FibonacciRegistersAndExecutedMarkerPass()
    {
        var result = Run(0x01, 3, 5, 0x11, 8, 13, 0x21, 21, 34, 0x40);
        Assert.StartsWith("TIMEOUT", result.Outcome, StringComparison.Ordinal); // Pairs reversed.
        result = Run(0x01, 5, 3, 0x11, 13, 8, 0x21, 34, 21, 0x40);
        Assert.Equal("PASS", result.Outcome);
        Assert.Equal(56UL, result.State.TotalTCycles);
    }

    [Fact]
    public void MarkerAloneIsNotSuccess()
    {
        Assert.StartsWith("TIMEOUT", Run(0x40, 0x18, 0xFE).Outcome, StringComparison.Ordinal);
    }

    [Fact]
    public void HaltCannotPretendToExecuteTheMarkerAtItsNextAddress()
    {
        Assert.StartsWith("TIMEOUT", Run(0x01, 5, 3, 0x11, 13, 8, 0x21, 34, 21, 0x76, 0x40).Outcome, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureRegistersAreReported()
    {
        Assert.Equal("FAIL", Run(0x01, 0x42, 0x42, 0x11, 0x42, 0x42, 0x21, 0x42, 0x42, 0x40).Outcome);
    }

    [Fact]
    public void StopAndUndefinedInstructionsHaveDistinctDiagnostics()
    {
        Assert.StartsWith("STOP", Run(0x10, 0).Outcome, StringComparison.Ordinal);
        Assert.StartsWith("FAULT", Run(0xD3).Outcome, StringComparison.Ordinal);
    }

    [Fact]
    public void HostDeadlineCanStopBeforeExecutingAnyInstruction()
    {
        var result = TestRomRunner.Run(TestRom.Create(0x18, 0xFE), 1000, TimeSpan.Zero);
        Assert.Equal("TIMEOUT (host)", result.Outcome);
        Assert.Equal(0UL, result.State.TotalTCycles);
    }

    private static TestRomResult Run(params byte[] program) =>
        TestRomRunner.Run(TestRom.Create(program), 1000, TimeSpan.FromSeconds(1));
}
