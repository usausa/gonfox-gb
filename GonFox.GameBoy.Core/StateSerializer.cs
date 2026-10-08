namespace GonFox.GameBoy.Core;

using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

using GonFox.GameBoy.Core.Audio;
using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Cpu;
using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Memory;
using GonFox.GameBoy.Core.Video;

using Timer = GonFox.GameBoy.Core.Devices.Timer;

// The byte form of a GameBoyState: records written field by field, guarded by a layout fingerprint.
internal static class StateSerializer
{
    internal const int MaxBytes = 8 * 1024 * 1024;
    private const int MaxElements = 1 << 20;
    private const int MaxStringBytes = 256;
    private static readonly byte[] Magic = "DMGSTATE"u8.ToArray();
    private static readonly Dictionary<Type, Codec> Codecs = [];

    // The record members the codecs reach by reflection.
    private const DynamicallyAccessedMemberTypes RecordMembers = DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicMethods | DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties;

    // Every record type a state holds, listed so that trimming keeps their reflected members.
    internal static readonly Type[] RecordTypes =
    [
        Keep<Sm83Cpu.State>(), Keep<MemoryBus.State>(), Keep<CartridgeState>(), Keep<RtcState>(), Keep<RtcRegisters>(),
        Keep<CameraState>(), Keep<Huc3State>(),
        Keep<Interrupts.State>(), Keep<Timer.State>(), Keep<Serial.State>(), Keep<Joypad.State>(), Keep<Apu.State>(),
        Keep<PulseChannel.State>(), Keep<WaveChannel.State>(), Keep<NoiseChannel.State>(), Keep<LengthCounter.State>(),
        Keep<VolumeEnvelope.State>(), Keep<AudioMixer.State>(), Keep<AudioOutput.State>(), Keep<OamDma.State>(),
        Keep<Ppu.State>(), Keep<Ppu.LineSprite>(), Keep<Ppu.Pipeline>(), Keep<VideoOutput.State>()
    ];

    private static Type Keep<[DynamicallyAccessedMembers(RecordMembers)] T>() => typeof(T);

    private static readonly Codec CpuCodec = For(typeof(Sm83Cpu.State));
    private static readonly Codec MemoryCodec = For(typeof(MemoryBus.State));
    private static readonly Codec CartridgeCodec = For(typeof(CartridgeState));
    private static readonly Codec InterruptsCodec = For(typeof(Interrupts.State));
    private static readonly Codec TimerCodec = For(typeof(Timer.State));
    private static readonly Codec SerialCodec = For(typeof(Serial.State));
    private static readonly Codec JoypadCodec = For(typeof(Joypad.State));
    private static readonly Codec ApuCodec = For(typeof(Apu.State));
    private static readonly Codec AudioCodec = For(typeof(AudioOutput.State));
    private static readonly Codec DmaCodec = For(typeof(OamDma.State));
    private static readonly Codec PpuCodec = For(typeof(Ppu.State));
    private static readonly Codec VideoCodec = For(typeof(VideoOutput.State));
    internal static readonly string Layout = "GameBoyState{Model:string;BootProfile:string;RomSha256:string;CartridgeType:u8;" +
        $"TotalTCycles:u64;Cpu:{CpuCodec.Schema};Memory:{MemoryCodec.Schema};Cartridge:{CartridgeCodec.Schema};" +
        $"Interrupts:{InterruptsCodec.Schema};Timer:{TimerCodec.Schema};Serial:{SerialCodec.Schema};Joypad:{JoypadCodec.Schema};" +
        $"Apu:{ApuCodec.Schema};Audio:{AudioCodec.Schema};Dma:{DmaCodec.Schema};Ppu:{PpuCodec.Schema};Video:{VideoCodec.Schema}}}";
    internal static readonly byte[] Fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes(Layout))[..8];

    internal static byte[] Serialize(GameBoyState state)
    {
        var w = new Writer();
        w.Raw(Magic);
        w.Int32(state.FormatVersion);
        w.Raw(Fingerprint);
        w.String(state.Model);
        w.String(state.BootProfile);
        w.String(state.RomSha256);
        w.Byte(state.CartridgeType);
        w.UInt64(state.TotalTCycles);
        CpuCodec.Write(w, state.Cpu);
        MemoryCodec.Write(w, state.Memory);
        CartridgeCodec.Write(w, state.Cartridge);
        InterruptsCodec.Write(w, state.Interrupts);
        TimerCodec.Write(w, state.Timer);
        SerialCodec.Write(w, state.Serial);
        JoypadCodec.Write(w, state.Joypad);
        ApuCodec.Write(w, state.Apu);
        AudioCodec.Write(w, state.Audio);
        DmaCodec.Write(w, state.Dma);
        PpuCodec.Write(w, state.Ppu);
        VideoCodec.Write(w, state.Video);
        return w.ToArray();
    }

    internal static GameBoyState Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxBytes)
        {
            throw new InvalidDataException($"A machine state is at most {MaxBytes} bytes.");
        }

        var r = new Reader(data.ToArray());
        if (!r.Raw(Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException("This is not a machine state.");
        }

        var version = r.Int32();
        if (version != GameBoyState.CurrentFormat)
        {
            throw new NotSupportedException($"Machine state format {version} cannot be read; this build reads format {GameBoyState.CurrentFormat}.");
        }

        if (!r.Raw(Fingerprint.Length).SequenceEqual(Fingerprint))
        {
            throw new NotSupportedException($"The machine state was written with another layout of format {version}.");
        }

        var state = new GameBoyState
        {
            FormatVersion = version,
            Model = r.String(),
            BootProfile = r.String(),
            RomSha256 = r.String(),
            CartridgeType = r.Byte(),
            TotalTCycles = r.UInt64(),
            Cpu = (Sm83Cpu.State)CpuCodec.Read(r)!,
            Memory = (MemoryBus.State)MemoryCodec.Read(r)!,
            Cartridge = (CartridgeState)CartridgeCodec.Read(r)!,
            Interrupts = (Interrupts.State)InterruptsCodec.Read(r)!,
            Timer = (Timer.State)TimerCodec.Read(r)!,
            Serial = (Serial.State)SerialCodec.Read(r)!,
            Joypad = (Joypad.State)JoypadCodec.Read(r)!,
            Apu = (Apu.State)ApuCodec.Read(r)!,
            Audio = (AudioOutput.State)AudioCodec.Read(r)!,
            Dma = (OamDma.State)DmaCodec.Read(r)!,
            Ppu = (Ppu.State)PpuCodec.Read(r)!,
            Video = (VideoOutput.State)VideoCodec.Read(r)!
        };
        if (!r.AtEnd)
        {
            throw new InvalidDataException("The machine state has bytes after its end.");
        }

        return state;
    }

    // The schema text of a type (for the fingerprint), as the codec writes it.
    internal static string SchemaOf(Type type) => For(type).Schema;

    private static Codec For(Type type)
    {
        lock (Codecs)
        {
            if (Codecs.TryGetValue(type, out var known))
            {
                return known;
            }

            var codec = Create(type);
            Codecs[type] = codec;
            return codec;
        }
    }

    private static Codec Create(Type type)
    {
        if (type == typeof(bool))
        {
            return new Codec("bool", (w, v) => w.Bool((bool)v!), r => r.Bool());
        }

        if (type == typeof(byte))
        {
            return new Codec("u8", (w, v) => w.Byte((byte)v!), r => r.Byte());
        }

        if (type == typeof(short))
        {
            return new Codec("i16", (w, v) => w.Int16((short)v!), r => r.Int16(), 2);
        }

        if (type == typeof(ushort))
        {
            return new Codec("u16", (w, v) => w.UInt16((ushort)v!), r => r.UInt16(), 2);
        }

        if (type == typeof(int))
        {
            return new Codec("i32", (w, v) => w.Int32((int)v!), r => r.Int32(), 4);
        }

        if (type == typeof(uint))
        {
            return new Codec("u32", (w, v) => w.UInt32((uint)v!), r => r.UInt32(), 4);
        }

        if (type == typeof(long))
        {
            return new Codec("i64", (w, v) => w.Int64((long)v!), r => r.Int64(), 8);
        }

        if (type == typeof(ulong))
        {
            return new Codec("u64", (w, v) => w.UInt64((ulong)v!), r => r.UInt64(), 8);
        }

        if (type == typeof(string))
        {
            return new Codec("string", (w, v) => w.String((string)v!), r => r.String(), 4);
        }

        if (Nullable.GetUnderlyingType(type) is { } inner)
        {
            var value = For(inner);
            return new Codec(value.Schema + "?", (w, v) =>
            {
                w.Bool(v is not null);
                if (v is not null)
                {
                    value.Write(w, v);
                }
            },
                r => r.Bool() ? value.Read(r) : null);
        }

        // Arrays: the length (-1 for none), then the elements; arrays of numbers avoid boxing.
        if (type == typeof(byte[]))
        {
            return new Codec("u8[]", (w, v) =>
            {
                if (w.ArrayLength(v))
                {
                    w.Raw((byte[])v!);
                }
            }, r => r.ArrayLength(1) is var n and >= 0 ? r.Raw(n).ToArray() : null, 4);
        }

        if (type == typeof(short[]))
        {
            return new Codec("i16[]", (w, v) =>
            {
                if (w.ArrayLength(v))
                {
                    w.Int16Array((short[])v!);
                }
            },
                r => r.ArrayLength(2) is var n and >= 0 ? r.Int16Array(n) : null, 4);
        }

        if (type == typeof(uint[]))
        {
            return new Codec("u32[]", (w, v) =>
            {
                if (w.ArrayLength(v))
                {
                    w.UInt32Array((uint[])v!);
                }
            },
                r => r.ArrayLength(4) is var n and >= 0 ? r.UInt32Array(n) : null, 4);
        }

        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            var element = For(elementType);
            return new Codec(element.Schema + "[]", (w, v) =>
            {
                if (!w.ArrayLength(v))
                {
                    return;
                }

                foreach (var item in (Array)v!)
                {
                    element.Write(w, item);
                }
            }, r =>
            {
                var length = r.ArrayLength(element.MinimumBytes);
                if (length < 0)
                {
                    return null;
                }

                var array = Array.CreateInstanceFromArrayType(type, length); // Safe ahead of time.
                for (var i = 0; i < length; i++)
                {
                    array.SetValue(element.Read(r), i);
                }

                return array;
            }, 4);
        }
        return Record(type);
    }

    // Codes a positional record through its primary constructor and one property per parameter.
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "RecordTypes keeps these members of every record the layout reaches (checked by a test).")]
    private static Codec Record(Type type)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var constructors = type.GetConstructors(instance)
            .Where(c => c.GetParameters() is not [var only] || only.ParameterType != type).ToArray();
        if (constructors.Length != 1 || (type.GetMethod("<Clone>$", instance) is null && !type.IsValueType))
        {
            throw new NotSupportedException($"{type} is not a positional record.");
        }

        var constructor = constructors[0];
        var parameters = constructor.GetParameters();
        if (parameters.Length == 0)
        {
            throw new NotSupportedException($"{type} has no fields.");
        }

        var properties = parameters.Select(p => type.GetProperty(p.Name!, instance) ??
            throw new NotSupportedException($"{type}.{p.Name} is not a property.")).ToArray();
        var fields = parameters.Select(p => For(p.ParameterType)).ToArray();
        var name = type.DeclaringType is { } outer ? $"{outer.Name}.{type.Name}" : type.Name;
        var schema = $"{name}{{{string.Join(';', parameters.Select((p, i) => $"{p.Name}:{fields[i].Schema}"))}}}";
        var minimum = fields.Sum(f => f.MinimumBytes);
        if (type.IsValueType)
        {
            return new Codec(schema, (w, v) => WriteFields(w, v!), ReadFields, minimum);
        }

        return new Codec(schema, (w, v) =>
        {
            w.Bool(v is not null);
            if (v is not null)
            {
                WriteFields(w, v);
            }
        },
            r => r.Bool() ? ReadFields(r) : null);

        void WriteFields(Writer w, object value)
        {
            for (var i = 0; i < fields.Length; i++)
            {
                fields[i].Write(w, properties[i].GetValue(value));
            }
        }

        object ReadFields(Reader r)
        {
            var values = new object?[fields.Length];
            for (var i = 0; i < fields.Length; i++)
            {
                values[i] = fields[i].Read(r);
            }

            return constructor.Invoke(values);
        }
    }

    // Writes and reads one type; MinimumBytes is the fewest bytes a value takes.
    private sealed class Codec(string schema, Action<Writer, object?> write, Func<Reader, object?> read, int minimumBytes = 1)
    {
        internal string Schema { get; } = schema;
        internal int MinimumBytes { get; } = minimumBytes;
        internal void Write(Writer w, object? value) => write(w, value);
        internal object? Read(Reader r) => read(r);
    }

    private sealed class Writer
    {
        private readonly ArrayBufferWriter<byte> buffer = new(256 * 1024);
        internal byte[] ToArray() => buffer.WrittenSpan.ToArray();
        internal void Raw(ReadOnlySpan<byte> bytes) => buffer.Write(bytes);
        internal void Bool(bool value) => Byte(value ? (byte)1 : (byte)0);
        internal void Byte(byte value) => buffer.Write([value]);

        internal void Int16(short value)
        {
            BinaryPrimitives.WriteInt16LittleEndian(buffer.GetSpan(2), value);
            buffer.Advance(2);
        }

        internal void UInt16(ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.GetSpan(2), value);
            buffer.Advance(2);
        }

        internal void Int32(int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.GetSpan(4), value);
            buffer.Advance(4);
        }

        internal void UInt32(uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.GetSpan(4), value);
            buffer.Advance(4);
        }

        internal void Int64(long value)
        {
            BinaryPrimitives.WriteInt64LittleEndian(buffer.GetSpan(8), value);
            buffer.Advance(8);
        }

        internal void UInt64(ulong value)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.GetSpan(8), value);
            buffer.Advance(8);
        }

        internal void Int16Array(short[] values)
        {
            var span = buffer.GetSpan(values.Length * 2);
            for (var i = 0; i < values.Length; i++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(span[(i * 2)..], values[i]);
            }

            buffer.Advance(values.Length * 2);
        }

        internal void UInt32Array(uint[] values)
        {
            var span = buffer.GetSpan(values.Length * 4);
            for (var i = 0; i < values.Length; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(span[(i * 4)..], values[i]);
            }

            buffer.Advance(values.Length * 4);
        }

        // Writes the length of an array, or -1 for none; true when its elements follow.
        internal bool ArrayLength(object? array)
        {
            Int32(array is Array a ? a.Length : -1);
            return array is not null;
        }

        internal void String(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > MaxStringBytes)
            {
                throw new ArgumentException("A state string is too long.", nameof(value));
            }

            Int32(bytes.Length);
            Raw(bytes);
        }
    }

    private sealed class Reader(byte[] data)
    {
        private int position;
        internal bool AtEnd => position == data.Length;

        internal ReadOnlySpan<byte> Raw(int count)
        {
            if (count > data.Length - position)
            {
                throw new InvalidDataException("The machine state ends early.");
            }

            var span = data.AsSpan(position, count);
            position += count;
            return span;
        }

        internal bool Bool() => Byte() switch { 0 => false, 1 => true, _ => throw new InvalidDataException("A flag is neither 0 nor 1.") };
        internal byte Byte() => Raw(1)[0];
        internal short Int16() => BinaryPrimitives.ReadInt16LittleEndian(Raw(2));
        internal ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Raw(2));
        internal int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Raw(4));
        internal uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Raw(4));
        internal long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Raw(8));
        internal ulong UInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Raw(8));

        internal short[] Int16Array(int count)
        {
            var bytes = Raw(count * 2);
            var values = new short[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes[(i * 2)..]);
            }

            return values;
        }

        internal uint[] UInt32Array(int count)
        {
            var bytes = Raw(count * 4);
            var values = new uint[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(i * 4)..]);
            }

            return values;
        }

        // Reads an array length that fits both the limit and the bytes left; -1 is none.
        internal int ArrayLength(int minimumBytes)
        {
            var length = Int32();
            if (length < -1 || length > MaxElements || (long)length * minimumBytes > data.Length - position)
            {
                throw new InvalidDataException("A length in the machine state is out of range.");
            }

            return length;
        }

        internal string String()
        {
            var length = ArrayLength(1);
            if (length < 0)
            {
                throw new InvalidDataException("A string in the machine state is missing.");
            }

            if (length > MaxStringBytes)
            {
                throw new InvalidDataException("A string in the machine state is too long.");
            }

            return Encoding.UTF8.GetString(Raw(length));
        }
    }
}
