namespace GonFox.GameBoy.Core;

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Video;

// Runs the mealybug-tearoom tests of PPU writes in mode 3 against their expected images.
[Trait("Category", "Rom")]
public sealed class MealybugTests(ITestOutputHelper output)
{
    private static readonly string DataRoot = Path.Combine(AppContext.BaseDirectory, "TestData");

    private static JsonDocument Manifest() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(DataRoot, "mealybug-manifest.json")));

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
    public void FrameAtTheBreakpointMatchesTheExpectedImage(string name)
    {
        using var manifest = Manifest();
        var root = manifest.RootElement;
        var test = root.GetProperty("tests").EnumerateArray().Single(test => test.GetProperty("name").GetString() == name);
        foreach (var file in root.GetProperty("files").EnumerateArray())
        {
            ReadVerified(file.GetProperty("path").GetString()!, file.GetProperty("sha256").GetString()!);
        }

        var image = ReadVerified(test.GetProperty("rom").GetString()!, test.GetProperty("romSha256").GetString()!);
        var expected = DmgAcid2Tests.DecodeReference(
            ReadVerified(test.GetProperty("expected").GetString()!, test.GetProperty("expectedSha256").GetString()!));
        ReadVerified(test.GetProperty("sourcePath").GetString()!, test.GetProperty("sourceSha256").GetString()!);

        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        var state = system.CaptureState();
        WriteBootRomVram(image, state.Ppu.Vram);
        system.RestoreState(state);
        var watch = Stopwatch.StartNew();
        Span<byte> opcode = stackalloc byte[1];
        var instructions = 0;
        while (true)
        {
            if ((instructions++ & 1023) == 0)
            {
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(root.GetProperty("timeoutSeconds").GetInt32()), $"{name}: host timeout");
            }

            Assert.True(system.TotalTCycles < root.GetProperty("maxTCycles").GetUInt64(), $"{name}: T-cycle timeout");
            var before = system.GetDebugSnapshot();
            system.CopyMemory(before.PC, opcode);
            var step = system.StepInstruction();
            Assert.False(step.IsStopped);
            if (opcode[0] == 0x40 && step.ExecutedTCycles == 4 && system.GetDebugSnapshot().PC == unchecked((ushort)(before.PC + 1)))
            {
                break; // LD B,B: the screenshot point.
            }
        }
        var actual = new byte[VideoOutput.BufferSize];
        system.Video.CopyLatestFrame(actual);
        int different = 0, first = -1;
        for (var i = 0; i < actual.Length; i++)
        {
            if (actual[i] != expected[i])
            {
                different++;
                if (first < 0)
                {
                    first = i;
                }
            }
        }

        if (different != 0)
        {
            var diagnostics = Path.Combine(AppContext.BaseDirectory, "TestResults");
            Directory.CreateDirectory(diagnostics);
            File.WriteAllBytes(Path.Combine(diagnostics, $"mealybug-{name}.bgra"), actual);
        }
        output.WriteLine($"{name} ({test.GetProperty("model").GetString()}): T={system.TotalTCycles}, " +
            $"BGRA SHA256={Convert.ToHexStringLower(SHA256.HashData(actual))}");
        Assert.True(different == 0, $"{name}: {different} bytes differ; first pixel ({(first / 4) % 160},{first / 640})");
    }

    // States captured at each instruction while a written line is drawn replay into the same future.
    [Theory]
    [InlineData("m3_lcdc_obj_en_change")]
    [InlineData("m3_scy_change")]
    [InlineData("m3_wx_4_change_sprites")]
    public void StatesWhileDrawingAWrittenLineReplay(string name)
    {
        using var manifest = Manifest();
        var test = manifest.RootElement.GetProperty("tests").EnumerateArray().Single(test => test.GetProperty("name").GetString() == name);
        var image = ReadVerified(test.GetProperty("rom").GetString()!, test.GetProperty("romSha256").GetString()!);
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        while (system.Video.CompletedFrameCount < 3)
        {
            system.StepInstruction();
        }

        while (system.GetDebugSnapshot() is not { Ly: 60, PpuMode: 3 })
        {
            system.StepInstruction();
        }

        var boundaries = 0;
        while (system.GetDebugSnapshot().PpuMode == 3)
        {
            var here = system.CaptureState();
            StateTests.ReplayTwice(system, s => s.RunForTCycles(70_224 * 2));
            system.RestoreState(here);
            system.StepInstruction();
            boundaries++;
        }
        Assert.True(boundaries >= 10, $"{boundaries} boundaries");
    }

    // Writes the logo and (R) tiles and map entries that the DMG boot ROM leaves in VRAM.
    internal static void WriteBootRomVram(byte[] rom, byte[] vram)
    {
        var address = 0x10;
        for (var i = 0x104; i < 0x134; i++)
        {
            foreach (var nibble in new[] { rom[i] >> 4, rom[i] & 15 })
            {
                var doubled = 0;
                for (var bit = 3; bit >= 0; bit--)
                {
                    doubled = (doubled << 2) | (((nibble >> bit) & 1) * 3);
                }

                vram[address] = vram[address + 2] = (byte)doubled;
                address += 4;
            }
        }

        byte[] registered = [0x3C, 0x42, 0xB9, 0xA5, 0xB9, 0xA5, 0x42, 0x3C];
        for (var row = 0; row < 8; row++)
        {
            vram[0x190 + (row * 2)] = registered[row];
        }

        vram[0x1910] = 0x19;
        for (var i = 0; i < 12; i++)
        {
            vram[0x1904 + i] = (byte)(1 + i);
            vram[0x1924 + i] = (byte)(13 + i);
        }
    }

    private static byte[] ReadVerified(string relativePath, string expectedHash)
    {
        var path = Path.Combine(DataRoot, "mealybug", relativePath);
        Assert.True(File.Exists(path), $"Missing test ROM: {relativePath}");
        var bytes = File.ReadAllBytes(path);
        Assert.True(Convert.ToHexStringLower(SHA256.HashData(bytes)) == expectedHash,
            $"SHA-256 mismatch in {relativePath}");
        return bytes;
    }
}
