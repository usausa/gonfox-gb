namespace GonFox.GameBoy.Core;

using System.Security.Cryptography;
using System.Text.Json;

[Trait("Category", "Rom")]
public sealed class MooneyeTests(ITestOutputHelper output)
{
    private static readonly string DataRoot = Path.Combine(AppContext.BaseDirectory, "TestData");

    public static IEnumerable<object[]> Cases()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(DataRoot, "mooneye-manifest.json")));
        foreach (var test in manifest.RootElement.GetProperty("tests").EnumerateArray())
        {
            yield return [test.GetProperty("name").GetString()!];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SelectedRomPassesWithOfficialRegisterProtocol(string name)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(DataRoot, "mooneye-manifest.json")));
        var test = manifest.RootElement.GetProperty("tests").EnumerateArray()
            .Single(test => test.GetProperty("name").GetString() == name);
        var image = ReadVerified(test.GetProperty("path").GetString()!, test.GetProperty("sha256").GetString()!);
        ReadVerified(test.GetProperty("sourcePath").GetString()!, test.GetProperty("sourceSha256").GetString()!);
        ReadVerified("LICENSE", manifest.RootElement.GetProperty("licenseSha256").GetString()!);
        var result = TestRomRunner.Run(image, test.GetProperty("maxTCycles").GetUInt64(),
            TimeSpan.FromSeconds(test.GetProperty("timeoutSeconds").GetInt32()));
        output.WriteLine($"{name}: {result}");
        Assert.True(result.Outcome == test.GetProperty("expected").GetString(), $"{name}: {result}");
    }

    private static byte[] ReadVerified(string relativePath, string expectedHash)
    {
        var path = Path.Combine(DataRoot, "mooneye", relativePath);
        Assert.True(File.Exists(path), $"Missing test ROM: {relativePath}");
        var bytes = File.ReadAllBytes(path);
        Assert.True(Convert.ToHexStringLower(SHA256.HashData(bytes)) == expectedHash,
            $"SHA-256 mismatch in {relativePath}");
        return bytes;
    }
}
