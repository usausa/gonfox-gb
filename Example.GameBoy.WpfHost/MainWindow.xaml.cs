namespace Example.GameBoy.WpfHost;

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

using Example.GameBoy.WpfHost.Rendering;

using GonFox.GameBoy.Core;
using GonFox.GameBoy.Core.Audio;
using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Cpu;
using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Video;

internal sealed partial class MainWindow : IDisposable
{
    private readonly AudioPlayback audio = new(() => new WasapiAudioDevice());
    private readonly EmulationRunner runner;
    private readonly EmulationSession session;
    private readonly ButtonInputState input = new();
    private readonly StatusText statusText = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Button[] pad;
    private ulong lastSequence = ulong.MaxValue;
    private long lastStatusTick;
    private (string? Error, AudioStreamInfo? Device, bool Playing, bool Priming, int TargetMs, int LatencyMs, long Underruns, long Dropped, int Ppm) audioShown;
    private SessionNotice? noticeShown;
    private CpuFault? faultShown;
    private int memoryRevision;
    private IReadOnlyList<SlotInfo>? slotsShown;
    private int slotShown;
    private bool loading;
    private bool saving;
    private bool debugging;
    private bool choosing;
    private bool closing;
    private bool allowClose;
    private bool closed;
    private bool Busy => loading || saving || debugging || choosing || closing;
    private readonly HostSettingsStore settings = new(WindowsStorage.SettingsPath);
    private string? bootRomPath;
    private string? startupBootRom;
    private readonly System.Windows.Threading.DispatcherTimer autoSave = new() { Interval = AutoSavePolicy.Interval };
    private readonly System.Windows.Threading.DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool renderingHooked;
    private IFrameDisplay? display;
    private FrameRenderer renderer;
    private readonly (Brush Released, Brush Pressed)[] padBrushes;
    private static readonly Brush LampOn = Frozen(Color.FromRgb(0xE2, 0x1B, 0x23));
    private static readonly Brush LampOff = Frozen(Color.FromRgb(0x3B, 0x23, 0x26));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public MainWindow()
    {
        runner = new(audio.Buffer, threadStarted: WorkerStarted);
        session = new(runner, new BatterySaveStore(new FileRecordStore(WindowsStorage.SavesDirectory)),
            states: new StateSlotStore(new FileRecordStore(WindowsStorage.StatesDirectory)));
        InitializeComponent();
        pad = [UpButton, DownButton, LeftButton, RightButton, AButton, BButton, SelectButton, StartButton];
        padBrushes = pad.Select(button => button.Background is SolidColorBrush { Color: var c }
            ? (Frozen(c), Frozen(Color.FromRgb(Lighter(c.R), Lighter(c.G), Lighter(c.B))))
            : (button.Background, button.Background)).ToArray();
        DemoChoice.ItemsSource = BuiltInDemos.All.Select(demo => demo.Name).ToArray();
        DemoChoice.SelectedIndex = 0;
        SlotChoice.ItemsSource = Enumerable.Range(1, StateSlotStore.Count).Select(slot => $"Slot {slot}").ToArray();
        SlotChoice.SelectedIndex = 0;
        RendererChoice.ItemsSource = new[] { "SkiaSharp (CPU scaling)", "WriteableBitmap (WPF scaling)" }; // FrameRenderer order.
        ApplySettings(settings.Load());
        if (RendererChoice.SelectedIndex < 0)
        {
            RendererChoice.SelectedIndex = (int)FrameRenderer.Skia;
        }

        ApplyAudioSettings(this, new RoutedEventArgs());
        statusTimer.Tick += (_, _) => UpdateDisplay();
        statusTimer.Start();
        StateChanged += (_, _) => UpdateDisplay();
        autoSave.Tick += AutoSave;
        autoSave.Start();
        Loaded += LoadStartupRom;
    }

    // Names the run thread for Windows tools when it starts.
    private static void WorkerStarted()
    {
        WindowsThread.DescribeCurrent(EmulationRunner.ThreadName);
    }

    // Shows the newest image each composition frame while running; a 0.1 s timer covers other times.
    private void OnRenderingFrame(object? sender, EventArgs e)
    {
        UpdateDisplay();
    }

    private void HookRendering(bool hook)
    {
        if (hook == renderingHooked)
        {
            return;
        }

        if (hook)
        {
            CompositionTarget.Rendering += OnRenderingFrame;
        }
        else
        {
            CompositionTarget.Rendering -= OnRenderingFrame;
        }

        renderingHooked = hook;
    }

    private void UpdateDisplay()
    {
        // Draws nothing while minimized; the newest image waits in the runner.
        var visible = WindowState != WindowState.Minimized && IsVisible;
        if (visible)
        {
            ShowLatestFrame();
        }

        var current = runner.Status;
        HookRendering(DisplayPolicy.WantsEveryFrame(closed, visible, current));
        var now = Stopwatch.GetTimestamp();
        if ((now - lastStatusTick) / (double)Stopwatch.Frequency < 0.1)
        {
            return;
        }

        lastStatusTick = now;
        var status = current;
        var changed = statusText.Update(status, DebugExpander.IsExpanded);
        if (changed.HasFlag(StatusLines.Title))
        {
            RomTitle.Text = statusText.Title;
        }

        if (changed.HasFlag(StatusLines.Fps))
        {
            FpsText.Text = statusText.Fps;
        }

        if (changed.HasFlag(StatusLines.State))
        {
            StateText.Text = statusText.State;
        }

        var lamp = status.RomLoaded && status.Running && !status.Stopped ? LampOn : LampOff;
        if (!ReferenceEquals(PowerLamp.Fill, lamp))
        {
            PowerLamp.Fill = lamp;
        }

        if (changed.HasFlag(StatusLines.Registers))
        {
            RegistersText.Text = statusText.Registers;
        }

        if (changed.HasFlag(StatusLines.Video))
        {
            VideoStatusText.Text = statusText.Video;
        }

        if (changed.HasFlag(StatusLines.Device))
        {
            DeviceStatusText.Text = statusText.Device;
        }

        if (changed.HasFlag(StatusLines.Snapshot))
        {
            SnapshotText.Text = statusText.Snapshot;
        }

        OpenButton.IsEnabled = DemoButton.IsEnabled = BootRomButton.IsEnabled = !Busy;
        BootBypassButton.IsEnabled = !Busy && bootRomPath is not null;
        ResumeButton.IsEnabled = !Busy && status.RomLoaded && !status.Running;
        PauseButton.IsEnabled = !Busy && status.Running;
        ResetButton.IsEnabled = !Busy && status.RomLoaded;
        StepButton.IsEnabled = !Busy && status.RomLoaded && !status.Running;
        SaveButton.IsEnabled = !Busy && status.HasBattery;
        CaptureStateButton.IsEnabled = !Busy && status.RomLoaded;
        RestoreStateButton.IsEnabled = !Busy && status.RomLoaded && SelectedSlotInfo() is not null;
        RewindButton.IsEnabled = !Busy && status.RewindAvailable;
        if (!ReferenceEquals(slotsShown, session.Slots) || slotShown != SelectedSlot)
        {
            ShowSlot();
        }

        MemoryReadButton.IsEnabled = !Busy && status.RomLoaded;
        if (!ReferenceEquals(noticeShown, session.Notice))
        {
            noticeShown = session.Notice;
            SaveText.Text = HostText.Session(noticeShown);
        }

        ShowProblem(status);

        UpdateAudio(status);
        ShowAudioStatus();
    }

    // Shows a CPU lockup once, or another error whenever it changes, in the message line.
    private void ShowProblem(EmulationStatus status)
    {
        if (status.Fault is { } fault)
        {
            if (fault != faultShown)
            {
                faultShown = fault;
                MessageText.Text = HostText.Lockup(fault);
            }

            return;
        }

        faultShown = null;
        if (status.Error is { } error && !ReferenceEquals(MessageText.Text, error))
        {
            MessageText.Text = error;
        }
    }

    // Starts or stops audio playback with the run state.
    private void UpdateAudio(EmulationStatus status)
    {
        audio.Update(status.Running && !status.Stopped);
    }

    // Updates the audio lines only when their coarse values change, with hysteresis against flicker.
    private void ShowAudioStatus()
    {
        var stats = audio.Buffer.Stats;
        var latencyMs = (stats.AverageFrames * 1000 / AudioOutput.SampleRate) + audio.Latency.TotalMilliseconds;
        var shownLatency = Math.Abs(latencyMs - audioShown.LatencyMs) < 10 ? audioShown.LatencyMs : (int)Math.Round(latencyMs / 10) * 10;
        var shownPpm = Math.Abs(stats.CorrectionPpm - audioShown.Ppm) < 100 ? audioShown.Ppm : (int)Math.Round(stats.CorrectionPpm / 100) * 100;
        (string? Error, AudioStreamInfo? Device, bool Playing, bool Priming, int TargetMs, int LatencyMs, long Underruns, long Dropped, int Ppm) shown =
            (audio.Error, audio.DeviceInfo, stats.Playing, stats.Priming, stats.TargetFrames * 1000 / AudioOutput.SampleRate, shownLatency,
                stats.Underruns, stats.DroppedFrames, shownPpm);
        if (shown == audioShown)
        {
            return;
        }

        audioShown = shown;
        AudioRetryButton.IsEnabled = shown.Error is not null;
        AudioErrorText.Visibility = shown.Error is null ? Visibility.Collapsed : Visibility.Visible;
        var device = HostText.AudioDevice(shown.Device);
        AudioErrorText.Text = shown.Error is { } problem ? $"Audio unavailable: {problem}" : string.Empty;
        AudioText.Text = shown switch
        {
            { Error: { } failure } => $"Audio: unavailable ({failure})",
            { Playing: false } => $"Audio: stopped{(device.Length > 0 ? $" ({device})" : string.Empty)}",
            { Priming: true } => $"Audio: priming {shown.TargetMs} ms · underruns {shown.Underruns} · dropped {shown.Dropped:N0}",
            _ => $"Audio: playing · latency {shown.LatencyMs} ms · {shown.Ppm:+0;-0;0} ppm · underruns {shown.Underruns} · dropped {shown.Dropped:N0} ({device})"
        };
    }

    private void ApplyAudioSettings(object sender, RoutedEventArgs e)
    {
        if (VolumeText is null || MuteCheckBox is null)
        {
            return; // Raised while XAML loads.
        }

        var percent = (int)Math.Round(VolumeSlider.Value);
        audio.SetVolume(percent, MuteCheckBox.IsChecked == true);
        VolumeText.Text = $"{percent}%";
    }

    // Applies saved settings before showing; an off-screen window keeps the default place.
    private void ApplySettings(HostSettings loaded)
    {
        VolumeSlider.Value = loaded.Volume;
        MuteCheckBox.IsChecked = loaded.Muted;
        RendererChoice.SelectedIndex = (int)FrameDisplay.Parse(loaded.Renderer);
        startupBootRom = loaded.BootRom;
        if (loaded.Window is not { } saved)
        {
            return;
        }

        Rect screen = new(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (WindowPlacement.Fit(saved, screen, new Size(MinWidth, MinHeight)) is { } bounds)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            (Left, Top, Width, Height) = (bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        }
        if (saved.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    // Saves the settings with the window's normal bounds, even while maximized or minimized.
    private void SaveSettings()
    {
        var normal = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        WindowBounds? window = normal.IsEmpty ? null : new WindowBounds(normal.Left, normal.Top, normal.Width, normal.Height,
            WindowState == WindowState.Maximized);
        settings.Save(new HostSettings((int)Math.Round(VolumeSlider.Value), MuteCheckBox.IsChecked == true, window, renderer.ToString(),
            bootRomPath));
    }

    private void RetryAudio(object sender, RoutedEventArgs e)
    {
        audio.Retry();
        audioShown = default;
    }

    // Hands the newest image from the runner's slot straight to the display, without a copy.
    private void ShowLatestFrame()
    {
        var slot = runner.Frames.TryTake(lastSequence);
        if (slot is null || display is null)
        {
            return;
        }

        if (!display.Show(slot.Pixels, slot.Info))
        {
            return; // Busy; retried next frame.
        }

        lastSequence = slot.Info.Sequence;
    }

    private void RendererChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RendererChoice.SelectedIndex >= 0)
        {
            UseRenderer((FrameRenderer)RendererChoice.SelectedIndex);
        }
    }

    // Swaps in the display of the other drawing method and shows the current image at once.
    private void UseRenderer(FrameRenderer method)
    {
        if (display is not null && method == renderer)
        {
            return;
        }

        var previous = display;
        display = FrameDisplay.Create(method);
        renderer = method;
        System.Windows.Automation.AutomationProperties.SetName(display.Element, "Game Boy screen");
        DisplayHost.Child = display.Element;
        previous?.Dispose();
        lastSequence = ulong.MaxValue; // Show the current image again.
        ShowLatestFrame();
    }

    // Refits the screen to the new DPI.
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        FitScreen(this, null);
    }

    // Sizes the screen to the largest whole multiple of 160 x 144 device pixels that fits the bezel.
    private void FitScreen(object sender, SizeChangedEventArgs? e)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var edge = ScreenEdge.BorderThickness.Left + ScreenEdge.BorderThickness.Right;
        double width = (ScreenArea.ActualWidth - edge) * dpi.DpiScaleX, height = (ScreenArea.ActualHeight - edge) * dpi.DpiScaleY;
        var scale = Math.Max(1, (int)Math.Min(width / VideoOutput.Width, height / VideoOutput.Height));
        DisplayHost.Width = VideoOutput.Width * scale / dpi.DpiScaleX;
        DisplayHost.Height = VideoOutput.Height * scale / dpi.DpiScaleY;
    }

    // Opens a ROM chosen in the file dialog.
    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void OpenRom(object sender, RoutedEventArgs e)
    {
        if (Busy)
        {
            return;
        }

        choosing = true;
        string? path;
#pragma warning disable CA1031
        try
        {
            path = await RomFileDialog.ChooseAsync(this);
        }
        catch (Exception exception)
        {
            if (!closed)
            {
                MessageText.Text = $"File dialog failed: {exception.Message}";
            }

            return;
        }
        finally
        {
            choosing = false;
        }
#pragma warning restore CA1031
        if (path is not null && !closed && !closing)
        {
            await LoadRomFileAsync(path);
        }
    }

    // Reads the boot ROM of the settings, then opens a ROM path given on the command line.
    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void LoadStartupRom(object sender, RoutedEventArgs e)
    {
        audio.Prepare();
        if (startupBootRom is { } bootRom)
        {
            await UseBootRomFileAsync(bootRom, startup: true);
        }

        var arguments = Environment.GetCommandLineArgs();
        if (arguments.Length > 1 && !arguments[1].StartsWith("--", StringComparison.Ordinal) && !Busy && !closed)
        {
            await LoadRomFileAsync(arguments[1]);
        }
    }

    // Sets a 256-byte DMG boot ROM chosen in the file dialog for the next load or Reset.
    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void ChooseBootRom(object sender, RoutedEventArgs e)
    {
        if (Busy)
        {
            return;
        }

        choosing = true;
        string? path;
#pragma warning disable CA1031
        try
        {
            path = await RomFileDialog.ChooseAsync(this, RomFileDialog.BootRomFilter);
        }
        catch (Exception exception)
        {
            if (!closed)
            {
                MessageText.Text = $"File dialog failed: {exception.Message}";
            }

            return;
        }
        finally
        {
            choosing = false;
        }
#pragma warning restore CA1031
        if (path is not null && !closed && !closing)
        {
            await UseBootRomFileAsync(path, startup: false);
        }
    }

    private async Task UseBootRomFileAsync(string path, bool startup)
    {
        loading = true;
#pragma warning disable CA1031
        try
        {
            var image = await ReadBootRomAsync(path, lifetime.Token);
            if (closed || closing)
            {
                return;
            }

            await runner.UseBootRomAsync(image);
            bootRomPath = path;
            ShowBootRom();
            if (!startup)
            {
                MessageText.Text = "Boot ROM set";
            }
        }
        catch (Exception exception)
        {
            if (closed)
            {
                return;
            }

            MessageText.Text = $"Boot ROM unusable: {exception.Message}";
            if (startup)
            {
                ShowBootRom($"{Path.GetFileName(path)} unusable");
            }
        }
        finally
        {
            loading = false;
        }
#pragma warning restore CA1031
    }

    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void UseBootBypass(object sender, RoutedEventArgs e)
    {
        if (Busy || bootRomPath is null)
        {
            return;
        }

        await Execute(() => runner.UseBootRomAsync(null));
        bootRomPath = null;
        ShowBootRom();
        if (!closed)
        {
            MessageText.Text = "Boot ROM removed";
        }
    }

    private void ShowBootRom(string? problem = null) => BootRomText.Text = bootRomPath is { } path
        ? $"Boot ROM: {Path.GetFileName(path)}"
        : $"Boot ROM: none{(problem is null ? string.Empty : $" ({problem})")}";

    private static async Task<byte[]> ReadBootRomAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        if (stream.Length != GameBoySystem.BootRomSize)
        {
            throw new InvalidDataException($"A DMG boot ROM is {GameBoySystem.BootRomSize} bytes, not {stream.Length:N0}.");
        }

        var image = new byte[GameBoySystem.BootRomSize];
        await stream.ReadExactlyAsync(image, cancellationToken);
        return image;
    }

    private async Task LoadRomFileAsync(string path)
    {
        loading = true;
#pragma warning disable CA1031
        try
        {
            var image = await ReadRomImageAsync(path, lifetime.Token);
            if (closed || closing)
            {
                return;
            }

            ClearInput();
            await session.LoadAsync(image, Path.GetFileName(path));
            if (!closed)
            {
                UpdateAudio(runner.Status);
                MessageText.Text = runner.Status.Warnings;
                ClearMemoryView();
                await RefreshMemoryAsync();
            }
        }
        catch (Exception exception)
        {
            if (!closed)
            {
                MessageText.Text = $"Load failed: {exception.Message}";
            }
        }
        finally
        {
            loading = false;
        }
#pragma warning restore CA1031
    }

    private static async Task<byte[]> ReadRomImageAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        if (stream.Length > CartridgeLoader.MaxRomSizeBytes)
        {
            throw new InvalidDataException($"A ROM is at most {CartridgeLoader.MaxRomSizeBytes / 1024 / 1024} MiB.");
        }

        var image = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(image, cancellationToken);
        return image;
    }

    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void OpenDemo(object sender, RoutedEventArgs e)
    {
        if (Busy)
        {
            return;
        }

        loading = true;
#pragma warning disable CA1031
        try
        {
            var demo = BuiltInDemos.All[Math.Clamp(DemoChoice.SelectedIndex, 0, BuiltInDemos.All.Length - 1)];
            var image = BuiltInDemos.Load(demo.Resource);
            ClearInput();
            await session.LoadAsync(image, demo.Name);
            if (!closed)
            {
                UpdateAudio(runner.Status);
                MessageText.Text = string.Empty;
                ClearMemoryView();
                await RefreshMemoryAsync();
            }
        }
        catch (Exception exception)
        {
            if (!closed)
            {
                MessageText.Text = exception.Message;
            }
        }
        finally
        {
            loading = false;
        }
#pragma warning restore CA1031
    }

    private async Task Execute(Func<Task> action)
    {
#pragma warning disable CA1031
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            if (!closed)
            {
                MessageText.Text = exception.Message;
            }
        }
#pragma warning restore CA1031
        if (!closed)
        {
            UpdateAudio(runner.Status);
        }
    }

    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void Resume(object sender, RoutedEventArgs e)
    {
        if (Busy)
        {
            return;
        }

        SynchronizeKeyboard();
        await Execute(() => runner.ResumeAsync(input.Mask));
    }

    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void Pause(object sender, RoutedEventArgs e)
    {
        if (!Busy)
        {
            await Execute(runner.PauseAsync);
        }
    }

    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void Step(object sender, RoutedEventArgs e)
    {
        if (Busy)
        {
            return;
        }

        await Execute(async () =>
        {
            await runner.StepAsync();
            await RefreshMemoryAsync();
        });
    }

    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void Reset(object sender, RoutedEventArgs e)
    {
        if (Busy)
        {
            return;
        }

        ClearInput();
        await Execute(runner.ResetAsync);
        if (!closed)
        {
            MessageText.Text = "Reset";
            await Execute(RefreshMemoryAsync);
        }
    }

    private int SelectedSlot => SlotChoice.SelectedIndex + 1;
    private SlotInfo? SelectedSlotInfo() => session.Slots.FirstOrDefault(info => info.Slot == SelectedSlot);

    private void ShowSlot()
    {
        slotsShown = session.Slots;
        slotShown = SelectedSlot;
        SlotText.Text = SelectedSlotInfo() is { } info ? $"Saved {info.Saved.ToLocalTime():yyyy-MM-dd HH:mm:ss}" : "Empty";
    }

    private void SlotChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SlotText is not null)
        {
            ShowSlot();
        }
    }

    // Saves the state to the chosen slot of the loaded ROM, leaving the machine paused.
    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void CaptureState(object sender, RoutedEventArgs e)
    {
        if (Busy)
        {
            return;
        }

        debugging = true;
        var slot = SelectedSlot;
#pragma warning disable CA1031
        try
        {
            await session.SaveStateAsync(slot);
            UpdateAudio(runner.Status);
            if (!closed)
            {
                MessageText.Text = $"Saved to slot {slot}";
            }

            await RefreshMemoryAsync();
        }
        catch (Exception exception)
        {
            if (!closed)
            {
                MessageText.Text = $"Slot {slot} save failed: {exception.Message}";
            }
        }
        finally
        {
            debugging = false;
        }
#pragma warning restore CA1031
    }

    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void RestoreState(object sender, RoutedEventArgs e)
    {
        if (Busy)
        {
            return;
        }

        debugging = true;
        ClearInput();
        var slot = SelectedSlot;
#pragma warning disable CA1031
        try
        {
            await session.LoadStateAsync(slot);
            UpdateAudio(runner.Status);
            if (!closed)
            {
                MessageText.Text = $"Loaded slot {slot}";
            }

            await RefreshMemoryAsync();
        }
        catch (Exception exception)
        {
            if (!closed)
            {
                MessageText.Text = $"Slot {slot} load failed: {exception.Message}";
            }
        }
        finally
        {
            debugging = false;
        }
#pragma warning restore CA1031
    }

    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void Rewind(object sender, RoutedEventArgs e)
    {
        if (Busy)
        {
            return;
        }

        debugging = true;
        ClearInput();
#pragma warning disable CA1031
        try
        {
            var back = await runner.RewindAsync();
            UpdateAudio(runner.Status);
            if (!closed)
            {
                MessageText.Text = back is { } cycles
                ? $"Rewound {cycles / (double)GameBoySystem.CyclesPerSecond:F1} s"
                : "Nothing to rewind";
            }

            await RefreshMemoryAsync();
        }
        catch (Exception exception)
        {
            if (!closed)
            {
                MessageText.Text = exception.Message;
            }
        }
        finally
        {
            debugging = false;
        }
#pragma warning restore CA1031
    }

    private void ClearMemoryView()
    {
        memoryRevision++;
        MemoryText.Clear();
        MemoryTimeText.Text = "Not read";
    }

    private async Task RefreshMemoryAsync()
    {
        if (closed || closing || !DebugExpander.IsExpanded || !MemoryExpander.IsExpanded || !runner.Status.RomLoaded)
        {
            return;
        }

        var revision = ++memoryRevision;
        if (!MemoryView.TryParseRange(MemoryAddress.Text, MemoryLength.Text, out var address, out var length))
        {
            MemoryText.Clear();
            MemoryTimeText.Text = HostText.InvalidMemoryRange;
            return;
        }

#pragma warning disable CA1031
        try
        {
            var memory = await runner.ReadMemoryAsync(address, length);
            if (closed || revision != memoryRevision)
            {
                return;
            }

            MemoryText.Text = MemoryView.Format(memory);
            MemoryTimeText.Text = $"T={memory.Registers.TotalTCycles:N0} PC={memory.Registers.PC:X4}";
        }
        catch (Exception exception)
        {
            if (closed || revision != memoryRevision)
            {
                return;
            }

            MemoryText.Clear();
            MemoryTimeText.Text = exception.Message;
        }
#pragma warning restore CA1031
    }

    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void ReadMemory(object sender, RoutedEventArgs e)
    {
        if (Busy)
        {
            return;
        }

        debugging = true;
        try
        {
            await Execute(RefreshMemoryAsync);
        }
        finally
        {
            debugging = false;
        }
    }

    private void MemoryExpanded(object sender, RoutedEventArgs e) => ReadMemory(sender, e);
    private void MemoryFocused(object sender, KeyboardFocusChangedEventArgs e) => ClearInput();

    private void SynchronizeKeyboard()
    {
        var allow = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0;
        foreach (var key in new[] { Key.Right, Key.Left, Key.Up, Key.Down, Key.Z, Key.J, Key.X, Key.K, Key.Back, Key.LeftShift, Key.RightShift, Key.Enter })
        {
            input.Set($"key:{key}", MapKey(key)!.Value, allow && Keyboard.IsKeyDown(key));
        }

        UpdatePad();
    }

    // Saves the battery RAM periodically without pausing, skipping while other work runs.
    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void AutoSave(object? sender, EventArgs e)
    {
        if (!Busy && !closed)
        {
            await session.AutoSaveAsync();
        }
    }

    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void SaveRam(object sender, RoutedEventArgs e)
    {
        if (Busy)
        {
            return;
        }

        saving = true;
        ClearInput();
#pragma warning disable CA1031
        try
        {
            await session.SaveAsync();
            if (!closed)
            {
                MessageText.Text = "RAM saved";
            }
        }
        catch (Exception exception)
        {
            if (!closed)
            {
                MessageText.Text = exception.Message;
            }
        }
        finally
        {
            saving = false;
        }
#pragma warning restore CA1031
    }

    private static JoypadButton? MapKey(Key key) => key switch
    {
        Key.Right => JoypadButton.Right,
        Key.Left => JoypadButton.Left,
        Key.Up => JoypadButton.Up,
        Key.Down => JoypadButton.Down,
        Key.Z or Key.J => JoypadButton.A,
        Key.X or Key.K => JoypadButton.B,
        Key.Back or Key.LeftShift or Key.RightShift => JoypadButton.Select,
        Key.Enter => JoypadButton.Start,
        _ => null
    };

    private void HandleKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase)
        {
            return;
        }

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
        {
            return;
        }

        if (MapKey(e.Key) is not { } button)
        {
            return;
        }

        if (!e.IsRepeat)
        {
            SetInput($"key:{e.Key}", button, true);
        }

        e.Handled = true;
    }

    private void HandleKeyUp(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase)
        {
            return;
        }

        if (MapKey(e.Key) is not { } button)
        {
            return;
        }

        SetInput($"key:{e.Key}", button, false);
        e.Handled = true;
    }

    private void PadMouseDown(object sender, MouseButtonEventArgs e)
    {
        var button = (Button)sender;
        button.CaptureMouse();
        SetInput($"mouse:{button.Tag}", (JoypadButton)button.Tag, true);
        e.Handled = true;
    }

    private void PadMouseUp(object sender, MouseButtonEventArgs e)
    {
        var button = (Button)sender;
        SetInput($"mouse:{button.Tag}", (JoypadButton)button.Tag, false);
        button.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void PadMouseLost(object sender, MouseEventArgs e)
    {
        var button = (Button)sender;
        SetInput($"mouse:{button.Tag}", (JoypadButton)button.Tag, false);
    }

    private void PadTouchDown(object sender, TouchEventArgs e)
    {
        var button = (Button)sender;
        button.CaptureTouch(e.TouchDevice);
        SetInput($"touch:{e.TouchDevice.Id}", (JoypadButton)button.Tag, true);
        e.Handled = true;
    }

    private void PadTouchUp(object sender, TouchEventArgs e)
    {
        var button = (Button)sender;
        SetInput($"touch:{e.TouchDevice.Id}", (JoypadButton)button.Tag, false);
        button.ReleaseTouchCapture(e.TouchDevice);
        e.Handled = true;
    }

    // Presses the button for 0.1 s (about six frames) on a UI Automation invoke.
    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void PadClick(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var source = $"invoke:{button.Tag}";
        SetInput(source, (JoypadButton)button.Tag, true);
        await Task.Delay(100);
        SetInput(source, (JoypadButton)button.Tag, false);
    }

    private void PadTouchLost(object sender, TouchEventArgs e)
    {
        var button = (Button)sender;
        SetInput($"touch:{e.TouchDevice.Id}", (JoypadButton)button.Tag, false);
    }

    private void SetInput(string source, JoypadButton button, bool pressed)
    {
        if (closed || closing || (Busy && pressed) || !input.Set(source, button, pressed))
        {
            return;
        }

        UpdatePad();
        _ = Execute(() => runner.SetButtonsAsync(input.Mask));
    }

    private void UpdatePad()
    {
        for (var i = 0; i < pad.Length; i++)
        {
            pad[i].Background = (input.Mask & (1 << (int)(JoypadButton)pad[i].Tag)) != 0 ? padBrushes[i].Pressed : padBrushes[i].Released;
        }
    }

    private static byte Lighter(byte channel) => (byte)(channel + ((255 - channel) * 2 / 5));

    // Updates the debugging rows at once when the debugging section opens or closes.
    private void DebugExpanderChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        lastStatusTick = 0;
        UpdateDisplay();
        if (DebugExpander.IsExpanded)
        {
            _ = Execute(RefreshMemoryAsync);
        }
    }

    private void ClearInput()
    {
        input.Clear();
        foreach (var button in pad)
        {
            button.ReleaseMouseCapture();
            button.ReleaseAllTouchCaptures();
        }
        UpdatePad();
        if (!closed)
        {
            _ = Execute(() => runner.SetButtonsAsync(0));
        }
    }

    private void HandleDeactivated(object? sender, EventArgs e) => ClearInput();

    // ReSharper disable once AsyncVoidEventHandlerMethod
    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (allowClose)
        {
            // Closes for real once the battery RAM is saved.
            SaveSettings();
            return;
        }

        e.Cancel = true;
        if (closing)
        {
            return;
        }

        closing = true;
        ClearInput();
#pragma warning disable CA1031
        try
        {
            await System.Windows.Threading.Dispatcher.Yield(); // Let Closing finish first.
            await session.SaveForCloseAsync();
            allowClose = true;
            Close();
        }
        catch (Exception exception)
        {
            closing = false;
            MessageText.Text = $"Close canceled, save failed: {exception.Message}";
        }
#pragma warning restore CA1031
    }

    protected override void OnClosed(EventArgs e)
    {
        closed = true;
        lifetime.Cancel();
        autoSave.Stop();
        statusTimer.Stop();
        HookRendering(false);
        ClearInput();
        Dispose();
        base.OnClosed(e);
    }

    // Ends the emulation the window owns.
    public void Dispose()
    {
        audio.Dispose(); // Before the runner.
        runner.Dispose();
        display?.Dispose();
        lifetime.Dispose();
        session.Dispose(); // After the final save.
    }
}
