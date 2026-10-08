namespace GonFox.GameBoy.Platform;

using System.IO;
using System.Security.Cryptography;

// What the session last did with the battery save, reported through SessionNotice.
public enum SessionEvent
{
    None,
    Loaded,
    LoadFailed,
    Saving,
    Saved,
    SaveFailed
}

// What a load found in the battery file.
[Flags]
public enum BatteryLoadNotes
{
    None = 0,
    NewFile = 1,
    ChangedOutside = 2,
    MissingClockRecord = 4,
    ClockRecord = 8
}

public sealed record SessionNotice(SessionEvent Event, string? Path = null, bool Automatic = false, DateTimeOffset At = default,
    BatteryLoadNotes Notes = BatteryLoadNotes.None, long ClockSeconds = 0, Exception? Error = null)
{
    public static SessionNotice None { get; } = new(SessionEvent.None);
}

// Serializes ROM loads, battery saves and state slots without blocking the UI or the model thread.
public sealed class EmulationSession(EmulationRunner runner, BatterySaveStore store, TimeProvider? time = null,
    StateSlotStore? states = null) : IDisposable
{
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly TimeProvider time = time ?? TimeProvider.System;
    private readonly AutoSavePolicy autoSave = new();
    private CapturedSave? pending;
    private IDisposable? romLock;
    private string? lockedRomId;
    private bool hasBattery;
    private string? loadedRomId;
    public SessionNotice Notice { get; private set; } = SessionNotice.None;
    public IReadOnlyList<SlotInfo> Slots { get; private set; } = [];
    public bool HasPendingSave => pending?.Ram is not null;

    public async Task LoadAsync(byte[] image, string name)
    {
        await operations.WaitAsync().ConfigureAwait(false);
        IDisposable? newLock = null;
        try
        {
            var prepared = await runner.PrepareLoadAsync(image).ConfigureAwait(false);
            var battery = prepared.Loaded.Info.HasBattery;

            // Locks the new save before pausing, so a save in use elsewhere leaves this ROM running.
            if (battery && prepared.Id != lockedRomId)
            {
                newLock = await store.LockAsync(prepared.Id).ConfigureAwait(false);
            }

            var previous = await CaptureAsync(pause: true).ConfigureAwait(false);
            await PersistAsync(previous, automatic: false).ConfigureAwait(false);
            BatteryLoad? load = null;
            var notes = BatteryLoadNotes.None;
            long clockSeconds = 0;
            if (battery)
            {
                // Reloading the same content must carry forward the latest RAM, never an earlier disk copy.
                if (previous.RomId == prepared.Id && previous.Ram is not null)
                {
                    load = new(previous.Ram, previous.Clock, Seconds(previous.At), previous.Huc3);
                }
                else
                {
                    (load, notes, clockSeconds) = await ReadAsync(prepared).ConfigureAwait(false);
                }
            }
            await runner.LoadPreparedAsync(prepared, name, load).ConfigureAwait(false);
            if (newLock is not null)
            {
                romLock?.Dispose();
                romLock = newLock;
                lockedRomId = prepared.Id;
                newLock = null;
            }
            else if (!battery)
            {
                romLock?.Dispose();
                romLock = null;
                lockedRomId = null;
            }
            hasBattery = battery;
            loadedRomId = prepared.Id;

            // Takes the loaded RAM as the auto-save baseline.
            if (battery)
            {
                autoSave.Saved(prepared.Id, load?.Ram ?? new byte[prepared.Loaded.Info.RamSizeBytes]);
            }

            Notice = new(SessionEvent.Loaded, battery ? store.GetPath(prepared.Id) : null, Notes: notes, ClockSeconds: clockSeconds);
            Slots = await ListSlotsAsync(prepared.Id).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Notice = new(SessionEvent.LoadFailed, Error: exception);
            throw;
        }
        finally
        {
            newLock?.Dispose();
            operations.Release();
        }
    }

    public Task SaveAsync() => SaveAsync(resume: true);
    public Task SaveForCloseAsync() => SaveAsync(resume: false);

    // Saves changed RAM without pausing, skipping while another operation runs; failures go to Notice.
    public async Task AutoSaveAsync()
    {
        if (!hasBattery || !await operations.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

#pragma warning disable CA1031
        try
        {
            var snapshot = await CaptureAsync(pause: false).ConfigureAwait(false);
            if (snapshot.Ram is null || !autoSave.ShouldSave(snapshot.RomId, snapshot.Ram, snapshot.At, snapshot.ClockChanges))
            {
                return;
            }

            await PersistAsync(snapshot, automatic: true).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Notice = new(SessionEvent.SaveFailed, Automatic: true, Error: exception);
        }
        finally
        {
            operations.Release();
        }
#pragma warning restore CA1031
    }

    // Captures the machine into a slot of the loaded ROM, leaving it paused.
    public async Task SaveStateAsync(int slot)
    {
        await operations.WaitAsync().ConfigureAwait(false);
        try
        {
            var (slots, romId) = Current();
            var saved = await runner.CaptureStateAsync().ConfigureAwait(false);
            await slots.SaveAsync(romId, slot, saved, time.GetUtcNow()).ConfigureAwait(false);
            Slots = await slots.ListAsync(romId).ConfigureAwait(false);
        }
        finally
        {
            operations.Release();
        }
    }

    // Restores a slot, leaving the machine paused; an invalid state changes nothing.
    public async Task LoadStateAsync(int slot)
    {
        await operations.WaitAsync().ConfigureAwait(false);
        try
        {
            var (slots, romId) = Current();
            var saved = await slots.LoadAsync(romId, slot).ConfigureAwait(false) ?? throw new InvalidOperationException($"Slot {slot} is empty.");
            await runner.RestoreStateAsync(saved).ConfigureAwait(false);
        }
        finally
        {
            operations.Release();
        }
    }

    public string DescribeSlot(int slot) => states is null || loadedRomId is null ? string.Empty : states.Describe(loadedRomId, slot);

    private (StateSlotStore Store, string RomId) Current() => (states ?? throw new InvalidOperationException("The session has no state slot store."),
        loadedRomId ?? throw new InvalidOperationException("No ROM is loaded."));

    // Lists the filled slots, or none when the store cannot be read.
    private async Task<IReadOnlyList<SlotInfo>> ListSlotsAsync(string romId)
    {
        if (states is null)
        {
            return [];
        }

        try
        {
            return await states.ListAsync(romId).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private async Task SaveAsync(bool resume)
    {
        await operations.WaitAsync().ConfigureAwait(false);
        try
        {
            var captured = await CaptureAsync(pause: true).ConfigureAwait(false);
            await PersistAsync(captured, automatic: false).ConfigureAwait(false);
            if (resume && captured.WasRunning)
            {
                await runner.ResumeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            Notice = new(SessionEvent.SaveFailed, Error: exception);
            throw;
        }
        finally
        {
            operations.Release();
        }
    }

    private async Task<CapturedSave> CaptureAsync(bool pause) =>
        (pause ? await runner.CaptureSaveAsync().ConfigureAwait(false) : await runner.PeekSaveAsync().ConfigureAwait(false)) with { At = time.GetUtcNow() };

    private async Task<(BatteryLoad? Load, BatteryLoadNotes Notes, long ClockSeconds)> ReadAsync(PreparedRom prepared)
    {
        var ramLength = prepared.Loaded.Info.RamSizeBytes;
        bool hasClock = prepared.Loaded.Cartridge is Core.Cartridge.IRealTimeClockCartridge,
            huc3 = prepared.Loaded.Cartridge is Core.Cartridge.IHuc3ClockCartridge;
        var stored = await store.ReadAsync(prepared.Id,
            ramLength + (hasClock ? BatteryFile.ClockBytes : huc3 ? BatteryFile.Huc3Bytes : 0)).ConfigureAwait(false);
        if (stored is null)
        {
            return (null, BatteryLoadNotes.NewFile, 0);
        }

        var path = store.GetPath(prepared.Id);
        var contents = huc3 ? BatteryFile.DecodeHuc3(stored.Data, ramLength, path) : BatteryFile.Decode(stored.Data, ramLength, hasClock, path);
        var notes = stored.ChangedSinceSave ? BatteryLoadNotes.ChangedOutside : BatteryLoadNotes.None;
        if (contents.Clock is null && contents.Huc3 is null)
        {
            return (new(contents.Ram), notes | (hasClock || huc3 ? BatteryLoadNotes.MissingClockRecord : BatteryLoadNotes.None), 0);
        }

        var seconds = Seconds(DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(contents.SavedUnixSeconds!.Value, 0, 253_402_300_799)));
        return (new(contents.Ram, contents.Clock, seconds, contents.Huc3), notes | BatteryLoadNotes.ClockRecord, seconds);
    }

    // Whole seconds since a save; a save stamped in the future (the host clock went back) adds nothing.
    private long Seconds(DateTimeOffset saved) => Math.Max(0, (long)Math.Floor((time.GetUtcNow() - saved).TotalSeconds));

    private async Task PersistAsync(CapturedSave captured, bool automatic)
    {
        if (captured.Ram is null)
        {
            return;
        }

        pending = captured;
        Notice = new(SessionEvent.Saving, store.GetPath(captured.RomId), automatic);
        await store.WriteAsync(captured.RomId,
            BatteryFile.Encode(new(captured.Ram, captured.Clock, captured.At.ToUnixTimeSeconds(), captured.Huc3))).ConfigureAwait(false);
        pending = null;
        autoSave.Saved(captured.RomId, captured.Ram, captured.ClockChanges);
        Notice = new(SessionEvent.Saved, store.GetPath(captured.RomId), automatic, captured.At);
    }

    public void Dispose()
    {
        romLock?.Dispose();
        romLock = null;
        lockedRomId = null;
        operations.Dispose();
    }
}

// Asks for a save once changed RAM holds steady for an interval, or a minute after it changed.
public sealed class AutoSavePolicy
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);
    private string savedRomId = string.Empty;
    private byte[] saved = [];
    private byte[] seen = [];
    private DateTimeOffset? changedAt;

    public void Saved(string romId, ReadOnlySpan<byte> ram, long clockChanges = 0)
    {
        savedRomId = romId;
        saved = seen = Hash(ram, clockChanges);
        changedAt = null;
    }

    public bool ShouldSave(string romId, ReadOnlySpan<byte> ram, DateTimeOffset now, long clockChanges = 0)
    {
        var hash = Hash(ram, clockChanges);
        if (romId != savedRomId)
        {
            // Unknown file: treat as changed.
            savedRomId = romId;
            saved = [];
            changedAt = null;
        }

        if (hash.AsSpan().SequenceEqual(saved))
        {
            seen = hash;
            changedAt = null;
            return false;
        }
        var steady = changedAt is not null && hash.AsSpan().SequenceEqual(seen);
        changedAt ??= now;
        seen = hash;
        return steady || now - changedAt >= Deadline;
    }

    private static byte[] Hash(ReadOnlySpan<byte> ram, long clockChanges)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(ram);
        hash.AppendData(BitConverter.GetBytes(clockChanges));
        return hash.GetHashAndReset();
    }
}
