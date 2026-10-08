namespace GonFox.GameBoy.Core;

using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Video;

[Trait("Category", "Rom")]
public sealed class DmgAcid2Tests(ITestOutputHelper output)
{
    private static readonly int[] PngBitDepths = [1, 2, 8];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedFramesMatchOfficialDmgReferencePixelForPixel(bool instructionSteps)
    {
        var dataRoot = Path.Combine(AppContext.BaseDirectory, "TestData");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataRoot, "dmg-acid2-manifest.json")));
        var settings = manifest.RootElement;
        var image = ReadVerified("dmg-acid2.gb", settings.GetProperty("binarySha256").GetString()!);
        byte[] reference = [];
        foreach (var file in settings.GetProperty("files").EnumerateArray())
        {
            var path = file.GetProperty("path").GetString()!;
            var bytes = ReadVerified(path, file.GetProperty("sha256").GetString()!);
            if (path == "img/reference-dmg.png")
            {
                reference = DecodeReference(bytes);
            }
        }
        Assert.Equal(VideoOutput.BufferSize, reference.Length);
        var system = new GameBoySystem();
        system.InsertCartridge(CartridgeLoader.Load(image).Cartridge);
        var target = settings.GetProperty("frames").GetUInt64();
        var watch = Stopwatch.StartNew();
        var calls = 0;

        // Compares three consecutive frames, then again after Reset.
        for (var run = 0; run < 2; run++)
        {
            var first = system.Video.CompletedFrameCount + target;
            for (var frame = first; frame < first + 3; frame++)
            {
                while (system.Video.CompletedFrameCount < frame)
                {
                    if ((calls++ & 1023) == 0)
                    {
                        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(settings.GetProperty("timeoutSeconds").GetInt32()), "dmg-acid2 host timeout");
                    }

                    Assert.True(system.TotalTCycles < settings.GetProperty("maxTCycles").GetUInt64(), "dmg-acid2 T-cycle timeout");
                    var result = instructionSteps ? system.StepInstruction() : system.RunForTCycles(4096);
                    Assert.False(result.IsStopped);
                }
                var actual = new byte[VideoOutput.BufferSize];
                system.Video.CopyLatestFrame(actual);
                int different = 0, firstDifference = -1;
                for (var i = 0; i < actual.Length; i++)
                {
                    if (actual[i] != reference[i])
                    {
                        different++;
                        if (firstDifference < 0)
                        {
                            firstDifference = i;
                        }
                    }
                }

                if (different != 0)
                {
                    var diagnostics = Path.Combine(AppContext.BaseDirectory, "TestResults");
                    Directory.CreateDirectory(diagnostics);
                    File.WriteAllBytes(Path.Combine(diagnostics, $"dmg-acid2-{instructionSteps}.bgra"), actual);
                }
                Assert.True(different == 0, $"{different} different bytes; first pixel ({(firstDifference / 4) % 160},{firstDifference / 640}), frame {frame}, {system.GetDebugSnapshot()}");
                output.WriteLine($"steps={instructionSteps}, run={run}, frame={frame}, T={system.TotalTCycles}, BGRA SHA256={Convert.ToHexStringLower(SHA256.HashData(actual))}");
            }
            system.Reset();
        }
    }

    private static byte[] ReadVerified(string path, string hash)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "TestData", "dmg-acid2", path);
        Assert.True(File.Exists(fullPath), $"Missing dmg-acid2 fixture: {path}");
        var bytes = File.ReadAllBytes(fullPath);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        return bytes;
    }

    // Decodes a 160x144 grayscale PNG of 1, 2 or 8 bits into BGRA, with .NET alone.
    internal static byte[] DecodeReference(byte[] png)
    {
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        using var compressed = new MemoryStream();
        var depth = 0;
        for (var offset = 8; offset < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            ReadOnlySpan<byte> type = png.AsSpan(offset + 4, 4);
            ReadOnlySpan<byte> data = png.AsSpan(offset + 8, length);
            if (type.SequenceEqual("IHDR"u8))
            {
                Assert.Equal(160, BinaryPrimitives.ReadInt32BigEndian(data));
                Assert.Equal(144, BinaryPrimitives.ReadInt32BigEndian(data[4..]));
                depth = data[8];
                Assert.Contains(depth, PngBitDepths);
                // ReSharper disable once UseUtf8StringLiteral
                Assert.Equal(new byte[] { 0, 0, 0, 0 }, data[9..].ToArray()); // Grayscale, deflate, adaptive filters, no interlace.
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                compressed.Write(data);
            }

            offset += length + 12;
        }
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        int stride = 160 * depth / 8, bytes = (depth / 8) + (depth < 8 ? 1 : 0); // Filter bytes per pixel, at least 1.
        var packed = new byte[(stride + 1) * 144];
        zlib.ReadExactly(packed);
        Assert.Equal(-1, zlib.ReadByte());
        byte[] rows = new byte[stride * 144], pixels = new byte[VideoOutput.BufferSize];
        for (var y = 0; y < 144; y++)
        {
            var filter = packed[y * (stride + 1)];
            for (var x = 0; x < stride; x++)
            {
                var a = x >= bytes ? rows[(y * stride) + x - bytes] : 0;
                var b = y > 0 ? rows[((y - 1) * stride) + x] : 0;
                var c = x >= bytes && y > 0 ? rows[((y - 1) * stride) + x - bytes] : 0;
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
                var row = y * stride;
                var gray = depth switch
                {
                    1 => (byte)(((rows[row + (x / 8)] >> (7 - (x % 8))) & 1) * 255),
                    2 => (byte)(((rows[row + (x / 4)] >> (6 - (2 * (x % 4)))) & 3) * 85),
                    _ => rows[row + x]
                };
                Assert.True(gray % 85 == 0, $"PNG shade {gray} at ({x},{y})");
                var offset = (y * 640) + (x * 4);
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = gray;
                pixels[offset + 3] = 255;
            }
        }
        return pixels;
    }
}
