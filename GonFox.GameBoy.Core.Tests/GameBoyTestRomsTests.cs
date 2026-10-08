namespace GonFox.GameBoy.Core;

using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Video;

// Runs the AGE, firstwhite and SameSuite ROMs taken from the game-boy-test-roms collection.
[Trait("Category", "Rom")]
public sealed class GameBoyTestRomsTests
{
    private static readonly string DataRoot = Path.Combine(AppContext.BaseDirectory, "TestData");
    private static readonly Lazy<JsonElement> Manifest = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(DataRoot, "game-boy-test-roms-manifest.json"))).RootElement);
    private static readonly Lazy<Dictionary<string, string>> Hashes = new(() => Manifest.Value.GetProperty("files").EnumerateArray()
        .ToDictionary(file => file.GetProperty("path").GetString()!, file => file.GetProperty("sha256").GetString()!));

    public static IEnumerable<object[]> AgeCases() =>
        Manifest.Value.GetProperty("age").GetProperty("selected").EnumerateArray().Select(name => new object[] { name.GetString()! });

    public static IEnumerable<object[]> SameSuiteCases() =>
        Manifest.Value.GetProperty("sameSuite").GetProperty("selected").EnumerateArray().Select(name => new object[] { name.GetString()! });

    [Fact]
    public void FilesAndLicensesMatchTheManifest()
    {
        foreach (var license in Manifest.Value.GetProperty("licenses").EnumerateArray())
        {
            Assert.Equal(license.GetProperty("sha256").GetString(),
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(DataRoot, license.GetProperty("path").GetString()!)))));
        }

        foreach (var path in Hashes.Value.Keys)
        {
            Assert.NotEmpty(Read(path));
        }
    }

    [Theory]
    [MemberData(nameof(AgeCases))]
    public void AgeTestPassesOnDmgC(string name)
    {
        var system = Start(Read(name));
        RunToBreakpoint(system, Manifest.Value.GetProperty("age").GetProperty("maxTCycles").GetUInt64(), name);
        var png = name[..name.LastIndexOf('.')] + "-dmgC.png";
        if (!Hashes.Value.ContainsKey(png))
        {
            var registers = system.GetDebugSnapshot();
            Assert.True(registers.BC == 0x0305 && registers.DE == 0x080D && registers.HL == 0x1522,
                $"{name}: BC={registers.BC:X4} DE={registers.DE:X4} HL={registers.HL:X4}");
            return;
        }

        var frame = new byte[VideoOutput.BufferSize];
        system.Video.CopyLatestFrame(frame);
        var reference = DecodeRgb(Read(png));
        for (var i = 0; i < reference.Length; i++)
        {
            Assert.True(Rgb(frame, i % 160, i / 160) == (reference[i] & 0xF8F8F8), $"{name}: differs at ({i % 160},{i / 160})");
        }
    }

    [Theory]
    [MemberData(nameof(SameSuiteCases))]
    public void SameSuiteTestPassesOnDmg(string name)
    {
        var system = Start(Read(name));
        RunToBreakpoint(system, Manifest.Value.GetProperty("sameSuite").GetProperty("maxTCycles").GetUInt64(), name);
        var registers = system.GetDebugSnapshot();
        Assert.True(registers.BC == 0x0305 && registers.DE == 0x080D && registers.HL == 0x1522,
            $"{name}: BC={registers.BC:X4} DE={registers.DE:X4} HL={registers.HL:X4}");
    }

    // Every shown image is white, since the first frame after each LCD enable is never shown.
    [Fact]
    public void FirstwhiteNeverShowsItsFrame()
    {
        var firstwhite = Manifest.Value.GetProperty("firstwhite");
        var system = Start(Read(firstwhite.GetProperty("rom").GetString()!));
        var reference = DecodeRgb(Read(firstwhite.GetProperty("expected").GetString()!));
        ulong end = firstwhite.GetProperty("tCycles").GetUInt64(), sequence = system.Video.Sequence;
        var frame = new byte[VideoOutput.BufferSize];
        var shown = 0;
        while (system.TotalTCycles < end)
        {
            system.RunForTCycles(456);
            if (system.Video.Sequence == sequence)
            {
                continue;
            }

            sequence = system.Video.Sequence;
            shown++;
            system.Video.CopyLatestFrame(frame);
            for (var i = 0; i < reference.Length; i++)
            {
                Assert.True(Rgb(frame, i % 160, i / 160) == (reference[i] & 0xF8F8F8), $"image {shown} differs at ({i % 160},{i / 160})");
            }
        }
        Assert.True(system.Video.CompletedFrameCount >= 10 && shown >= 10, $"{system.Video.CompletedFrameCount} frames, {shown} images");
    }

    // Reads a file of the collection, checked against the manifest's hash.
    private static byte[] Read(string path)
    {
        var bytes = File.ReadAllBytes(Path.Combine(DataRoot, "game-boy-test-roms", path));
        Assert.Equal(Hashes.Value[path], Convert.ToHexStringLower(SHA256.HashData(bytes)));
        return bytes;
    }

    private static GameBoySystem Start(byte[] image)
    {
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        return system;
    }

    // Runs until LD B,B executes, which ends an AGE or SameSuite test.
    private static void RunToBreakpoint(GameBoySystem system, ulong maxTCycles, string name)
    {
        var watch = Stopwatch.StartNew();
        Span<byte> opcode = stackalloc byte[1];
        for (var instructions = 0; ; instructions++)
        {
            if ((instructions & 1023) == 0)
            {
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(Manifest.Value.GetProperty("timeoutSeconds").GetInt32()), $"{name}: host timeout");
            }

            Assert.True(system.TotalTCycles < maxTCycles, $"{name}: T-cycle limit");
            var before = system.GetDebugSnapshot();
            system.CopyMemory(before.PC, opcode);
            var step = system.StepInstruction();
            Assert.False(system.IsFaulted, $"{name}: undefined opcode");
            if (opcode[0] == 0x40 && step.ExecutedTCycles == 4 && system.GetDebugSnapshot().PC == unchecked((ushort)(before.PC + 1)))
            {
                return;
            }
        }
    }

    private static uint Rgb(byte[] frame, int x, int y)
    {
        var offset = ((y * 160) + x) * 4; // BGRA
        return (uint)((frame[offset + 2] << 16) | (frame[offset + 1] << 8) | frame[offset]) & 0xF8F8F8;
    }

    // Decodes a 160x144 8-bit RGB or RGBA PNG without interlace.
    internal static uint[] DecodeRgb(byte[] png)
    {
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        using var compressed = new MemoryStream();
        var channels = 0;
        for (var offset = 8; offset < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            ReadOnlySpan<byte> type = png.AsSpan(offset + 4, 4), data = png.AsSpan(offset + 8, length);
            if (type.SequenceEqual("IHDR"u8))
            {
                Assert.Equal(160, BinaryPrimitives.ReadInt32BigEndian(data));
                Assert.Equal(144, BinaryPrimitives.ReadInt32BigEndian(data[4..]));
                Assert.Equal(8, data[8]);
                channels = data[9] switch { 2 => 3, 6 => 4, _ => throw new InvalidDataException($"PNG colour type {data[9]}") };
                // ReSharper disable once UseUtf8StringLiteral
                Assert.Equal(new byte[] { 0, 0, 0 }, data[10..13].ToArray()); // Deflate, adaptive filters, no interlace.
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                compressed.Write(data);
            }

            offset += length + 12;
        }
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        var stride = 160 * channels;
        var packed = new byte[(stride + 1) * 144];
        zlib.ReadExactly(packed);
        var rows = new byte[stride * 144];
        var pixels = new uint[160 * 144];
        for (var y = 0; y < 144; y++)
        {
            var filter = packed[y * (stride + 1)];
            for (var x = 0; x < stride; x++)
            {
                var a = x >= channels ? rows[(y * stride) + x - channels] : 0;
                var b = y > 0 ? rows[((y - 1) * stride) + x] : 0;
                var c = x >= channels && y > 0 ? rows[((y - 1) * stride) + x - channels] : 0;
                int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                var predictor = filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => pa <= pb && pa <= pc ? a : pb <= pc ? b : c,
                    _ => throw new InvalidDataException("PNG filter")
                };
                rows[(y * stride) + x] = unchecked((byte)(packed[(y * (stride + 1)) + x + 1] + predictor));
            }
            for (var x = 0; x < 160; x++)
            {
                var at = (y * stride) + (x * channels);
                pixels[(y * 160) + x] = (uint)((rows[at] << 16) | (rows[at + 1] << 8) | rows[at + 2]);
            }
        }
        return pixels;
    }
}
