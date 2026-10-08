namespace GonFox.GameBoy.Platform;

using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using GonFox.GameBoy.Core;

// A slot's machine state with the error text of a run stopped by an exception.
public sealed record SavedState(GameBoyState State, string? Error);

// A filled slot of the current ROM.
public sealed record SlotInfo(int Slot, DateTimeOffset Saved, long Length);

// Stores up to nine machine states per ROM, each with a header and a hash checked on load.
public sealed class StateSlotStore(IRecordStore records)
{
    public const int Count = 9;
    private const int MaxRecordBytes = 9 * 1024 * 1024;
    private const int MaxErrorBytes = 1024;
    private static readonly byte[] Magic = "SESTATE1"u8.ToArray();

    public string Describe(string romId, int slot) => records.Describe(Name(romId, slot));

    public async Task<IReadOnlyList<SlotInfo>> ListAsync(string romId)
    {
        var prefix = Id(romId) + ".slot";
        var slots = new List<SlotInfo>();
        foreach (var record in await records.ListAsync(prefix).ConfigureAwait(false))
        {
            var middle = record.Name[prefix.Length..];
            if (middle.EndsWith(".state", StringComparison.Ordinal) && int.TryParse(middle[..^6], out var slot) &&
                slot is >= 1 and <= Count && record.Name == Name(romId, slot))
            {
                slots.Add(new(slot, record.Written, record.Length));
            }
        }
        return slots;
    }

    public Task SaveAsync(string romId, int slot, SavedState saved, DateTimeOffset now) =>
        records.WriteAsync(Name(romId, slot), Encode(saved, now));

    // Loads a slot, or null if empty; throws on a damaged, foreign or other-format state.
    public async Task<SavedState?> LoadAsync(string romId, int slot)
    {
        var data = await records.ReadAsync(Name(romId, slot), MaxRecordBytes).ConfigureAwait(false);
        if (data is null)
        {
            return null;
        }

        var saved = Decode(data, Describe(romId, slot));
        if (!string.Equals(saved.State.RomSha256, Id(romId), StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The state belongs to another ROM: {Describe(romId, slot)}");
        }

        return saved;
    }

    public static byte[] Encode(SavedState saved, DateTimeOffset now)
    {
        var payload = saved.State.Serialize();
        var error = Encoding.UTF8.GetBytes(saved.Error ?? string.Empty);
        if (error.Length > MaxErrorBytes)
        {
            error = error[..MaxErrorBytes];
        }

        using var stream = new MemoryStream(Magic.Length + 8 + 32 + 4 + error.Length + 4 + payload.Length);
        Span<byte> number = stackalloc byte[8];
        stream.Write(Magic);
        BinaryPrimitives.WriteInt64LittleEndian(number, now.ToUnixTimeMilliseconds());
        stream.Write(number);
        stream.Write(SHA256.HashData(payload));
        BinaryPrimitives.WriteInt32LittleEndian(number, error.Length);
        stream.Write(number[..4]);
        stream.Write(error);
        BinaryPrimitives.WriteInt32LittleEndian(number, payload.Length);
        stream.Write(number[..4]);
        stream.Write(payload);
        return stream.ToArray();
    }

    public static SavedState Decode(ReadOnlySpan<byte> data, string location)
    {
        InvalidDataException Damaged(string what) => new($"Damaged state file ({what}): {location}");
        if (data.Length < Magic.Length + 8 + 32 + 8 || !data[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException($"Not a state file: {location}");
        }

        var rest = data[(Magic.Length + 8)..];
        var hash = rest[..32];
        rest = rest[32..];
        var errorLength = BinaryPrimitives.ReadInt32LittleEndian(rest);
        rest = rest[4..];
        if (errorLength is < 0 or > MaxErrorBytes || errorLength + 4 > rest.Length)
        {
            throw Damaged("length");
        }

        var error = Encoding.UTF8.GetString(rest[..errorLength]);
        rest = rest[errorLength..];
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(rest);
        rest = rest[4..];
        if (payloadLength != rest.Length)
        {
            throw Damaged("length");
        }

        if (!SHA256.HashData(rest).AsSpan().SequenceEqual(hash))
        {
            throw Damaged("hash mismatch");
        }

        GameBoyState state;
        try
        {
            state = GameBoyState.Deserialize(rest);
        }
        catch (NotSupportedException exception)
        {
            throw new NotSupportedException($"The state has a format this build cannot read ({exception.Message}): {location}", exception);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"Damaged state file ({exception.Message}): {location}", exception);
        }
        return new(state, error.Length == 0 ? null : error);
    }

    // The save time in a record's header.
    public static DateTimeOffset SavedAt(ReadOnlySpan<byte> data) =>
        DateTimeOffset.FromUnixTimeMilliseconds(BinaryPrimitives.ReadInt64LittleEndian(data[Magic.Length..]));

    private static string Name(string romId, int slot)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slot, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, Count);
        return $"{Id(romId)}.slot{slot}.state";
    }

    // The canonical (lower-case) form of a SHA-256 ROM identifier.
    private static string Id(string romId) => romId.Length == 64 && romId.All(char.IsAsciiHexDigit)
        ? Convert.ToHexStringLower(Convert.FromHexString(romId)) : throw new ArgumentException("Expected a SHA-256 ROM identifier.", nameof(romId));
}
