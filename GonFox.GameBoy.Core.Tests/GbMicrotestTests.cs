namespace GonFox.GameBoy.Core;

using System.Security.Cryptography;
using System.Text.Json;

// Runs the DMG-confirmed gbmicrotest ROMs that the manifest selects.
[Trait("Category", "Rom")]
public sealed class GbMicrotestTests(ITestOutputHelper output)
{
    private static readonly string DataRoot = Path.Combine(AppContext.BaseDirectory, "TestData");

    private static JsonDocument Manifest() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(DataRoot, "gbmicrotest-manifest.json")));

    public static IEnumerable<object[]> Cases()
    {
        using var manifest = Manifest();
        foreach (var test in manifest.RootElement.GetProperty("tests").EnumerateArray())
        {
            yield return [test.GetProperty("name").GetString()!];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SelectedRomPassesWithItsResultBytes(string name)
    {
        using var manifest = Manifest();
        var root = manifest.RootElement;
        var test = root.GetProperty("tests").EnumerateArray().Single(test => test.GetProperty("name").GetString() == name);
        var image = ReadVerified(test.GetProperty("path").GetString()!, test.GetProperty("sha256").GetString()!);
        ReadVerified(test.GetProperty("sourcePath").GetString()!, test.GetProperty("sourceSha256").GetString()!);
        ReadVerified("LICENSE", root.GetProperty("licenseSha256").GetString()!);
        var result = TestRomRunner.RunMicrotest(image, root.GetProperty("maxTCycles").GetUInt64(),
            TimeSpan.FromSeconds(root.GetProperty("timeoutSeconds").GetInt32()));
        output.WriteLine($"{name} ({test.GetProperty("hardware").GetString()}): {result}");
        Assert.True(result.Outcome == test.GetProperty("expected").GetString(),
            $"{name}: {result} actual={result.HighRam[0]:X2} expected={result.HighRam[1]:X2}");
    }

    private static byte[] ReadVerified(string relativePath, string expectedHash)
    {
        var path = Path.Combine(DataRoot, "gbmicrotest", relativePath);
        Assert.True(File.Exists(path), $"Missing test ROM: {relativePath}");
        var bytes = File.ReadAllBytes(path);
        Assert.True(Convert.ToHexStringLower(SHA256.HashData(bytes)) == expectedHash,
            $"SHA-256 mismatch in {relativePath}");
        return bytes;
    }
}
