namespace GonFox.GameBoy.Core;

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Devices;

// Drives the rtc3test menus with the buttons and reads each verdict from the 9C00 tile map.
[Trait("Category", "Rom")]
public sealed class Rtc3TestTests(ITestOutputHelper output)
{
    private const int Frame = 70_224;

    [Fact]
    public void EveryTestOfTheThreeMenusPassesOnTheRomsOwnTerms()
    {
        var dataRoot = Path.Combine(AppContext.BaseDirectory, "TestData");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataRoot, "rtc3test-manifest.json")));
        var settings = manifest.RootElement;
        var image = ReadVerified("rtc3test.gb", settings.GetProperty("binarySha256").GetString()!);
        foreach (var file in settings.GetProperty("files").EnumerateArray())
        {
            ReadVerified(file.GetProperty("path").GetString()!, file.GetProperty("sha256").GetString()!);
        }

        var maxTCycles = settings.GetProperty("maxTCycles").GetUInt64();
        var timeout = TimeSpan.FromSeconds(settings.GetProperty("timeoutSeconds").GetInt32());
        var watch = Stopwatch.StartNew();
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(image).Cartridge);

        void RunUntil(Func<bool> condition, string waitingFor)
        {
            while (!condition())
            {
                Assert.True(system.TotalTCycles < maxTCycles, $"rtc3test T-cycle limit while waiting for {waitingFor}");
                Assert.True(watch.Elapsed < timeout, $"rtc3test host timeout while waiting for {waitingFor}");
                Assert.False(system.RunForTCycles(Frame).IsStopped);
            }
        }

        // Holds a button for six frames, then releases it for six.
        void Press(JoypadButton button)
        {
            system.Joypad.SetButtonState(button, true);
            system.RunForTCycles(6 * Frame);
            system.Joypad.SetButtonState(button, false);
            system.RunForTCycles(6 * Frame);
        }

        // The menu lists one entry per row from row 2, with the cursor in column 1.
        bool MenuShows(int selected) => Row(system, 0).StartsWith("  MBC3 RTC test ROM", StringComparison.Ordinal) && Row(system, 2 + selected)[1] == '>';

        int menuIndex = 0, passed = 0;
        RunUntil(() => MenuShows(0), "the menu");
        foreach (var menu in settings.GetProperty("menus").EnumerateArray())
        {
            var name = menu.GetProperty("name").GetString()!;
            if (menuIndex > 0)
            {
                Press(JoypadButton.Down);
                var selected = menuIndex;
                RunUntil(() => MenuShows(selected), $"the cursor on {name}");
            }
            Press(JoypadButton.A);
            RunUntil(() => Row(system, 17).StartsWith("      * Return", StringComparison.Ordinal) && Row(system, 0).Trim() == name, $"the results of {name}");
            var results = Results(system);
            foreach (var (label, result, verdict) in results) output.WriteLine($"{name} / {label}: {result} ({verdict})");
            Assert.Equal(menu.GetProperty("tests").EnumerateArray().Select(test => test.GetString()), results.Select(r => r.Label));
            Assert.All(results, result => Assert.Equal(Verdict.Pass, result.Verdict));
            passed += results.Count;
            Press(JoypadButton.A);
            var shown = menuIndex;
            RunUntil(() => MenuShows(shown), "the menu again");
            menuIndex++;
        }
        output.WriteLine($"{passed} tests passed, T={system.TotalTCycles} ({system.TotalTCycles / (double)GameBoySystem.TCyclesPerSecond:F1} s), host {watch.Elapsed.TotalSeconds:F1} s");
        Assert.Equal(23, passed);
    }

    private enum Verdict
    {
        Pass,
        Fail,
        NotApplicable
    }

    // Reads each test's label and result; the colour bits of the result tiles give the verdict.
    private static List<(string Label, string Result, Verdict Verdict)> Results(GameBoySystem system)
    {
        var map = new byte[32 * 18];
        system.CopyMemory(0x9C00, map);
        List<(string, string, Verdict)> results = [];
        var label = string.Empty;
        for (var row = 2; row < 17; row++)
        {
            ReadOnlySpan<byte> tiles = map.AsSpan(row * 32, 20);
            var start = 20; // First coloured tile.
            for (var column = 0; column < 20; column++)
            {
                if ((tiles[column] & 0xC0) is 0x40 or 0x80)
                {
                    start = column;
                    break;
                }
            }

            var plain = Decode(tiles[..start]).TrimEnd();
            var skipped = start == 20 && plain.EndsWith("N/A", StringComparison.Ordinal);
            if (skipped)
            {
                plain = plain[..^3].TrimEnd();
            }

            if (plain.Length > 0)
            {
                label = plain.TrimEnd(':'); // Result may follow on the next row.
            }

            if (skipped)
            {
                results.Add((label, "N/A", Verdict.NotApplicable));
            }
            else if (start < 20)
            {
                results.Add((label, Decode(tiles[start..]),
                    tiles[start..].ToArray().All(tile => (tile & 0xC0) == 0x40) ? Verdict.Pass : Verdict.Fail));
            }
        }
        return results;
    }

    private static string Row(GameBoySystem system, int row)
    {
        var tiles = new byte[20];
        system.CopyMemory((ushort)(0x9C00 + (row * 32)), tiles);
        return Decode(tiles);
    }

    // Decodes tiles with the ROM's character map, ignoring the colour bits.
    private static string Decode(ReadOnlySpan<byte> tiles)
    {
        var text = new StringBuilder();
        foreach (var tile in tiles)
        {
            text.Append(tile switch
            {
                0xC0 => '>',
                0xC1 => '*',
                0xC2 => ':',
                0xC3 => '/',
                0xC4 => '-',
                0xFF => ' ',
                _ => (tile & 0x3F) switch
                {
                    < 0x0A and var digit => (char)('0' + digit),
                    < 0x24 and var upper => (char)('A' + upper - 0x0A),
                    < 0x3E and var lower => (char)('a' + lower - 0x24),
                    0x3E => '.',
                    _ => ' '
                }
            });
        }
        return text.ToString();
    }

    private static byte[] ReadVerified(string path, string hash)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "TestData", "rtc3test", path);
        Assert.True(File.Exists(fullPath), $"Missing rtc3test fixture: {path}");
        var bytes = File.ReadAllBytes(fullPath);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        return bytes;
    }
}
