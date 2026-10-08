#:project ../../GonFox.GameBoy.Core/GonFox.GameBoy.Core.csproj
#:property PublishAot=false

using System.Numerics;
using System.Text.Json;
using GonFox.GameBoy.Core;
using GonFox.GameBoy.Core.Audio;
using GonFox.GameBoy.Core.Cartridge;

// Compares a ROM's sound with a binjgb recording: <rom.gb> <binjgb.f32> <seconds> <output folder>.
const int Rate = 44_100, Hop = 441, Size = 4096, Skip = 100; // Skips the first second.
string romPath = args[0], theirsPath = args[1], output = args[3];
double seconds = double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
Directory.CreateDirectory(output);

short[] pcm = Render(File.ReadAllBytes(romPath), seconds);
WriteWav(Path.Combine(output, "ours.wav"), pcm, AudioOutput.SampleRate);
double[][] ours = Resample(pcm, AudioOutput.SampleRate, Rate);
double[][] theirs = ReadBinjgb(theirsPath);
if (theirs[0].Length < Rate * 4) throw new InvalidDataException($"The binjgb recording is too short: {theirs[0].Length} frames.");
foreach (double[] side in ours.Concat(theirs)) HighPass(side);
WriteWav(Path.Combine(output, "binjgb.wav"), Interleave(theirs), Rate);

double[] envelopeOurs = Envelope(ours), envelopeTheirs = Envelope(theirs);
const int Span = 800;
int lag = Enumerable.Range(0, Math.Max(1, envelopeTheirs.Length - Span - Skip))
    .MaxBy(l => Correlation(envelopeOurs.AsSpan(Skip, Span), envelopeTheirs.AsSpan(l + Skip, Span)));
int frames = Math.Min(envelopeOurs.Length - Skip, envelopeTheirs.Length - lag - Skip);
double envelopeCorrelation = Correlation(envelopeOurs.AsSpan(Skip, frames), envelopeTheirs.AsSpan(lag + Skip, frames));

int offset = lag * Hop, windows = 0, peaksEqual = 0;
var similarity = new List<double>[] { [], [] };
var ratios = new List<(double Ours, double Theirs)>();
double[] hann = Enumerable.Range(0, Size).Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Size - 1))).ToArray();
int low = (int)Math.Floor(60.0 * Size / Rate) + 1, high = (int)Math.Ceiling(5000.0 * Size / Rate) - 1; // 60 Hz < f < 5 kHz
for (int start = Hop * Skip; start + Size < Math.Min(ours[0].Length, theirs[0].Length - offset); start += Size)
{
    windows++;
    for (int side = 0; side < 2; side++)
    {
        double[] a = Magnitudes(ours[side].AsSpan(start, Size), hann), b = Magnitudes(theirs[side].AsSpan(offset + start, Size), hann);
        double dot = 0, na = 0, nb = 0;
        int peakA = low, peakB = low;
        for (int k = low; k <= high; k++)
        {
            dot += a[k] * b[k]; na += a[k] * a[k]; nb += b[k] * b[k];
            if (a[k] > a[peakA]) peakA = k;
            if (b[k] > b[peakB]) peakB = k;
        }
        if (na > 0 && nb > 0) similarity[side].Add(dot / Math.Sqrt(na * nb));
        double fa = peakA * (double)Rate / Size, fb = peakB * (double)Rate / Size;
        if (Math.Abs(fa - fb) <= Math.Max(12, 0.02 * fa)) peaksEqual++;
    }
    ratios.Add((Rms(ours[0].AsSpan(start, Size)) / Rms(ours[1].AsSpan(start, Size)),
        Rms(theirs[0].AsSpan(offset + start, Size)) / Rms(theirs[1].AsSpan(offset + start, Size))));
}

var result = new
{
    Rom = Path.GetFileName(romPath), Seconds = seconds, BinjgbStartLateMs = lag * 10, EnvelopeCorrelation = Math.Round(envelopeCorrelation, 4),
    Windows = windows,
    Left = Summary(similarity[0]), Right = Summary(similarity[1]),
    StrongestFrequencyMatches = peaksEqual, StrongestFrequencyWindows = windows * 2,
    LeftRightRmsRatio = new { Ours = Math.Round(Median(ratios.Select(r => r.Ours)), 3), Binjgb = Math.Round(Median(ratios.Select(r => r.Theirs)), 3) },
};
string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(output, "metrics.json"), json);
Console.WriteLine(json);
return 0;

static short[] Render(byte[] rom, double seconds)
{
    var system = new GameBoySystem();
    system.InsertCartridge(CartridgeLoader.Load(rom).Cartridge);
    ulong end = (ulong)(seconds * GameBoySystem.TCyclesPerSecond);
    var all = new List<short>();
    short[] buffer = new short[AudioOutput.CapacityFrames * AudioOutput.ChannelCount];
    while (system.TotalTCycles < end)
    {
        system.RunForTCycles(4096);
        int read = system.Audio.ReadFrames(buffer);
        all.AddRange(buffer.AsSpan(0, read * 2));
    }
    return [.. all];
}

// Resamples linearly, which suffices for the band below 5 kHz.
static double[][] Resample(short[] pcm, int from, int to)
{
    int frames = pcm.Length / 2, count = (int)((long)(frames - 1) * to / from);
    var result = new double[][] { new double[count], new double[count] };
    for (int i = 0; i < count; i++)
    {
        double position = (double)i * from / to;
        int index = (int)position; double t = position - index;
        for (int side = 0; side < 2; side++)
            result[side][i] = (pcm[index * 2 + side] * (1 - t) + pcm[(index + 1) * 2 + side] * t) / 32768.0;
    }
    return result;
}

static double[][] ReadBinjgb(string path)
{
    byte[] bytes = File.ReadAllBytes(path);
    int frames = bytes.Length / 8;
    var result = new double[][] { new double[frames], new double[frames] };
    for (int i = 0; i < frames; i++)
    {
        result[1][i] = BitConverter.ToSingle(bytes, i * 8); // SO1 (right) comes first.
        result[0][i] = BitConverter.ToSingle(bytes, i * 8 + 4);
    }
    return result;
}

// Subtracts the centred 441-sample (10 ms) moving average, zeros beyond the ends.
static void HighPass(double[] x)
{
    double[] prefix = new double[x.Length + 1];
    for (int i = 0; i < x.Length; i++) prefix[i + 1] = prefix[i] + x[i];
    double[] average = new double[x.Length];
    for (int i = 0; i < x.Length; i++)
        average[i] = (prefix[Math.Min(x.Length, i + 221)] - prefix[Math.Max(0, i - 220)]) / 441;
    for (int i = 0; i < x.Length; i++) x[i] -= average[i];
}

static double[] Envelope(double[][] x)
{
    int count = x[0].Length / Hop;
    double[] result = new double[count];
    for (int n = 0; n < count; n++)
    {
        double sum = 0;
        for (int i = n * Hop; i < (n + 1) * Hop; i++) sum += x[0][i] * x[0][i] + x[1][i] * x[1][i];
        result[n] = Math.Sqrt(sum / (2 * Hop));
    }
    return result;
}

static double Correlation(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
{
    double meanA = 0, meanB = 0;
    for (int i = 0; i < a.Length; i++) { meanA += a[i]; meanB += b[i]; }
    meanA /= a.Length; meanB /= b.Length;
    double ab = 0, aa = 0, bb = 0;
    for (int i = 0; i < a.Length; i++) { double da = a[i] - meanA, db = b[i] - meanB; ab += da * db; aa += da * da; bb += db * db; }
    return aa > 0 && bb > 0 ? ab / Math.Sqrt(aa * bb) : 0;
}

static double[] Magnitudes(ReadOnlySpan<double> samples, double[] window)
{
    var data = new Complex[samples.Length];
    for (int i = 0; i < samples.Length; i++) data[i] = samples[i] * window[i];
    Fft(data);
    double[] result = new double[samples.Length / 2 + 1];
    for (int k = 0; k < result.Length; k++) result[k] = data[k].Magnitude;
    return result;
}

// Iterative radix-2 FFT in place.
static void Fft(Complex[] data)
{
    int n = data.Length;
    for (int i = 1, j = 0; i < n; i++)
    {
        int bit = n >> 1;
        for (; (j & bit) != 0; bit >>= 1) j ^= bit;
        j ^= bit;
        if (i < j) (data[i], data[j]) = (data[j], data[i]);
    }
    for (int length = 2; length <= n; length <<= 1)
    {
        Complex step = Complex.FromPolarCoordinates(1, -2 * Math.PI / length);
        for (int i = 0; i < n; i += length)
        {
            Complex w = Complex.One;
            for (int k = 0; k < length / 2; k++)
            {
                Complex even = data[i + k], odd = data[i + k + length / 2] * w;
                data[i + k] = even + odd; data[i + k + length / 2] = even - odd;
                w *= step;
            }
        }
    }
}

static double Rms(ReadOnlySpan<double> x)
{
    double sum = 0;
    foreach (double value in x) sum += value * value;
    return Math.Sqrt(sum / x.Length);
}

static double Median(IEnumerable<double> values) => Percentile(values, 0.5);

// Interpolates linearly between the closest ranks.
static double Percentile(IEnumerable<double> values, double p)
{
    double[] sorted = values.Order().ToArray();
    double position = p * (sorted.Length - 1);
    int index = (int)position;
    return index + 1 < sorted.Length ? sorted[index] + (sorted[index + 1] - sorted[index]) * (position - index) : sorted[index];
}

static object Summary(List<double> values) => new
{
    Mean = Math.Round(values.Average(), 3), Median = Math.Round(Median(values), 3), Percentile10 = Math.Round(Percentile(values, 0.1), 3),
};

static short[] Interleave(double[][] x)
{
    double peak = Math.Max(1e-9, x.Max(side => side.Max(Math.Abs)));
    short[] result = new short[x[0].Length * 2];
    for (int i = 0; i < x[0].Length; i++)
        for (int side = 0; side < 2; side++) result[i * 2 + side] = (short)Math.Round(x[side][i] / peak * 0.9 * 32767);
    return result;
}

static void WriteWav(string path, short[] samples, int rate)
{
    using var writer = new BinaryWriter(File.Create(path));
    writer.Write("RIFF"u8); writer.Write(36 + samples.Length * 2); writer.Write("WAVE"u8);
    writer.Write("fmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(rate);
    writer.Write(rate * 4); writer.Write((short)4); writer.Write((short)16);
    writer.Write("data"u8); writer.Write(samples.Length * 2);
    foreach (short sample in samples) writer.Write(sample);
}
