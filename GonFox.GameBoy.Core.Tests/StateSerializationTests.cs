namespace GonFox.GameBoy.Core;

using System.Reflection;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Devices;

using static GonFox.GameBoy.Core.StateTests;

// Round trips of the GameBoyState byte form and rejection of damaged or foreign bytes.
[Trait("Category", "Unit")]
public sealed class StateSerializationTests
{
    private sealed record Scenario(byte[] Image, Action<GameBoySystem> Setup, Action<GameBoySystem> Future);

    private static Scenario Get(string name) => name switch
    {
        "cpu" => new(TestRom.Create(0x18, 0xFE), s => s.RunForTCycles(10_000), s => s.RunForTCycles(200_000)),
        "fault" => new(TestRom.Create(0xD3), s =>
        {
            s.StepInstruction();
            s.StepInstruction();
            Assert.True(s.IsFaulted);
        },
            s => s.RunForTCycles(200_000)), // Only time passes.
        "stop" => new(TestRom.Create(0xAF, 0xE0, 0x40, 0xE0, 0x00, 0x10, 0, 0x18, 0xFE), s => // LCD off, P1=$00, STOP.
        {
            for (var i = 0; i < 5; i++)
            {
                s.StepInstruction(); // The entry JP first.
            }

            Assert.True(s.IsStopped);
            s.Joypad.SetButtonState(JoypadButton.A, true); // A wake request is pending.
        }, s =>
        {
            s.RunForTCycles(4096);
            Assert.False(s.IsStopped);
        }),
        "dma-serial" => new(TestRom.Create(0x18, 0xFE), s =>
        {
            s.RunForTCycles(20);
            var seed = s.CaptureState();
            seed.Memory.HighRam[0] = 0x18;
            seed.Memory.HighRam[1] = 0xFE; // CPU loops in HRAM.
            s.RestoreState(seed with
            {
                Cpu = seed.Cpu with { PC = 0xFF80 },
                Serial = new(Data: 0xAA, Control: 0x81, Sent: 5, Clock: true, Bits: 3, Completed: [17, 23], Dropped: 4),
                Dma = seed.Dma with { Active = true, SourcePage = 0xC0, Register = 0xC0, Index = 17, PendingCycles = 1 },
                Ppu = seed.Ppu with { DmaActive = true }
            });
        }, s => s.RunForTCycles(160_000)),
        "mbc3-clock" => new(TestRom.CreateMbc3(0x10, 1, 3, 0xF3, 0x76), s =>
        {
            var cart = CartridgeOf(s);
            cart.Write(0, 0x0A);
            cart.Write(0x4000, 2);
            cart.Write(0xA123, 0x5A);
            Mbc3Tests.SetClock(cart, new(58, 59, 23, 0xFF, 0x01));
            Mbc3Tests.Latch(cart);
            s.RunForTCycles(GameBoySystem.CyclesPerSecond * 7 / 10); // In the middle of a second.
        }, s => s.RunForTCycles(GameBoySystem.CyclesPerSecond)),
        "mbc5-ram" => new(TestRom.CreateMbc5(0x1B, 2, 4, 0x18, 0xFE), s =>
        {
            var cart = CartridgeOf(s);
            cart.Write(0, 0x0A);
            cart.Write(0x2000, 7);
            for (var bank = 0; bank < 16; bank++)
            {
                cart.Write(0x4000, (byte)bank);
                cart.Write(0xBFFF, (byte)(bank * 13));
            }
        }, s => s.RunForTCycles(100_000)),
        "camera" => new(TestRom.CreateMbc1(0xFC, 5, 4, 0x18, 0xFE), s =>
        {
            var cart = CartridgeOf(s);
            cart.Write(0, 0x0A);
            cart.Write(0x4000, 3);
            cart.Write(0xA123, 0x5A);
            cart.Write(0x4000, 0x10);
            cart.Write(0xA002, 0x03);
            for (var register = 0xA006; register <= 0xA035; register++)
            {
                cart.Write((ushort)register, 0x81); // Grey: black.
            }

            cart.Write(0xA000, 0x03);
            s.RunForTCycles(100_000); // Mid-capture.
        }, s => s.RunForTCycles(200_000)), // Past the capture's end.
        _ => new(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "MegaDemo", "megademo.gb")),
            s => s.RunForTCycles(GameBoySystem.CyclesPerSecond * 2), s => s.RunForTCycles(300_000)) // Music and queued PCM.
    };

    private static readonly Dictionary<GameBoySystem, ICartridge> Inserted = [];

    private static ICartridge CartridgeOf(GameBoySystem system)
    {
        lock (Inserted)
        {
            return Inserted[system];
        }
    }

    private static GameBoySystem Start(byte[] image)
    {
        var system = new GameBoySystem();
        var cartridge = CartridgeLoader.Load(image).Cartridge;
        system.InsertCartridge(cartridge);
        lock (Inserted)
        {
            Inserted[system] = cartridge;
        }

        return system;
    }

    [Theory]
    [InlineData("cpu")]
    [InlineData("fault")]
    [InlineData("stop")]
    [InlineData("dma-serial")]
    [InlineData("mbc3-clock")]
    [InlineData("mbc5-ram")]
    [InlineData("camera")]
    [InlineData("megademo")]
    public void RoundTripKeepsEveryBlockTheBytesAndTheFuture(string name)
    {
        var scenario = Get(name);
        var system = Start(scenario.Image);
        scenario.Setup(system);
        var saved = system.CaptureState();
        var bytes = saved.Serialize();
        var copy = GameBoyState.Deserialize(bytes);
        EqualState(saved, copy);
        Assert.Equal(bytes, copy.Serialize());
        GameBoySystem original = Start(scenario.Image), restored = Start(scenario.Image);
        original.RestoreState(saved);
        restored.RestoreState(copy);
        EqualState(original.CaptureState(), restored.CaptureState());
        scenario.Future(original);
        scenario.Future(restored);
        EqualState(original.CaptureState(), restored.CaptureState());
    }

    [Fact]
    public void DamagedOrForeignBytesAreRejected()
    {
        var system = Start(TestRom.Create(0x18, 0xFE));
        system.RunForTCycles(10_000);
        var bytes = system.CaptureState().Serialize();
        byte[] Changed(Action<byte[]> change)
        {
            var copy = bytes.ToArray();
            change(copy);
            return copy;
        }

        Assert.Throws<InvalidDataException>(() => GameBoyState.Deserialize(Changed(b => b[0] ^= 1))); // Not "DMGSTATE".

        // Rejects an earlier and a later format.
        foreach (var version in new[] { 11, 13 })
        {
            var error = Assert.Throws<NotSupportedException>(() => GameBoyState.Deserialize(Changed(b => BitConverter.TryWriteBytes(b.AsSpan(8), version))));
            Assert.Contains($"format {version}", error.Message, StringComparison.Ordinal);
        }
        Assert.Throws<NotSupportedException>(() => GameBoyState.Deserialize(Changed(b => b[12] ^= 0x80))); // Fingerprint.
        foreach (var length in new[] { 0, 7, 8, 19, 20, 120, bytes.Length / 2, bytes.Length - 1 })
        {
            Assert.Throws<InvalidDataException>(() => GameBoyState.Deserialize(bytes.AsSpan(0, length)));
        }

        Assert.Throws<InvalidDataException>(() => GameBoyState.Deserialize([.. bytes, 0])); // Bytes after the end.
        Assert.Throws<InvalidDataException>(() => GameBoyState.Deserialize(new byte[(8 * 1024 * 1024) + 1]));

        // Offsets of the CPU and Bus blocks in the byte form.
        const int cpu = 20 + 4 + 3 + 4 + 13 + 4 + 64 + 1 + 8, memory = cpu + 1 + 12 + 5 + 1 + 1 + 1;
        Assert.Equal(1, bytes[cpu]);
        Assert.Equal(1, bytes[memory]);
        Assert.Equal(8192, BitConverter.ToInt32(bytes, memory + 1));
        Assert.Equal(127, BitConverter.ToInt32(bytes, memory + 1 + 4 + 8192));
        Assert.Throws<InvalidDataException>(() => GameBoyState.Deserialize(Changed(b => BitConverter.TryWriteBytes(b.AsSpan(20), int.MaxValue))));
        Assert.Throws<InvalidDataException>(() => GameBoyState.Deserialize(Changed(b => BitConverter.TryWriteBytes(b.AsSpan(20), -2))));
        Assert.Throws<InvalidDataException>(() => GameBoyState.Deserialize(Changed(b => b[cpu + 13] = 2))); // IME is a flag.
        Assert.Throws<InvalidDataException>(() => GameBoyState.Deserialize(Changed(b => BitConverter.TryWriteBytes(b.AsSpan(memory + 1), int.MaxValue))));
        Assert.Throws<InvalidDataException>(() => GameBoyState.Deserialize(Changed(b => BitConverter.TryWriteBytes(b.AsSpan(memory + 1), 1 << 20))));

        // A state without its Bus block reads fine but does not restore.
        var missing = GameBoyState.Deserialize(Changed(b => b[memory] = 0)[..(memory + 1)].Concat(bytes[(memory + 1 + 4 + 8192 + 4 + 127 + 4)..]).ToArray());
        Assert.Throws<ArgumentException>(() => system.RestoreState(missing));

        // A WRAM length of -1 (no array) reads fine; any other negative length does not.
        byte[] WithoutWorkRam(int length) => [.. bytes[..(memory + 1)], .. BitConverter.GetBytes(length), .. bytes[(memory + 1 + 4 + 8192)..]];
        Assert.Throws<ArgumentException>(() => system.RestoreState(GameBoyState.Deserialize(WithoutWorkRam(-1))));
        Assert.Throws<InvalidDataException>(() => GameBoyState.Deserialize(WithoutWorkRam(-2)));
    }

    // The serializer's trimming-safe record list must match the records its layout reaches.
    [Fact]
    public void RecordTypeListIsWhatTheLayoutReaches()
    {
        var reached = System.Text.RegularExpressions.Regex.Matches(StateSerializer.Layout, @"([A-Za-z0-9_.]+)\{")
            .Select(match => match.Groups[1].Value).Where(name => name != "GameBoyState").ToHashSet();
        var listed = StateSerializer.RecordTypes.Select(type => type.DeclaringType is { } outer ? $"{outer.Name}.{type.Name}" : type.Name).ToList();
        Assert.Equal(listed.Count, listed.Distinct().Count());
        Assert.Equal(reached.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ALengthBeyondTheBytesLeftAllocatesNothing()
    {
        var system = Start(TestRom.Create(0x18, 0xFE));
        system.RunForTCycles(1000);
        var state = system.CaptureState();
        state = state with { Audio = state.Audio with { Queued = [0x1234, 0x5678] } };
        var bytes = state.Serialize();
        var at = bytes.AsSpan().IndexOf(new byte[] { 0x34, 0x12, 0x78, 0x56 }) - 4;
        Assert.Equal(2, BitConverter.ToInt32(bytes, at)); // Queue length.
        BitConverter.TryWriteBytes(bytes.AsSpan(at), 1 << 20);
        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => GameBoyState.Deserialize(bytes));
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 1_500_000); // Not the 2 MiB of a 1M-element queue.
    }

    [Fact]
    public void RandomDamageNeverEscapesTheDocumentedErrors()
    {
        var system = Start(TestRom.CreateMbc3(0x10, 1, 3, 0x18, 0xFE));
        system.RunForTCycles(50_000);
        var before = system.CaptureState();
        var bytes = before.Serialize();
        var random = new Random(22);
        for (var trial = 0; trial < 300; trial++)
        {
            var damaged = bytes.ToArray();
            for (var flips = random.Next(1, 4); flips > 0; flips--)
            {
                damaged[random.Next(damaged.Length)] ^= (byte)(1 << random.Next(8));
            }

            try
            {
                system.RestoreState(GameBoyState.Deserialize(damaged)); // May still be valid.
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or ArgumentException)
            {
            }
            system.RestoreState(before);
        }
        EqualState(before, system.CaptureState());
    }

    // The serializer writes only constructor parameters, so every state record must be positional.
    [Fact]
    public void EveryStateRecordIsPositional()
    {
        var seen = new HashSet<Type>();
        var pending = new Queue<Type>(typeof(GameBoyState).GetProperties(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(p => p.Name != "EqualityContract").Select(p => p.PropertyType));
        while (pending.TryDequeue(out var type))
        {
            type = Nullable.GetUnderlyingType(type) ?? (type.IsArray ? type.GetElementType()! : type);
            if (type.IsPrimitive || type == typeof(string) || !seen.Add(type))
            {
                continue;
            }

            var constructor = type.GetConstructors().Single(c => c.GetParameters() is not [var only] || only.ParameterType != type);
            var parameters = constructor.GetParameters().Select(p => p.Name!).ToArray();
            var values = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(p => p.Name != "EqualityContract").Select(p => p.Name).ToArray();
            Assert.True(values.Order().SequenceEqual(parameters.Order()), $"{type}: {string.Join(",", values)} vs {string.Join(",", parameters)}");
            foreach (var parameter in constructor.GetParameters())
            {
                pending.Enqueue(parameter.ParameterType);
            }
        }
        Assert.True(seen.Count >= 15, $"Only {seen.Count} state records were reached.");
    }

    // Types whose layout text the test reads; never instantiated.
    public sealed record Sample(int Count, byte Flag);

    public sealed record Renamed(int Count, byte Mode);

    public sealed record Wider(int Count, ushort Flag);

    public sealed record Longer(int Count, byte Flag, bool Extra);

    [Fact]
    public void TheLayoutTextFollowsNamesTypesAndOrder()
    {
        var sample = StateSerializer.SchemaOf(typeof(Sample));
        Assert.Equal("StateSerializationTests.Sample{Count:i32;Flag:u8}", sample);
        Assert.NotEqual(sample[sample.IndexOf('{', StringComparison.Ordinal)..], StateSerializer.SchemaOf(typeof(Renamed))[StateSerializer.SchemaOf(typeof(Renamed)).IndexOf('{', StringComparison.Ordinal)..]);
        Assert.Contains("Flag:u16", StateSerializer.SchemaOf(typeof(Wider)), StringComparison.Ordinal);
        Assert.Contains("Extra:bool", StateSerializer.SchemaOf(typeof(Longer)), StringComparison.Ordinal);
        Assert.Contains("Rtc:RtcState{Current:RtcRegisters{", StateSerializer.Layout, StringComparison.Ordinal); // Nested records are spelled out.
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(StateSerializer.Layout))[..8], StateSerializer.Fingerprint);
    }
}
