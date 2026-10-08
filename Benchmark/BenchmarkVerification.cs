namespace Benchmark;

using GonFox.GameBoy.Core.Video;

internal static class BenchmarkVerification
{
    internal static object[] Run()
    {
        List<object> results = [];
        foreach (var scenario in Enum.GetValues<ExecutionCase>())
        {
            var workload = new ExecutionWorkload(scenario);
            var before = workload.System.Video.CompletedFrameCount;
            var cycles = workload.Run();
            var frames = workload.System.Video.CompletedFrameCount - before;
            var final = workload.System.CaptureState();
            var hash = ExecutionWorkload.Fingerprint(final);
            workload.Reset();
            if (ExecutionWorkload.Fingerprint(workload.System.CaptureState()) != ExecutionWorkload.Fingerprint(workload.Initial))
            {
                throw new InvalidOperationException($"Initial state changed: {scenario}");
            }

            var chunkedCycles = workload.Run(4096);
            if (cycles != chunkedCycles || hash != ExecutionWorkload.Fingerprint(workload.System.CaptureState()))
            {
                throw new InvalidOperationException($"Execution partition/replay mismatch: {scenario}");
            }

            // Counts halted T-cycles and active steps outside the timing, over the same span.
            workload.Reset();
            var target = workload.System.TotalTCycles + ExecutionWorkload.Cycles;
            long haltedCycles = 0, instructions = 0;
            while (workload.System.TotalTCycles < target)
            {
                var halted = workload.System.IsHalted;
                var consumed = workload.System.StepInstruction().ExecutedTCycles;
                if (consumed == 0)
                {
                    throw new InvalidOperationException("Stopped during verification.");
                }

                if (halted)
                {
                    haltedCycles += consumed;
                }
                else
                {
                    instructions++;
                }
            }
            if (hash != ExecutionWorkload.Fingerprint(workload.System.CaptureState()))
            {
                throw new InvalidOperationException("Step mismatch.");
            }

            results.Add(new
            {
                Scenario = scenario.ToString(),
                workload.RomHash,
                Input = final.Joypad,
                InitialTCycles = workload.Initial.TotalTCycles,
                InitialStateSha256 = ExecutionWorkload.Fingerprint(workload.Initial),
                ActualTCycles = cycles,
                Frames = frames,
                ActiveSteps = instructions,
                HaltedTCycles = haltedCycles,
                FinalStateSha256 = hash,
                BgraSha256 = ExecutionWorkload.Hash(final.Video.Published)
            });
        }
        foreach (var scenario in Enum.GetValues<PpuCase>())
        {
            var workload = new PpuWorkload(scenario);
            if (workload.Run() != PpuWorkload.Frames)
            {
                throw new InvalidOperationException("PPU frame count mismatch.");
            }

            var pixels = workload.Pixels();
            for (var y = 0; y < VideoOutput.Height; y++)
            {
                for (var x = 0; x < VideoOutput.Width; x++)
                {
                    for (var channel = 0; channel < 4; channel++)
                    {
                        if (pixels[(((y * 160) + x) * 4) + channel] != (channel == 3 ? 255 : PpuWorkload.Expected(scenario, x, y)))
                        {
                            throw new InvalidOperationException($"PPU pixel mismatch: {scenario} ({x},{y})");
                        }
                    }
                }
            }

            workload.Reset();
            workload.Run();
            if (!pixels.AsSpan().SequenceEqual(workload.Pixels()))
            {
                throw new InvalidOperationException("PPU replay mismatch.");
            }

            results.Add(new { Scenario = "Ppu" + scenario, TCycles = PpuWorkload.Cycles, PpuWorkload.Frames, BgraSha256 = ExecutionWorkload.Hash(pixels) });
        }
        foreach (var mbc1 in new[] { false, true })
        {
            var workload = new BusWorkload(mbc1);
            var expected = (4096 * 255 / 2) + (mbc1 ? 8191 : 4096);
            if (workload.Run() != expected || workload.Run() != expected)
            {
                throw new InvalidOperationException("Bus checksum mismatch.");
            }

            results.Add(new { Scenario = mbc1 ? "BusMbc1" : "BusRomOnly", Checksum = expected });
        }
        return results.ToArray();
    }
}
