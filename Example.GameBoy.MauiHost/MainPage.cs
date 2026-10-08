namespace Example.GameBoy.MauiHost;

using GonFox.GameBoy.Core;
using GonFox.GameBoy.Core.Audio;
using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Cpu;
using GonFox.GameBoy.Core.Devices;

using Microsoft.Maui.Controls.Shapes;

// The handheld page and composition root of the app; one gate runs a single command at a time.
public sealed class MainPage : ContentPage, IDisposable
{
    private const string LastRomRecord = "last-rom.gb";
    private const string BootRomRecord = "boot-rom.bin";
    private const int ResumeSlot = 1; // One state per ROM.
    private static readonly Color Body = Color.FromArgb("#C9CBC4");
    private static readonly Color Ink = Color.FromArgb("#2E3138");
    private readonly AudioPlayback audio = new(() => new AudioTrackDevice());
    private readonly EmulationRunner runner;
    private readonly EmulationSession session;
    private readonly StateSlotStore resume = new(new FileRecordStore(AndroidStorage.ResumeDirectory));
    private readonly FileRecordStore library = new(AndroidStorage.LibraryDirectory);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ButtonInputState input = new();
    private readonly ScreenView screen = new();
    private readonly PadView pad = new();
    private readonly Label title = new() { FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, LineBreakMode = LineBreakMode.TailTruncation };
    private readonly Label state = new() { FontSize = 12, TextColor = Color.FromArgb("#B8BCC6"), HorizontalTextAlignment = TextAlignment.End };
    private readonly Label message = new() { FontSize = 12, TextColor = Color.FromArgb("#E8D7A8"), MaxLines = 3, LineBreakMode = LineBreakMode.WordWrap };
    private readonly RumbleMotor rumble = new();
    private readonly Switch muteSwitch = new() { AutomationId = "MuteSwitch" };
    private readonly Switch hapticsSwitch = new() { AutomationId = "HapticsSwitch" };
    private readonly Switch rumbleSwitch = new() { AutomationId = "RumbleSwitch" };
    private readonly Slider volumeSlider = new(0, 100, AppSettings.DefaultVolume) { AutomationId = "Volume" };
    private readonly Label volumeText = SettingText(string.Empty);
    private readonly Label bootRomText = SettingText(string.Empty);
    private readonly Button pause;
    private readonly Button settingsButton;
    private readonly View settings;
    private Button? removeBootRom;
    private IDispatcherTimer? timer;
    private Task suspension = Task.CompletedTask;
    private ulong lastSequence = ulong.MaxValue;
    private long lastStatus;
    private long lastLog;
    private long lastAutoSave;
    private EmulationStatus? statusShown;
    private SessionNotice? noticeShown;
    private CpuFault? faultShown;
    private int volume;
    private bool muted;
    private bool haptics;
    private bool rumbleEnabled;
    private bool bootRomMapped;
    private bool started;
    private bool wasRunning;
    private bool suspended;
    private bool closed;

    public MainPage()
    {
        runner = new EmulationRunner(audio.Buffer);
        session = new EmulationSession(runner, new BatterySaveStore(new FileRecordStore(AndroidStorage.SavesDirectory)),
            states: new StateSlotStore(new FileRecordStore(AndroidStorage.StatesDirectory)));
        BackgroundColor = Color.FromArgb("#2B2E35");
        pause = Command("Pause", "Pause", TogglePause);
        settingsButton = Command("Settings", "Settings", ToggleSettings);
        var commands = new Grid { ColumnDefinitions = Columns(6), ColumnSpacing = 4, Padding = new Thickness(6, 4) };
        commands.Add(Command("ROM", "OpenRom", PickRomAsync), 0);
        commands.Add(Command("Demo", "Demo", ChooseDemoAsync), 1);
        commands.Add(pause, 2);
        commands.Add(Command("Reset", "Reset", ResetAsync), 3);
        commands.Add(Command("State", "State", StateAsync), 4);
        commands.Add(settingsButton, 5);
        var header = new Grid { ColumnDefinitions = Columns(2), Padding = new Thickness(10, 8, 10, 2) };
        header.Add(title, 0);
        header.Add(state, 1);
        var screenFrame = new Border
        {
            Content = screen,
            Stroke = Color.FromArgb("#5B5F6A"),
            StrokeThickness = 2,
            Margin = new Thickness(10, 4),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Color.FromArgb("#202429")
        };
        settings = BuildSettings();
        var lower = new Grid { BackgroundColor = Body };
        lower.Add(pad);
        lower.Add(settings);
        var layout = new Grid
        {
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)]
        };
        layout.Add(header, 0);
        layout.Add(screenFrame, 0, 1);
        layout.Add(commands, 0, 2);
        layout.Add(new ContentView { Content = message, Padding = new Thickness(10, 0, 10, 4) }, 0, 3);
        layout.Add(lower, 0, 4);
        Content = layout;
        SizeChanged += (_, _) => screen.HeightRequest = (Math.Max(0, Width - 24) * 144 / 160) + 4;
        pad.Changed += OnButton;
        LaunchRequest.Arrived += OnLaunchRequest;
    }

    private static ColumnDefinitionCollection Columns(int count)
    {
        var columns = new ColumnDefinitionCollection();
        for (var i = 0; i < count; i++)
        {
            columns.Add(new ColumnDefinition(GridLength.Star));
        }

        return columns;
    }

    private Button Command(string text, string id, Func<Task> action)
    {
        var button = new Button
        {
            Text = text,
            AutomationId = id,
            FontSize = 12,
            Padding = new Thickness(2, 0),
            HeightRequest = 40,
            BackgroundColor = Color.FromArgb("#3A3E47"),
            TextColor = Colors.White,
            CornerRadius = 6
        };
        button.Clicked += async (_, _) => await Exclusive(action, wait: false);
        return button;
    }

    // Runs one action at a time; a tap while busy is dropped, while start and launch requests wait.
    private async Task Exclusive(Func<Task> action, bool wait)
    {
        if (wait)
        {
            await gate.WaitAsync();
        }
        else if (!await gate.WaitAsync(0))
        {
            return;
        }

#pragma warning disable CA1031
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            Show($"Failed: {exception.Message}");
        }
        finally
        {
            gate.Release();
        }
#pragma warning restore CA1031
    }

    // Builds the settings that take the place of the buttons: sound, haptics, rumble and boot ROM.
    private ScrollView BuildSettings()
    {
        volume = AppSettings.Volume;
        muted = AppSettings.Muted;
        haptics = AppSettings.Haptics;
        rumbleEnabled = AppSettings.Rumble;
        muteSwitch.IsToggled = muted;
        volumeSlider.Value = volume;
        hapticsSwitch.IsToggled = haptics;
        rumbleSwitch.IsToggled = rumbleEnabled;
        muteSwitch.Toggled += (_, e) =>
        {
            muted = e.Value;
            AppSettings.Muted = e.Value;
            ApplyVolume();
        };
        volumeSlider.ValueChanged += (_, e) =>
        {
            var chosen = (int)Math.Round(e.NewValue);
            if (chosen == volume)
            {
                return;
            }

            volume = chosen;
            AppSettings.Volume = chosen;
            ApplyVolume();
        };
        hapticsSwitch.Toggled += (_, e) =>
        {
            haptics = e.Value;
            AppSettings.Haptics = e.Value;
        };
        rumbleSwitch.Toggled += (_, e) =>
        {
            rumbleEnabled = e.Value;
            AppSettings.Rumble = e.Value;
        };
        var bootRom = new Grid { ColumnDefinitions = Columns(2), ColumnSpacing = 8 };
        bootRom.Add(Command("Choose boot ROM", "ChooseBootRom", ChooseBootRomAsync), 0);
        bootRom.Add(removeBootRom = Command("Remove boot ROM", "RemoveBootRom", RemoveBootRomAsync), 1);
        var rows = new VerticalStackLayout { Spacing = 10, Padding = new Thickness(16, 12) };
        rows.Add(Row(SettingText("Mute"), muteSwitch, fillRight: false));
        rows.Add(Row(volumeText, volumeSlider, fillRight: true));
        rows.Add(Row(SettingText("Button haptics"), hapticsSwitch, fillRight: false));
        rows.Add(Row(SettingText(rumble.Available ? "Rumble" : "Rumble (no vibrator)"), rumbleSwitch, fillRight: false));
        rows.Add(bootRomText);
        rows.Add(bootRom);
        ShowBootRom();
        return new ScrollView { Content = rows, IsVisible = false };
    }

    private static Label SettingText(string text) => new() { Text = text, FontSize = 14, TextColor = Ink, VerticalTextAlignment = TextAlignment.Center };

    private static Grid Row(View left, View right, bool fillRight)
    {
        var row = new Grid
        {
            ColumnDefinitions = fillRight ? [new(GridLength.Auto), new(GridLength.Star)] : [new(GridLength.Star), new(GridLength.Auto)],
            ColumnSpacing = 12,
            MinimumHeightRequest = 40
        };
        row.Add(left, 0);
        row.Add(right, 1);
        return row;
    }

    private Task ToggleSettings()
    {
        var show = !settings.IsVisible;
        if (show)
        {
            ClearInput();
        }

        settings.IsVisible = show;
        pad.IsVisible = !show;
        settingsButton.Text = show ? "Close" : "Settings";
        return Task.CompletedTask;
    }

    private void ApplyVolume()
    {
        audio.SetVolume(volume, muted);
        volumeText.Text = $"Volume {volume}%";
        AppLog.Info($"volume {volume} muted={muted}");
    }

    private void ShowBootRom()
    {
        var name = AppSettings.BootRom;
        bootRomText.Text = name is not null ? $"Boot ROM: {name}" : "Boot ROM: none";
        if (removeBootRom is not null)
        {
            removeBootRom.IsEnabled = name is not null;
            removeBootRom.Opacity = name is not null ? 1 : 0.5;
        }
    }

    // Sets up the audio, the display timer and the start when the page is first shown.
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (timer is null)
        {
            ApplyVolume();
            audio.Prepare();
            timer = Dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(8);
            timer.Tick += (_, _) => Tick();
            _ = Exclusive(StartAsync, wait: true);
        }
        if (!suspended)
        {
            timer.Start();
        }
    }

    // Opens the launch request, else the last ROM (restored after a process end), else a demo.
    private async Task StartAsync()
    {
        started = true;
        var request = LaunchRequest.Take();
        await UseSavedBootRomAsync();
        string? problem = null;
        if (request is not null)
        {
#pragma warning disable CA1031
            try
            {
                if (await ApplyAsync(request))
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                problem = $"Failed: {exception.Message}";
            }
#pragma warning restore CA1031
        }
        var opened = false;
#pragma warning disable CA1031
        try
        {
            opened = await OpenLastAsync(LaunchRequest.Restored);
        }
        catch (Exception exception)
        {
            problem ??= $"Last ROM unavailable: {exception.Message}";
        }
#pragma warning restore CA1031
        if (!opened)
        {
            await LoadDemoAsync(0, remember: false);
        }

        if (problem is not null)
        {
            Show(problem);
        }
    }

    private void OnLaunchRequest() => Dispatcher.Dispatch(() =>
    {
        if (started && !closed)
        {
            _ = Exclusive(HandleLaunchAsync, wait: true);
        }
    });

    private async Task HandleLaunchAsync()
    {
        if (LaunchRequest.Take() is { } request)
        {
            await ApplyAsync(request);
        }
    }

    // Applies a launch request; returns whether a ROM or a demo was opened.
    private async Task<bool> ApplyAsync(LaunchRequest request)
    {
        if (request.Muted is { } requestedMuted)
        {
            muteSwitch.IsToggled = requestedMuted;
        }

        if (request.Volume is { } requestedVolume)
        {
            volumeSlider.Value = Math.Clamp(requestedVolume, 0, 100);
        }

        if (request.BootRom is "none")
        {
            await RemoveBootRomAsync();
        }
        else if (request.BootRom is { } bootRom)
        {
            await UseBootRomAsync(await ReadFileAsync(bootRom, GameBoySystem.BootRomSize, BootRomSizeMessage), System.IO.Path.GetFileName(bootRom));
        }

        var opened = true;
        if (request.Rom is { } path)
        {
            await OpenRomAsync(await ReadFileAsync(path, CartridgeLoader.MaxRomSizeBytes, RomSizeMessage), System.IO.Path.GetFileName(path));
        }
        else if (request.Demo is { } demo)
        {
            await LoadDemoAsync(demo);
        }
        else
        {
            opened = false;
        }

        return opened;
    }

    // Shows the newest image each tick, and the status, auto-save and log at slower intervals.
    private void Tick()
    {
        if (closed)
        {
            return;
        }

        if (runner.Frames.TryTake(lastSequence) is { } slot)
        {
            screen.Show(slot.Pixels);
            lastSequence = slot.Info.Sequence;
        }
        var now = Environment.TickCount64;
        if (now - lastStatus < 100)
        {
            return;
        }

        lastStatus = now;
        var status = runner.Status;
        if (!ReferenceEquals(status, statusShown))
        {
            statusShown = status; // New record on each change.
            title.Text = HostText.Title(status);
            state.Text = $"{HostText.State(status)}  FPS {status.Fps:F1}";
        }

        ShowProblem(status);

        pause.Text = status.Running ? "Pause" : "Resume";
        audio.Update(status.Running && !status.Stopped && !suspended);
        var motor = rumbleEnabled && status.Rumble && status.Running && !suspended;
        if (rumble.Update(motor))
        {
            AppLog.Info($"rumble {(motor ? "on" : "off")}");
        }

        if (status.BootRomMapped != bootRomMapped)
        {
            bootRomMapped = status.BootRomMapped;
            AppLog.Info($"boot rom mapped={bootRomMapped} t={status.Registers.TotalTCycles}");
        }
        if (!ReferenceEquals(session.Notice, noticeShown))
        {
            noticeShown = session.Notice;
            AppLog.Info($"session {noticeShown}");
        }
        if (now - lastAutoSave >= (long)AutoSavePolicy.Interval.TotalMilliseconds)
        {
            lastAutoSave = now;
            _ = session.AutoSaveAsync();
        }
        if (now - lastLog >= 5000)
        {
            lastLog = now;
            var audioStats = audio.Buffer.Stats;
            AppLog.Info($"status title={HostText.Title(status)} state={HostText.State(status)} fps={status.Fps:F1} t={status.Registers.TotalTCycles} " +
                $"boot={status.BootRomMapped} dropped={status.DroppedSeconds * 1000:F0}ms audio={audio.Error ?? HostText.AudioDevice(audio.DeviceInfo)} playing={audioStats.Playing} " +
                $"queued={audioStats.QueuedFrames * 1000 / AudioOutput.SampleRate}ms target={audioStats.TargetFrames * 1000 / AudioOutput.SampleRate}ms " +
                $"underruns={audioStats.Underruns} droppedFrames={audioStats.DroppedFrames}");
        }
    }

    // Shows a CPU lockup once, or another error whenever it changes, in the message line.
    private void ShowProblem(EmulationStatus status)
    {
        if (status.Fault is { } fault)
        {
            if (fault != faultShown)
            {
                faultShown = fault;
                message.Text = HostText.Lockup(fault);
            }

            return;
        }

        faultShown = null;
        if (status.Error is { } error && message.Text != error)
        {
            message.Text = error;
        }
    }

    private void OnButton(string source, JoypadButton button, bool pressed)
    {
        if (closed || !input.Set(source, button, pressed))
        {
            return;
        }

        var felt = pressed && haptics && ButtonHaptics.Press(pad.Handler?.PlatformView as Android.Views.View);
        pad.ShowMask(input.Mask);
        AppLog.Debug($"button {button} {(pressed ? "down" : "up")} mask={input.Mask:X2} source={source}{(felt ? " haptic" : string.Empty)}");
        _ = Run(() => runner.SetButtonsAsync(input.Mask));
    }

    private void ClearInput()
    {
        pad.ReleaseAll();
        input.Clear();
        pad.ShowMask(0);
        _ = Run(() => runner.SetButtonsAsync(0));
    }

    private async Task Run(Func<Task> action)
    {
#pragma warning disable CA1031
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            Show($"{exception.GetType().Name}: {exception.Message}");
        }
#pragma warning restore CA1031
    }

    private void Show(string text)
    {
        message.Text = text;
        AppLog.Info($"message {text}");
    }

    private async Task LoadDemoAsync(int index, bool remember = true)
    {
        index = Math.Clamp(index, 0, BuiltInDemos.All.Length - 1);
        var demo = BuiltInDemos.All[index];
        ClearInput();
        await session.LoadAsync(BuiltInDemos.Load(demo.Resource), demo.Name);
        if (remember)
        {
            AppSettings.Last = $"demo:{index}";
        }

        Show($"Opened {demo.Name}");
        AppLog.Info($"loaded demo {demo.Name}");
    }

    // Opens a ROM and keeps a copy in the library for the next start.
    private async Task OpenRomAsync(byte[] image, string name, bool remember = true)
    {
        ClearInput();
        await session.LoadAsync(image, name);
        string? note = null;
        if (remember)
        {
            try
            {
                await library.WriteAsync(LastRomRecord, image);
                AppSettings.Last = $"rom:{name}";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                note = $" (not kept for the next start: {exception.Message})";
            }
        }
        Show((runner.Status.Warnings is { Length: > 0 } warnings ? warnings : $"Opened {name}") + note);
        AppLog.Info($"loaded rom {name} ({image.Length} bytes)");
    }

    // Reopens the last ROM or demo, restoring its background state if asked; false if none.
    private async Task<bool> OpenLastAsync(bool restore)
    {
        var last = AppSettings.Last;
        if (last?.StartsWith("demo:", StringComparison.Ordinal) == true && int.TryParse(last.AsSpan(5), out var demo))
        {
            await LoadDemoAsync(demo, remember: false);
        }
        else if (last?.StartsWith("rom:", StringComparison.Ordinal) == true &&
            await library.ReadAsync(LastRomRecord, CartridgeLoader.MaxRomSizeBytes) is { } image)
        {
            await OpenRomAsync(image, last[4..], remember: false);
        }
        else
        {
            return false;
        }

        if (!restore)
        {
            return true;
        }

#pragma warning disable CA1031
        try
        {
            var romId = (await runner.PeekSaveAsync()).RomId;
            if (await resume.LoadAsync(romId, ResumeSlot) is not { } saved)
            {
                return true;
            }

            await runner.RestoreStateAsync(saved);
            if (AppSettings.ResumeRunning)
            {
                await runner.ResumeAsync(0);
            }

            Show("Resumed");
        }
        catch (Exception exception)
        {
            Show($"Resume failed, started from power-on: {exception.Message}");
        }
#pragma warning restore CA1031
        AppLog.Info("restored");
        return true;
    }

    private static string RomSizeMessage(long length) => $"A ROM is at most {CartridgeLoader.MaxRomSizeBytes / 1024 / 1024} MiB, not {length:N0} bytes.";

    private static string BootRomSizeMessage(long length) => $"A DMG boot ROM is {GameBoySystem.BootRomSize} bytes, not {length:N0}.";

    // Reads a file named by the launch extras after checking its size.
    private static Task<byte[]> ReadFileAsync(string path, int maxLength, Func<long, string> tooLong)
    {
        var length = new FileInfo(path).Length;
        if (length > maxLength)
        {
            throw new InvalidDataException(tooLong(length));
        }

        return File.ReadAllBytesAsync(path);
    }

    // Reads a picked document up to the limit, as its length is not always known in advance.
    private static async Task<byte[]> ReadPickedAsync(FileResult file, int maxLength, Func<long, string> tooLong)
    {
        await using var stream = await file.OpenReadAsync();
        if (stream.CanSeek && stream.Length > maxLength)
        {
            throw new InvalidDataException(tooLong(stream.Length));
        }

        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        for (int read; (read = await stream.ReadAsync(buffer)) > 0;)
        {
            if (memory.Length + read > maxLength)
            {
                throw new InvalidDataException(tooLong(memory.Length + read));
            }

            await memory.WriteAsync(buffer.AsMemory(0, read));
        }
        return memory.ToArray();
    }

    private async Task PickRomAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Game Boy ROM" });
        if (file is null)
        {
            return;
        }

        await OpenRomAsync(await ReadPickedAsync(file, CartridgeLoader.MaxRomSizeBytes, RomSizeMessage), file.FileName);
    }

    private async Task ChooseDemoAsync()
    {
        var labels = BuiltInDemos.All.Select(demo => demo.Name).ToArray();
        var choice = await DisplayActionSheetAsync("Demo", "Cancel", null, labels);
        var index = Array.IndexOf(labels, choice);
        if (index >= 0)
        {
            await LoadDemoAsync(index);
        }
    }

    // Sets a picked boot ROM for the next load or Reset, keeping a copy in the library.
    private async Task ChooseBootRomAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "DMG boot ROM (256 bytes)" });
        if (file is null)
        {
            return;
        }

        await UseBootRomAsync(await ReadPickedAsync(file, GameBoySystem.BootRomSize, BootRomSizeMessage), file.FileName);
    }

    private async Task UseBootRomAsync(byte[] image, string name)
    {
        if (image.Length != GameBoySystem.BootRomSize)
        {
            throw new InvalidDataException(BootRomSizeMessage(image.Length));
        }

        await runner.UseBootRomAsync(image);
        await library.WriteAsync(BootRomRecord, image);
        AppSettings.BootRom = name;
        ShowBootRom();
        Show("Boot ROM set");
    }

    private async Task RemoveBootRomAsync()
    {
        await runner.UseBootRomAsync(null);
        AppSettings.BootRom = null;
        await library.DeleteAsync(BootRomRecord);
        ShowBootRom();
        Show("Boot ROM removed");
    }

    private async Task UseSavedBootRomAsync()
    {
        if (AppSettings.BootRom is not { } name)
        {
            return;
        }

#pragma warning disable CA1031
        try
        {
            var image = await library.ReadAsync(BootRomRecord, GameBoySystem.BootRomSize)
                ?? throw new FileNotFoundException("The boot ROM copy of the app is missing.");
            await runner.UseBootRomAsync(image);
        }
        catch (Exception exception)
        {
            AppSettings.BootRom = null;
            ShowBootRom();
            Show($"Boot ROM {name} unusable: {exception.Message}");
        }
#pragma warning restore CA1031
    }

    private Task TogglePause() => runner.Status.Running ? runner.PauseAsync() : runner.ResumeAsync(input.Mask);

    private async Task ResetAsync()
    {
        ClearInput();
        await runner.ResetAsync();
        Show("Reset");
    }

    private async Task StateAsync()
    {
        var choice = await DisplayActionSheetAsync("State (slot 1)", "Cancel", null, "Save", "Load", "Rewind");
        switch (choice)
        {
            case "Save": await session.SaveStateAsync(1); Show("Saved to slot 1"); break;
            case "Load": ClearInput(); await session.LoadStateAsync(1); Show("Loaded slot 1"); break;
            case "Rewind":
                ClearInput();
                var back = await runner.RewindAsync();
                Show(back is { } cycles ? $"Rewound {cycles / (double)GameBoySystem.CyclesPerSecond:F1} s" : "Nothing to rewind");
                break;
        }
    }

    // Pauses for the background and saves the battery RAM and the state; the display timer rests.
    internal Task SuspendAsync()
    {
        if (suspended || closed)
        {
            return suspension;
        }

        suspended = true;
        timer?.Stop();
        ClearInput();
        audio.Update(false);
        rumble.Update(false);
        return suspension = SaveForBackgroundAsync(suspension);
    }

    // Saves the battery RAM and the resume state after any earlier background save has finished.
    private async Task SaveForBackgroundAsync(Task previous)
    {
        var chained = !previous.IsCompleted;
        await previous;
        var romId = string.Empty;
#pragma warning disable CA1031
        try
        {
            var captured = await runner.CaptureSaveAsync(); // Pauses.
            if (!chained)
            {
                wasRunning = captured.WasRunning;
            }

            romId = captured.RomId;
            await session.SaveForCloseAsync();
        }
        catch (Exception exception)
        {
            AppLog.Info($"suspend save failed: {exception.Message}");
        }
#pragma warning restore CA1031
#pragma warning disable CA1031
        try
        {
            if (romId.Length > 0 && runner.Status.RomLoaded)
            {
                await resume.SaveAsync(romId, ResumeSlot, await runner.CaptureStateAsync(), DateTimeOffset.UtcNow);
                AppSettings.ResumeRunning = wasRunning;
            }
        }
        catch (Exception exception)
        {
            AppLog.Info($"suspend state failed: {exception.Message}");
        }
#pragma warning restore CA1031
        AppLog.Info($"suspended running={wasRunning}");
    }

    // Resumes the runner if it ran, after the background save that pauses it has finished.
    internal async Task ResumeAsync()
    {
        if (!suspended || closed)
        {
            return;
        }

        suspended = false;
        timer?.Start();
        await suspension;
        if (suspended || closed)
        {
            return; // Left or closed meanwhile.
        }

        if (wasRunning)
        {
            await Run(() => runner.ResumeAsync(0));
        }

        AppLog.Info($"resumed running={wasRunning}");
    }

    internal async Task ShutdownAsync()
    {
        if (closed)
        {
            return;
        }

        closed = true;
        LaunchRequest.Arrived -= OnLaunchRequest;
        timer?.Stop();
        await suspension; // Never throws.
        Dispose();
        AppLog.Info("shutdown");
    }

    // Ends the emulation the page owns, stopping the audio before the runner.
    public void Dispose()
    {
        audio.Dispose();
        rumble.Dispose();
        runner.Dispose();
        session.Dispose();
        screen.Dispose();
        gate.Dispose();
    }
}
