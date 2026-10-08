namespace GonFox.GameBoy.Core;

using System.Security.Cryptography;
using System.Text.Json;

using GonFox.GameBoy.Core.Cartridge;

// Runs the pinned SameBoy DMG boot ROM from power-on to its hand-over to the cartridge.
[Trait("Category", "Rom")]
public sealed class SameBoyBootRomTests
{
    private static readonly string DataRoot = Path.Combine(AppContext.BaseDirectory, "TestData");
    private static readonly byte[] Trademark = [0x3C, 0x42, 0xB9, 0xA5, 0xB9, 0xA5, 0x42, 0x3C];

    private static JsonDocument Manifest() => JsonDocument.Parse(File.ReadAllText(Path.Combine(DataRoot, "sameboy-boot-manifest.json")));

    private static byte[] ReadVerified(string name, string expectedHash)
    {
        var path = Path.Combine(DataRoot, "sameboy", name);
        Assert.True(File.Exists(path), $"Missing test ROM: sameboy/{name}");
        var bytes = File.ReadAllBytes(path);
        Assert.True(Convert.ToHexStringLower(SHA256.HashData(bytes)) == expectedHash, $"SHA-256 mismatch in sameboy/{name}");
        return bytes;
    }

    private static byte[] BootRom()
    {
        using var manifest = Manifest();
        return ReadVerified(manifest.RootElement.GetProperty("bootRom").GetString()!, manifest.RootElement.GetProperty("bootRomSha256").GetString()!);
    }

    private static byte Read(GameBoySystem system, ushort address)
    {
        var value = new byte[1];
        system.CopyMemory(address, value);
        return value[0];
    }

    // Builds the logo tiles, each nibble a row with every bit doubled, followed by the (R) tile.
    private static byte[] ExpectedTiles(ReadOnlySpan<byte> logo)
    {
        var tiles = new byte[0x1A0];
        var at = 0x10;
        foreach (var value in logo)
        {
            foreach (var nibble in new[] { value >> 4, value & 0x0F })
            {
                var doubled = 0;
                for (var bit = 3; bit >= 0; bit--)
                {
                    doubled = (doubled << 2) | (((nibble >> bit) & 1) * 3);
                }

                tiles[at] = tiles[at + 2] = (byte)doubled;
                at += 4;
            }
        }
        foreach (var row in Trademark)
        {
            tiles[at] = row;
            at += 2;
        }
        return tiles;
    }

    [Fact]
    public void FilesMatchTheManifest()
    {
        using var manifest = Manifest();
        var root = manifest.RootElement;
        Assert.Equal(GameBoySystem.BootRomSize, ReadVerified(root.GetProperty("bootRom").GetString()!, root.GetProperty("bootRomSha256").GetString()!).Length);
        var license = ReadVerified(root.GetProperty("licenseEntry").GetString()!, root.GetProperty("licenseSha256").GetString()!);
        Assert.Equal(license, File.ReadAllBytes(Path.Combine(DataRoot, "SameBoy-LICENSE.txt")));
    }

    [Fact]
    public void TheBootRomHandsOverToTheCartridgeWithItsDocumentedState()
    {
        using var manifest = Manifest();
        var maxTCycles = manifest.RootElement.GetProperty("maxTCycles").GetUInt64();
        var cartridge = TestRom.Create(0x18, 0xFE);
        for (var i = 0; i < 0x30; i++)
        {
            cartridge[0x104 + i] = (byte)((i * 0x35) + 0x1B); // Every nibble value; not checked.
        }

        TestRom.UpdateHeaderChecksum(cartridge);
        var system = new GameBoySystem();
        system.UseBootRom(BootRom());
        system.InsertCartridge(CartridgeLoader.Load(cartridge).Cartridge);
        while (system.GetDebugSnapshot().PC != 0x0100 && system.TotalTCycles < maxTCycles)
        {
            system.StepInstruction();
        }

        var s = system.GetDebugSnapshot();
        Assert.True(s.PC == 0x0100, $"No hand-over within {maxTCycles} T: PC={s.PC:X4}");
        Assert.False(system.IsBootRomMapped);
        Assert.Equal((0x01B0, 0x0013, 0x00D8, 0x014D, 0xFFFE), (s.AF, s.BC, s.DE, s.HL, s.SP));
        Assert.Equal(cartridge[0x14D], Read(system, 0x014D));
        Assert.Equal((0x91, 0xFC, 0x00), (Read(system, 0xFF40), Read(system, 0xFF47), Read(system, 0xFF42)));
        Assert.Equal((0x77, 0xF3, 0xF1), (Read(system, 0xFF24), Read(system, 0xFF25), Read(system, 0xFF26)));
        Assert.Equal(0xFF, Read(system, 0xFF50));
        Assert.Equal((144, 1), (s.Ly, s.PpuMode));
        var low = new byte[0x100];
        system.CopyMemory(0x0000, low);
        Assert.Equal(cartridge.AsSpan(0, 0x100).ToArray(), low);

        var tiles = new byte[0x1800];
        system.CopyMemory(0x8000, tiles);
        var expected = new byte[0x1800];
        ExpectedTiles(cartridge.AsSpan(0x104, 0x30)).CopyTo(expected, 0);
        Assert.Equal(expected, tiles);
        var map = new byte[0x800];
        system.CopyMemory(0x9800, map);
        var expectedMap = new byte[0x800];
        for (var i = 0; i < 12; i++)
        {
            expectedMap[0x104 + i] = (byte)(1 + i);
            expectedMap[0x124 + i] = (byte)(13 + i);
        }
        expectedMap[0x110] = 0x19;
        Assert.Equal(expectedMap, map);
    }

    [Fact]
    public void MooneyeBootRegsPassesAfterTheBootRom()
    {
        using var mooneye = JsonDocument.Parse(File.ReadAllText(Path.Combine(DataRoot, "mooneye-manifest.json")));
        var test = mooneye.RootElement.GetProperty("tests").EnumerateArray()
            .Single(test => test.GetProperty("name").GetString() == "boot_regs-dmgABC");
        var path = Path.Combine(DataRoot, "mooneye", test.GetProperty("path").GetString()!);
        Assert.True(File.Exists(path), $"Missing test ROM: {path}");
        var image = File.ReadAllBytes(path);
        Assert.Equal(test.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(image)));
        using var manifest = Manifest();
        var result = TestRomRunner.Run(image, manifest.RootElement.GetProperty("maxTCycles").GetUInt64() + test.GetProperty("maxTCycles").GetUInt64(),
            TimeSpan.FromSeconds(manifest.RootElement.GetProperty("timeoutSeconds").GetInt32() + test.GetProperty("timeoutSeconds").GetInt32()), BootRom());
        Assert.True(result.Outcome == "PASS", result.ToString());
    }
}
