namespace GonFox.GameBoy.Platform;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

using GonFox.GameBoy.Core;
using GonFox.GameBoy.Core.Audio;
using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Cpu;
using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Video;
using GonFox.GameBoy.Platform.Audio;

internal sealed record PreparedRom(CartridgeLoadResult Loaded, string Id);

// Detached copies of the battery RAM and clock state; At is the host time of the capture.
public sealed record CapturedSave(
    string RomId,
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "A copy made for this capture, handed over without another one.")] byte[]? Ram,
    RtcSnapshot? Clock,
    bool WasRunning,
    DateTimeOffset At = default,
    Huc3ClockSnapshot? Huc3 = null,
    long ClockChanges = 0);

// The battery RAM and clock a load imports, with the whole seconds the clock missed.
internal sealed record BatteryLoad(byte[] Ram, RtcSnapshot? Clock = null, long ClockSeconds = 0, Huc3ClockSnapshot? Huc3 = null);

public sealed record MemorySnapshot(
    ushort Address,
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "A copy of the memory read, handed over without another one.")] byte[] Bytes,
    DebugSnapshot Registers);

// Runner state for the UI; Fault marks a CPU lockup, Error the exception that stopped the runner.
public sealed record EmulationStatus(bool RomLoaded, bool Running, bool Stopped, CpuFault? Fault,
    string Title, string? Error, string Warnings, DebugSnapshot Registers, double Fps, double DroppedSeconds,
    bool HasBattery = false, ulong? SavedStateTCycles = null, int RewindSeconds = 0, bool RewindAvailable = false, bool Rumble = false,
    bool BootRomMapped = false)
{
    public bool Faulted => Fault is not null;
}

// How often the runner records a state while running, and how many records it keeps (rewind).
public sealed record RewindOptions(ulong IntervalTCycles, int Capacity)
{
    public static RewindOptions Default { get; } = new(GameBoySystem.TCyclesPerSecond, 30);
}

public sealed class EmulationRunner : IDisposable
{
    private readonly record struct Command(Action<GameBoySystem> Action, TaskCompletionSource Completion);

    private readonly ConcurrentQueue<Command> commands = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly Lock gate = new();
    private readonly Thread thread;
    private readonly IPcmSink? audio;
    private readonly short[] pcm = new short[AudioOutput.CapacityFrames * AudioOutput.ChannelCount];
    private ulong publishedSequence;
    private bool published;
    private bool disposed;
    private volatile bool shutdown;

    // Fields below and the model belong to the worker; input producers only read inputEpoch atomically.
    private readonly EmulationPacer pacer = new();
    private readonly FrameRateCounter fps = new();
    private bool running;
    private bool wakeRequested;
    private string title = string.Empty;
    private string warnings = string.Empty;
    private string? error;
    private string romId = string.Empty;
    private IBatteryBackedCartridge? batteryCartridge;
    private IRealTimeClockCartridge? clockCartridge;
    private IHuc3ClockCartridge? huc3Cartridge;
    private IRumbleCartridge? rumble;
    private ICameraCartridge? camera;
    private SavedState? savedState;
    private readonly RewindOptions rewindOptions;
    private readonly List<GameBoyState> rewind = []; // Oldest first.
    private ulong nextRewindAt;
    private byte buttons;
    private bool inputNeedsSync;
    private long inputEpoch;
    private long lastTick;
    private long lastStatusTick;
    private readonly Action? threadStarted;

    public const string ThreadName = "Game Boy emulation";

    // Starts the emulation thread; threadStarted runs first on it for platform-specific thread setup.
    public EmulationRunner(IPcmSink? audio = null, RewindOptions? rewind = null, Action? threadStarted = null)
    {
        this.audio = audio;
        rewindOptions = rewind ?? RewindOptions.Default;
        nextRewindAt = rewindOptions.IntervalTCycles;
        this.threadStarted = threadStarted;
        thread = new Thread(Work) { IsBackground = true, Name = ThreadName };
        thread.Start();
    }

    public EmulationStatus Status
    {
        get
        {
            lock (gate)
            {
                return field;
            }
        }

        private set;
    }
    = new(false, false, false, null, string.Empty, null, string.Empty, default, 0, 0);

    // Frames for the single display thread, which draws from the newest slot in place.
    public FrameExchange Frames { get; } = new();

    // Copies the newest image unless it is previousSequence; like Frames, for one reader thread only.
    public bool TryCopyFrame(ulong previousSequence, byte[] destination, out VideoFrameInfo info)
    {
        if (destination.Length < VideoOutput.BufferSize)
        {
            throw new ArgumentException("Frame buffer too short.", nameof(destination));
        }

        var slot = Frames.TryTake(previousSequence);
        info = slot?.Info ?? default;
        if (slot is null)
        {
            return false;
        }

        slot.Pixels.CopyTo(destination, 0);
        return true;
    }

    // Parses and hashes a ROM on the worker thread; the session does the disk I/O around it.
    internal Task<PreparedRom> PrepareLoadAsync(byte[] image) => Query(_ =>
        new PreparedRom(CartridgeLoader.Load(image), Convert.ToHexStringLower(SHA256.HashData(image))));

    public async Task LoadAsync(byte[] image, string name) =>
        await LoadPreparedAsync(await PrepareLoadAsync(image).ConfigureAwait(false), name, null).ConfigureAwait(false);

    internal Task LoadPreparedAsync(PreparedRom prepared, string name, BatteryLoad? battery) => Enqueue(system =>
    {
        var loaded = prepared.Loaded;
        if (battery is not null)
        {
            if (loaded.Cartridge is not IBatteryBackedCartridge cartridge)
            {
                throw new InvalidOperationException("Cartridge has no battery RAM.");
            }

            cartridge.ImportRam(battery.Ram); // Validates before insertion.
            if (battery.Clock is { } clock)
            {
                if (loaded.Cartridge is not IRealTimeClockCartridge rtc)
                {
                    throw new InvalidOperationException("Cartridge has no clock.");
                }

                rtc.ImportClock(clock);
                rtc.AdvanceClock(battery.ClockSeconds);
            }
            if (battery.Huc3 is { } mcu)
            {
                if (loaded.Cartridge is not IHuc3ClockCartridge huc3)
                {
                    throw new InvalidOperationException("Cartridge has no clock.");
                }

                huc3.ImportClock(mcu);
                huc3.AdvanceClock(battery.ClockSeconds);
            }
        }
        system.InsertCartridge(loaded.Cartridge);
        romId = prepared.Id;
        batteryCartridge = loaded.Cartridge as IBatteryBackedCartridge;
        clockCartridge = loaded.Cartridge as IRealTimeClockCartridge;
        huc3Cartridge = loaded.Cartridge as IHuc3ClockCartridge;
        rumble = loaded.Info.HasRumble ? loaded.Cartridge as IRumbleCartridge : null;
        camera = loaded.Cartridge as ICameraCartridge;
        title = string.IsNullOrWhiteSpace(loaded.Info.Title) ? name : loaded.Info.Title;
        warnings = string.Join(" / ", loaded.Warnings);
        error = null;
        running = true;
        wakeRequested = false;
        savedState = null;
        buttons = 0;
        inputNeedsSync = false;
        Interlocked.Increment(ref inputEpoch);
        ClearRewind(system);
        ResetTiming(system);
    });

    public Task<SavedState> CaptureStateAsync() => Query(system =>
    {
        savedState = new(system.CaptureState(), error);
        running = false;
        ResetTiming(system);
        return savedState;
    });

    public Task RestoreStateAsync() => Enqueue(system =>
        Restore(system, savedState ?? throw new InvalidOperationException("No state has been captured.")));

    // Restores a state read from a slot and keeps it as the last state.
    public Task RestoreStateAsync(SavedState saved) => Enqueue(system => Restore(system, saved));

    private void Restore(GameBoySystem system, SavedState saved)
    {
        system.RestoreState(saved.State);
        savedState = saved;
        running = false;
        error = saved.Error;
        wakeRequested = false;
        buttons = 0;
        inputNeedsSync = true;
        Interlocked.Increment(ref inputEpoch);
        ClearRewind(system);
        ResetTiming(system);
    }

    // Rewinds and pauses at the newest record at least half an interval old; returns T-cycles undone.
    public Task<ulong?> RewindAsync() => Query<ulong?>(system =>
    {
        var index = rewind.FindLastIndex(state => Old(state, system));
        if (index < 0)
        {
            return null;
        }

        var now = system.TotalTCycles;
        var target = rewind[index];
        rewind.RemoveRange(index + 1, rewind.Count - index - 1);
        system.RestoreState(target);
        running = false;
        error = null;
        wakeRequested = false;
        buttons = 0;
        inputNeedsSync = true;
        Interlocked.Increment(ref inputEpoch);
        nextRewindAt = target.TotalTCycles + rewindOptions.IntervalTCycles;
        ResetTiming(system);
        return now - target.TotalTCycles;
    });

    private bool Old(GameBoyState state, GameBoySystem system) => state.TotalTCycles + (rewindOptions.IntervalTCycles / 2) <= system.TotalTCycles;

    private void ClearRewind(GameBoySystem system)
    {
        rewind.Clear();
        nextRewindAt = system.TotalTCycles + rewindOptions.IntervalTCycles;
    }

    // Records the state once per interval after a run slice, dropping the oldest record at capacity.
    private void RecordRewind(GameBoySystem system)
    {
        if (rewindOptions.Capacity == 0 || system.TotalTCycles < nextRewindAt)
        {
            return;
        }

        if (rewind.Count == rewindOptions.Capacity)
        {
            rewind.RemoveAt(0);
        }

        rewind.Add(system.CaptureState());
        nextRewindAt = system.TotalTCycles + rewindOptions.IntervalTCycles;
    }

    // Sets a copy of the camera image (128 x 112 luminance bytes, 0 = black); null gives flat grey.
    public Task SetCameraImageAsync(byte[]? luminance)
    {
        const int length = ICameraCartridge.ImageWidth * ICameraCartridge.ImageHeight;
        if (luminance is not null && luminance.Length != length)
        {
            throw new ArgumentException($"A camera image is {ICameraCartridge.ImageWidth} x {ICameraCartridge.ImageHeight} luminance bytes ({length}), not {luminance.Length}.", nameof(luminance));
        }

        var image = luminance?.ToArray();
        return Enqueue(_ =>
        {
            if (image is null)
            {
                camera?.ClearImage();
            }
            else
            {
                camera?.SetImage(image);
            }
        });
    }

    public Task<MemorySnapshot> ReadMemoryAsync(ushort address, int length) => Query(system =>
    {
        if (length is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Read 1 to 256 bytes.");
        }

        var bytes = new byte[length];
        system.CopyMemory(address, bytes);
        return new MemorySnapshot(address, bytes, system.GetDebugSnapshot());
    });

    public Task<CapturedSave> CaptureSaveAsync() => Query(system =>
    {
        var wasRunning = running;
        running = false;
        ResetTiming(system);
        return Captured(wasRunning);
    });

    // Captures the same copies without pausing, for periodic saves.
    public Task<CapturedSave> PeekSaveAsync() => Query(_ => Captured(running));

    private CapturedSave Captured(bool wasRunning) => new(romId, batteryCartridge?.ExportRam(), clockCartridge?.ExportClock(), wasRunning,
        Huc3: huc3Cartridge?.ExportClock(), ClockChanges: huc3Cartridge?.ClockChanges ?? 0);

    private async Task<T> Query<T>(Func<GameBoySystem, T> query)
    {
        T result = default!;
        await Enqueue(system => result = query(system)).ConfigureAwait(false);
        return result;
    }

    public Task PauseAsync() => Enqueue(system =>
    {
        running = false;
        ResetTiming(system);
    });

    public Task ResumeAsync() => ResumeAsync(null);

    public Task ResumeAsync(byte? currentButtons) => Enqueue(system =>
    {
        if (!system.IsRomLoaded)
        {
            throw new InvalidOperationException("No ROM is loaded.");
        }

        buttons = currentButtons ?? buttons;
        DiscardAudio(system);
        if (inputNeedsSync)
        {
            system.Joypad.SynchronizeButtons(buttons);
            inputNeedsSync = false;
        }
        else
        {
            ApplyButtons(system, buttons);
        }

        running = true;
        wakeRequested = true;
        ResetTiming(system);
    });

    public Task ResetAsync() => Enqueue(system =>
    {
        system.Reset();
        error = null;
        wakeRequested = false;
        buttons = 0;
        inputNeedsSync = false;
        Interlocked.Increment(ref inputEpoch);
        ClearRewind(system);
        ResetTiming(system);
    });

    // Sets the boot ROM the next load or Reset runs, or null to start from the post-boot state.
    public Task UseBootRomAsync(byte[]? image) => Enqueue(system =>
    {
        if (image is null)
        {
            system.UseBootBypass();
        }
        else
        {
            system.UseBootRom(image);
        }
    });

    public Task StepAsync() => Enqueue(system =>
    {
        running = false;
        ResetTiming(system);
        system.StepInstruction();
    });

    public Task SetButtonsAsync(byte mask)
    {
        var epoch = Interlocked.Read(ref inputEpoch);
        return Enqueue(system =>
        {
            if (epoch != Interlocked.Read(ref inputEpoch))
            {
                return;
            }

            buttons = mask;

            // After a restore, the joypad keeps its captured state until the machine resumes.
            if (!inputNeedsSync)
            {
                ApplyButtons(system, mask);
                wakeRequested = true;
            }
        });
    }

    private static void ApplyButtons(GameBoySystem system, byte mask)
    {
        for (var i = 0; i < 8; i++)
        {
            system.Joypad.SetButtonState((JoypadButton)i, (mask & (1 << i)) != 0);
        }
    }

    private Task Enqueue(Action<GameBoySystem> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (disposed || shutdown)
            {
                return Task.FromException(new ObjectDisposedException(nameof(EmulationRunner)));
            }

            commands.Enqueue(new(action, completion));
            wake.Set();
        }
        return completion.Task;
    }

    private void Work()
    {
        threadStarted?.Invoke();
        var system = new GameBoySystem();
        ResetTiming(system);
        try
        {
            Publish(system, true);
            while (!shutdown)
            {
                while (commands.TryDequeue(out var command))
                {
#pragma warning disable CA1031
                    try
                    {
                        command.Action(system);
                        Publish(system, true);
                        command.Completion.SetResult();
                    }
                    catch (Exception exception)
                    {
                        Publish(system, true);
                        command.Completion.SetException(exception);
                    }
#pragma warning restore CA1031
                }
                if (shutdown)
                {
                    break;
                }

                if (!running || !system.IsRomLoaded || (system.IsStopped && !wakeRequested))
                {
                    ResetTiming(system);
                    Publish(system, true);
                    wake.WaitOne();
                    ResetTiming(system);
                    continue;
                }
                var now = Stopwatch.GetTimestamp();
                var elapsed = (now - lastTick) / (double)Stopwatch.Frequency;
                lastTick = now;
                pacer.Accrue(elapsed);
                fps.Advance(system.Video.CompletedFrameCount, elapsed);
                var budget = pacer.NextBudget;
                if (budget > 0)
                {
                    wakeRequested = false;
#pragma warning disable CA1031
                    try
                    {
                        pacer.Consume(system.RunForTCycles(budget).ExecutedTCycles);
                        DrainAudio(system);
                        RecordRewind(system);
                    }
                    catch (Exception exception)
                    {
                        running = false;
                        error = exception.Message;
                        ResetTiming(system);
                        Publish(system, true);
                    }
#pragma warning restore CA1031
                }
                Publish(system, false);
                if (pacer.NextBudget == 0)
                {
                    wake.WaitOne(1);
                }
            }
        }
        finally
        {
            shutdown = true;
            while (commands.TryDequeue(out var command))
            {
                command.Completion.TrySetException(new ObjectDisposedException(nameof(EmulationRunner)));
            }
        }
    }

    private void ResetTiming(GameBoySystem system)
    {
        lastTick = Stopwatch.GetTimestamp();
        pacer.Reset();
        fps.Reset(system.Video.CompletedFrameCount);
        audio?.Flush();
    }

    // Moves the core's PCM to the host queue after each run slice, so the core's queue never fills.
    private void DrainAudio(GameBoySystem system)
    {
        for (int frames; (frames = system.Audio.ReadFrames(pcm)) > 0;)
        {
            audio?.Write(pcm.AsSpan(0, frames * AudioOutput.ChannelCount));
        }
    }

    private void DiscardAudio(GameBoySystem system)
    {
        while (system.Audio.ReadFrames(pcm) > 0)
        {
        }
    }

    private void Publish(GameBoySystem system, bool forceStatus)
    {
        if (!published || publishedSequence != system.Video.Sequence)
        {
            var slot = Frames.Back;
            slot.Info = system.Video.CopyLatestFrame(slot.Pixels);
            Frames.Publish();
            publishedSequence = slot.Info.Sequence;
            published = true;
        }
        lock (gate)
        {
            var now = Stopwatch.GetTimestamp();
            if (!forceStatus && (now - lastStatusTick) / (double)Stopwatch.Frequency < 0.1)
            {
                return;
            }

            lastStatusTick = now;
            Status = new(system.IsRomLoaded, running, system.IsStopped, system.Fault,
                title, error, warnings, system.GetDebugSnapshot(), fps.Fps, pacer.DroppedSeconds, batteryCartridge is not null, savedState?.State.TotalTCycles,
                rewind.Count == 0 ? 0 : (int)((system.TotalTCycles - rewind[0].TotalTCycles) / GameBoySystem.TCyclesPerSecond),
                rewind.Count != 0 && Old(rewind[0], system), rumble?.MotorOn == true, system.IsBootRomMapped);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            shutdown = true;
            wake.Set();
        }
        thread.Join();
        wake.Dispose();
    }
}
