namespace GonFox.GameBoy.Core;

using System.Security.Cryptography;

using GonFox.GameBoy.Core.Audio;
using GonFox.GameBoy.Core.Cartridge;

// The sound check ROM: CH1 left, CH2 right, CH3 and CH4 on both sides, half a second each.
[Trait("Category", "Rom")]
public sealed class SoundCheckTests
{
    internal const string Hash = "6af87bfdf71bdf121d3ee24c5213df7873cd20bfc9101e9311728e0f7c7798c0";
    private const int Phase = AudioOutput.SampleRate / 2; // Frames per phase.

    internal static byte[] Image()
    {
        var image = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "sound-check.gb"));
        Assert.Equal(Hash, Convert.ToHexStringLower(SHA256.HashData(image)));
        return image;
    }

    [Fact]
    public void PhasesPlayLeftThenRightThenBothSides()
    {
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(Image()).Cartridge);
        var pcm = new List<short>();
        var buffer = new short[AudioOutput.CapacityFrames * AudioOutput.ChannelCount];
        while (system.TotalTCycles < 5UL * GameBoySystem.TCyclesPerSecond / 2)
        {
            system.RunForTCycles(4096);
            pcm.AddRange(buffer.AsSpan(0, system.Audio.ReadFrames(buffer) * 2));
        }

        // Checks each phase past its start, where the high-pass is still settling.
        for (var phase = 0; phase < 5; phase++)
        {
            var (left, right) = Window(pcm, (phase * Phase) + (Phase / 5), (phase * Phase) + (Phase * 9 / 10));
            switch (phase % 4)
            {
                case 0:
                    Assert.True(Rms(left) > 1_000); Assert.All(right, sample => Assert.Equal(0, sample)); break;
                case 1:
                    Assert.True(Rms(right) > 1_000); Assert.All(left, sample => Assert.Equal(0, sample)); break;
                default:
                    Assert.True(Rms(left) > 500);
                    Assert.All(left.Zip(right), pair => Assert.InRange(pair.First - pair.Second, -1, 1)); break;
            }
        }
    }

    private static (short[] Left, short[] Right) Window(List<short> pcm, int from, int to) =>
        (Enumerable.Range(from, to - from).Select(i => pcm[i * 2]).ToArray(), Enumerable.Range(from, to - from).Select(i => pcm[(i * 2) + 1]).ToArray());

    private static double Rms(IEnumerable<short> samples) => Math.Sqrt(samples.Average(sample => (double)sample * sample));
}
