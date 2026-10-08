using System.Text.Json;

using Benchmark;

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

if (args is ["--verify"])
{
    Console.WriteLine(JsonSerializer.Serialize(BenchmarkVerification.Run(), new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
var config = DefaultConfig.Instance.AddJob(Job.Default.WithRuntime(CoreRuntime.Core10_0)
    .WithLaunchCount(2).WithWarmupCount(5).WithIterationCount(10).WithUnrollFactor(1).WithId("Default"))
    .AddDiagnoser(MemoryDiagnoser.Default, new DisassemblyDiagnoser(new DisassemblyDiagnoserConfig(maxDepth: 2, printSource: true)))
    .AddExporter(JsonExporter.Full, CsvMeasurementsExporter.Default);
if (!args.Contains("--list") && !args.Contains("--help"))
{
    BenchmarkVerification.Run();
}

var summaries = BenchmarkSwitcher.FromAssembly(typeof(ExecutionBenchmarks).Assembly).Run(args, config).ToArray();
if (args.Contains("--list") || args.Contains("--help"))
{
    return 0;
}

// Returns 1 when a build or any case fails, which BDN alone does not report in its exit code.
return summaries.Length == 0 || summaries.Any(s => s.HasCriticalValidationErrors || s.Reports.Any(r => !r.Success || r.ResultStatistics is null)) ? 1 : 0;
