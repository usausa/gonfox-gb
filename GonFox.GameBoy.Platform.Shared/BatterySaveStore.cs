namespace GonFox.GameBoy.Platform;

using System.Security.Cryptography;
using System.Text;

// A battery file as read back, flagged when it differs from the hash of the last write.
internal sealed record StoredBattery(byte[] Data, bool ChangedSinceSave);

// Stores battery saves per ROM with a backup, a hash of the last write and a lock file.
public sealed class BatterySaveStore(IRecordStore records)
{
    public BatterySaveStore(string directory)
        : this(new FileRecordStore(directory))
    {
    }

    public string GetPath(string romId) => records.Describe(Name(romId));

    public static string BackupPath(string path) => path + ".bak";

    internal async Task<StoredBattery?> ReadAsync(string romId, int maxLength)
    {
        var name = Name(romId);
        var data = await records.ReadAsync(name, maxLength).ConfigureAwait(false);
        if (data is null)
        {
            return null;
        }

        var recorded = await records.ReadAsync(name + ".sha256", 256).ConfigureAwait(false);
        var text = recorded is null ? null : Encoding.ASCII.GetString(recorded).Trim();
        return new(data, text is not null && !string.Equals(text, Hash(data), StringComparison.OrdinalIgnoreCase));
    }

    // Writes the save, keeping the previous file as the backup, then records its hash.
    public async Task WriteAsync(string romId, byte[] data)
    {
        var name = Name(romId);
        await records.WriteAsync(name, data, name + ".bak").ConfigureAwait(false);
        await records.WriteAsync(name + ".sha256", Encoding.ASCII.GetBytes(Hash(data))).ConfigureAwait(false);
    }

    // Locks the save so that another window or app instance cannot load and overwrite it.
    public Task<IDisposable> LockAsync(string romId) => records.LockAsync(Id(romId) + ".lock");

    private static string Name(string romId) => Id(romId) + ".sav";

    // The canonical (lower-case) form of a SHA-256 ROM identifier.
    private static string Id(string romId) => romId.Length == 64 && romId.All(char.IsAsciiHexDigit)
        ? Convert.ToHexStringLower(Convert.FromHexString(romId)) : throw new ArgumentException("Expected a SHA-256 ROM identifier.", nameof(romId));

    private static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
