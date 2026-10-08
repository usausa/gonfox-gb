namespace GonFox.GameBoy.Platform;

using System.Buffers.Binary;
using System.IO;

using GonFox.GameBoy.Core.Cartridge;

// A battery file's contents: the cartridge RAM, plus the MBC3 or HuC3 clock and the save time.
internal sealed record BatteryContents(byte[] Ram, RtcSnapshot? Clock = null, long? SavedUnixSeconds = null, Huc3ClockSnapshot? Huc3 = null);

// Reads and writes battery files in the layout other emulators share, with any clock footer.
internal static class BatteryFile
{
    internal const int ClockBytes = 48;
    internal const int LegacyClockBytes = 44;
    internal const int Huc3Bytes = 136;
    internal const int SameBoyHuc3Bytes = 17;

    internal static byte[] Encode(BatteryContents contents)
    {
        if (contents.Huc3 is { } huc3)
        {
            return EncodeHuc3(contents.Ram, huc3, contents.SavedUnixSeconds ?? 0);
        }

        if (contents.Clock is not { } clock)
        {
            return contents.Ram.ToArray();
        }

        var data = new byte[contents.Ram.Length + ClockBytes];
        contents.Ram.CopyTo(data, 0);
        var footer = data.AsSpan(contents.Ram.Length);
        Write(footer, clock.Current);
        Write(footer[20..], clock.Latched);
        BinaryPrimitives.WriteInt64LittleEndian(footer[40..], contents.SavedUnixSeconds ?? 0);
        return data;
    }

    internal static BatteryContents Decode(ReadOnlySpan<byte> data, int ramLength, bool hasClock, string path)
    {
        var footer = data.Length - ramLength;
        if (footer == 0)
        {
            return new(data.ToArray());
        }

        if (!hasClock || footer is not (ClockBytes or LegacyClockBytes))
        {
            throw new InvalidDataException($"Invalid battery RAM size: {data.Length} bytes (expected {ramLength}" +
                (hasClock ? $", or {ramLength + ClockBytes} or {ramLength + LegacyClockBytes} with the clock" : string.Empty) + $"): {path}");
        }

        var clock = data[ramLength..];
        var saved = footer == ClockBytes ? BinaryPrimitives.ReadInt64LittleEndian(clock[40..]) : BinaryPrimitives.ReadUInt32LittleEndian(clock[40..]);
        return new(data[..ramLength].ToArray(), new(Read(clock), Read(clock[20..])), saved);
    }

    // Decodes a HuC3 battery file, telling the footer format by its length.
    internal static BatteryContents DecodeHuc3(ReadOnlySpan<byte> data, int ramLength, string path)
    {
        var footer = data.Length - ramLength;
        if (footer == 0)
        {
            return new(data.ToArray());
        }

        if (footer is not (Huc3Bytes or SameBoyHuc3Bytes))
        {
            throw new InvalidDataException($"Invalid battery RAM size: {data.Length} bytes (expected {ramLength}" +
                $", or {ramLength + Huc3Bytes} or {ramLength + SameBoyHuc3Bytes} with the clock): {path}");
        }

        var clock = data[ramLength..];
        var memory = new byte[256];
        long saved;
        if (footer == Huc3Bytes)
        {
            for (var i = 0; i < 128; i++)
            {
                (memory[i * 2], memory[(i * 2) + 1]) = ((byte)(clock[i] & 15), (byte)(clock[i] >> 4));
            }

            saved = BinaryPrimitives.ReadInt64LittleEndian(clock[128..]);
        }
        else
        {
            var counted = BinaryPrimitives.ReadUInt64LittleEndian(clock);
            Nibbles(memory, 0x10, BinaryPrimitives.ReadUInt16LittleEndian(clock[8..]), 3);
            Nibbles(memory, 0x13, BinaryPrimitives.ReadUInt16LittleEndian(clock[10..]), 3);
            Nibbles(memory, 0x58, BinaryPrimitives.ReadUInt16LittleEndian(clock[12..]), 3);
            Nibbles(memory, 0x5B, BinaryPrimitives.ReadUInt16LittleEndian(clock[14..]), 4);
            memory[0x5F] = (byte)(clock[16] & 1);
            saved = counted > long.MaxValue ? long.MaxValue : (long)(counted - (counted % 60));
        }
        return new(data[..ramLength].ToArray(), SavedUnixSeconds: saved, Huc3: new(memory, 0));
    }

    // Encodes the RAM and the MCU memory with the time the current minute began.
    private static byte[] EncodeHuc3(byte[] ram, Huc3ClockSnapshot clock, long savedUnixSeconds)
    {
        var data = new byte[ram.Length + Huc3Bytes];
        ram.CopyTo(data, 0);
        var footer = data.AsSpan(ram.Length);
        for (var i = 0; i < 128; i++)
        {
            footer[i] = (byte)((clock.Memory[i * 2] & 15) | ((clock.Memory[(i * 2) + 1] & 15) << 4));
        }

        BinaryPrimitives.WriteInt64LittleEndian(footer[128..], savedUnixSeconds - clock.Seconds);
        return data;
    }

    private static void Nibbles(byte[] memory, int at, int value, int count)
    {
        for (var i = 0; i < count; i++)
        {
            memory[at + i] = (byte)((value >> (4 * i)) & 15);
        }
    }

    private static void Write(Span<byte> destination, RtcRegisters registers)
    {
        byte[] values = [registers.Seconds, registers.Minutes, registers.Hours, registers.DayLow, registers.DayHigh];
        for (var i = 0; i < 5; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination[(i * 4)..], values[i]);
        }
    }

    // Reads one register set, one byte from each 32-bit value.
    private static RtcRegisters Read(ReadOnlySpan<byte> source) =>
        new(source[0], source[4], source[8], source[12], source[16]);
}
