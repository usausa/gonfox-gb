namespace Benchmark;

using BenchmarkDotNet.Attributes;

using GonFox.GameBoy.Core;
using GonFox.GameBoy.Core.Video;

[InvocationCount(1)]
public class ExecutionBenchmarks
{
    [ParamsAllValues]
    public ExecutionCase Scenario { get; set; }
    private ExecutionWorkload workload = null!;

    [GlobalSetup]
    public void Setup() => workload = new(Scenario);

    [IterationSetup]
    public void Reset() => workload.Reset();

    [Benchmark]
    public long RunFourSeconds() => workload.Run();
}

[InvocationCount(1)]
public class PpuBenchmarks
{
    [ParamsAllValues]
    public PpuCase Scenario { get; set; }
    private PpuWorkload workload = null!;

    [GlobalSetup]
    public void Setup() => workload = new(Scenario);

    [IterationSetup]
    public void Reset() => workload.Reset();

    [Benchmark]
    public ulong Render240Frames() => workload.Run();
}

public class BusBenchmarks
{
    [Params(false, true)]
    public bool Mbc1 { get; set; }
    private BusWorkload workload = null!;

    [GlobalSetup]
    public void Setup() => workload = new(Mbc1);

    [Benchmark]
    public int ReadWrite4096() => workload.Run();
}

public class StateBenchmarks
{
    [Params(ExecutionCase.Background, ExecutionCase.Mbc1Background)]
    public ExecutionCase Scenario { get; set; }
    private ExecutionWorkload workload = null!;
    private readonly byte[] pixels = new byte[VideoOutput.BufferSize];
    private byte[] bytes = [];

    [GlobalSetup]
    public void Setup()
    {
        workload = new(Scenario);
        bytes = workload.Initial.Serialize();
    }

    [Benchmark]
    public GameBoyState Capture() => workload.System.CaptureState();

    [Benchmark]
    public byte[] Serialize() => workload.Initial.Serialize();

    [Benchmark]
    public GameBoyState Deserialize() => GameBoyState.Deserialize(bytes);

    [Benchmark]
    public ulong Restore()
    {
        workload.Reset();
        return workload.System.TotalTCycles;
    }

    [Benchmark]
    public byte CopyFrame()
    {
        workload.System.Video.CopyLatestFrame(pixels);
        return pixels[0];
    }
}
