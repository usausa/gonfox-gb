namespace GonFox.GameBoy.Platform;

using System.IO;

// A record's size and the time it was last written.
public sealed record RecordInfo(string Name, long Length, DateTimeOffset Written);

// Stores named byte records, each written whole, so a reader sees either the old or the new bytes.
public interface IRecordStore
{
    // Where a record lives, for messages.
    string Describe(string name);

    // Reads a record, or null if missing; throws InvalidDataException beyond maxLength.
    Task<byte[]?> ReadAsync(string name, int maxLength);

    Task WriteAsync(string name, byte[] data, string? previous = null);

    Task<IReadOnlyList<RecordInfo>> ListAsync(string prefix);

    Task<bool> DeleteAsync(string name);

    // Locks a name until the result is disposed, across processes for files.
    Task<IDisposable> LockAsync(string name);
}

internal static class RecordName
{
    internal static string Check(string name) =>
        name is { Length: > 0 and <= 160 } && name[0] != '.' && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')
            ? name : throw new ArgumentException($"Invalid record name: {name}", nameof(name));

    internal static string InUse(string location) => $"In use by another window or application: {location}";
}

// Stores records as files in one directory, replacing each through a temporary file.
public sealed class FileRecordStore(string directory) : IRecordStore
{
    private const int SharingViolation = 32;

    public string Describe(string name) => Path.Combine(directory, RecordName.Check(name));

    public Task<byte[]?> ReadAsync(string name, int maxLength) => Task.Run(() =>
    {
        var path = Describe(name);
        if (File.Exists(directory))
        {
            throw new IOException($"The save location is not a folder: {directory}");
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > maxLength)
            {
                throw new InvalidDataException($"File too large: {stream.Length} bytes (at most {maxLength}): {path}");
            }

            var data = new byte[stream.Length];
            stream.ReadExactly(data);
            return data;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    });

    public Task WriteAsync(string name, byte[] data, string? previous = null) => Task.Run(() =>
    {
        var path = Describe(name);
        var backup = previous is null ? null : Describe(previous);
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path))
            {
                File.Replace(temporary, path, backup);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    });

    public Task<IReadOnlyList<RecordInfo>> ListAsync(string prefix) => Task.Run<IReadOnlyList<RecordInfo>>(() =>
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return new DirectoryInfo(directory).EnumerateFiles()
            .Where(file => file.Name.StartsWith(prefix, StringComparison.Ordinal) && !file.Name.EndsWith(".tmp", StringComparison.Ordinal))
            .Select(file => new RecordInfo(file.Name, file.Length, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)))
            .OrderBy(record => record.Name, StringComparer.Ordinal).ToArray();
    });

    public Task<bool> DeleteAsync(string name) => Task.Run(() =>
    {
        var path = Describe(name);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    });

    // Holds the lock as an open delete-on-close handle, which the OS releases if the process ends.
    public Task<IDisposable> LockAsync(string name) => Task.Run<IDisposable>(() =>
    {
        var path = Describe(name);
        Directory.CreateDirectory(directory);
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException exception) when ((exception.HResult & 0xFFFF) == SharingViolation)
        {
            throw new IOException(RecordName.InUse(path), exception);
        }
    });
}

// Keeps records in memory, copying data in and out, for tests and hosts that keep nothing.
internal sealed class MemoryRecordStore(TimeProvider? time = null) : IRecordStore
{
    private readonly TimeProvider time = time ?? TimeProvider.System;
    private readonly Dictionary<string, (byte[] Data, DateTimeOffset Written)> records = [with(StringComparer.Ordinal)];
    private readonly HashSet<string> locks = [with(StringComparer.Ordinal)];

    public string Describe(string name) => "memory:" + RecordName.Check(name);

    public Task<byte[]?> ReadAsync(string name, int maxLength)
    {
        RecordName.Check(name);
        lock (records)
        {
            if (!records.TryGetValue(name, out var record))
            {
                return Task.FromResult<byte[]?>(null);
            }

            if (record.Data.Length > maxLength)
            {
                return Task.FromException<byte[]?>(new InvalidDataException($"File too large: {record.Data.Length} bytes (at most {maxLength}): {Describe(name)}"));
            }

            return Task.FromResult<byte[]?>(record.Data.ToArray());
        }
    }

    public Task WriteAsync(string name, byte[] data, string? previous = null)
    {
        RecordName.Check(name);
        if (previous is not null)
        {
            RecordName.Check(previous);
        }

        lock (records)
        {
            if (previous is not null && records.TryGetValue(name, out var old))
            {
                records[previous] = old;
            }

            records[name] = (data.ToArray(), time.GetUtcNow());
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RecordInfo>> ListAsync(string prefix)
    {
        lock (records)
        {
            return Task.FromResult<IReadOnlyList<RecordInfo>>(records.Where(r => r.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(r => new RecordInfo(r.Key, r.Value.Data.Length, r.Value.Written)).OrderBy(r => r.Name, StringComparer.Ordinal).ToArray());
        }
    }

    public Task<bool> DeleteAsync(string name)
    {
        RecordName.Check(name);
        lock (records)
        {
            return Task.FromResult(records.Remove(name));
        }
    }

    public Task<IDisposable> LockAsync(string name)
    {
        RecordName.Check(name);
        lock (locks)
        {
            if (!locks.Add(name))
            {
                return Task.FromException<IDisposable>(new IOException(RecordName.InUse(Describe(name))));
            }
        }
        return Task.FromResult<IDisposable>(new Release(this, name));
    }

    private sealed class Release(MemoryRecordStore store, string name) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) != 0)
            {
                return;
            }

            lock (store.locks)
            {
                store.locks.Remove(name);
            }
        }
    }
}
