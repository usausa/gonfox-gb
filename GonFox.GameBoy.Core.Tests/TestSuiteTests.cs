namespace GonFox.GameBoy.Core;

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using GonFox.GameBoy.Core.Cartridge;

// Runs the homebrew test ROMs and compares their results, case by case, with the bytes SameBoy or binjgb showed for them.
[Trait("Category", "Rom")]
public sealed class TestSuiteTests(ITestOutputHelper output)
{
    private const ushort StatusAddress = 0xC000;
    private const ushort LengthAddress = 0xC00E;
    private const ushort ResultsAddress = 0xC010;
    private static readonly string DataRoot = Path.Combine(AppContext.BaseDirectory, "TestData", "TestSuite");

    public static IEnumerable<object[]> Roms() =>
        Directory.EnumerateFiles(Path.Combine(DataRoot, "reference"), "*.json").Order(StringComparer.Ordinal)
            .Select(path => new object[] { Path.GetFileNameWithoutExtension(path) });

    [Theory]
    [MemberData(nameof(Roms))]
    public void ResultsMatchTheReference(string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(DataRoot, "reference", name + ".json")));
        var reference = document.RootElement;
        var image = File.ReadAllBytes(Path.Combine(DataRoot, reference.GetProperty("rom").GetString()!));
        Assert.Equal(reference.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(image)));
        var source = reference.GetProperty("reference").GetString()!;
        var expected = Convert.FromHexString(reference.GetProperty(source).GetString()!);
        var cases = Cases(reference, expected.Length);
        var actual = Run(image, reference.GetProperty("maxTCycles").GetUInt64(), name);
        Assert.True(actual.Length == expected.Length, string.Create(CultureInfo.InvariantCulture, $"{name}: {actual.Length} result bytes, {source} showed {expected.Length}"));

        var differences = new StringBuilder();
        foreach (var test in cases)
        {
            var want = expected.AsSpan(test.Offset, test.Length);
            var got = actual.AsSpan(test.Offset, test.Length);
            if (test.Known is not null)
            {
                output.WriteLine($"{test.Name}: known difference ({test.Known}), expected {Convert.ToHexString(want)}, got {Convert.ToHexString(got)}");
            }
            else if (!want.SequenceEqual(got))
            {
                differences.AppendLine().Append(CultureInfo.InvariantCulture, $"{test.Name}: expected {Convert.ToHexString(want)}, got {Convert.ToHexString(got)}");
            }
        }

        Assert.True(differences.Length == 0, $"{name}: results differ from {source}{differences}");
    }

    // The named cases, in order and without gaps over all result bytes.
    private static List<Case> Cases(JsonElement reference, int length)
    {
        var cases = reference.GetProperty("cases").EnumerateArray()
            .Select(test => new Case(test.GetProperty("name").GetString()!, test.GetProperty("offset").GetInt32(), test.GetProperty("length").GetInt32(),
                test.TryGetProperty("known", out var known) ? known.GetString() : null))
            .ToList();
        var end = 0;
        foreach (var test in cases)
        {
            Assert.True(test.Offset == end && test.Length > 0, string.Create(CultureInfo.InvariantCulture, $"{test.Name}: offset {test.Offset}, length {test.Length}, expected offset {end}"));
            end += test.Length;
        }

        Assert.Equal(length, end);
        return cases;
    }

    // Runs the ROM until it marks its results complete, then reads them from WRAM.
    private static byte[] Run(byte[] image, ulong maxTCycles, string name)
    {
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        var watch = Stopwatch.StartNew();
        var status = new byte[1];
        while (true)
        {
            Assert.True(system.TotalTCycles < maxTCycles, $"{name}: T-cycle limit");
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), $"{name}: host timeout");
            system.RunForTCycles(70_224);
            system.CopyMemory(StatusAddress, status);
            if (status[0] == 1)
            {
                break;
            }
        }

        var length = new byte[2];
        system.CopyMemory(LengthAddress, length);
        var results = new byte[length[0] | (length[1] << 8)];
        system.CopyMemory(ResultsAddress, results);
        return results;
    }

    // A named range of result bytes; Known holds why GonFox differs from the reference there.
    private sealed record Case(string Name, int Offset, int Length, string? Known);
}
