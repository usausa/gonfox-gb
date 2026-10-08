# GonFox.GameBoy

[![NuGet](https://img.shields.io/nuget/v/GonFox.GameBoy.Core.svg?label=GonFox.GameBoy.Core)](https://www.nuget.org/packages/GonFox.GameBoy.Core)
[![NuGet](https://img.shields.io/nuget/v/GonFox.GameBoy.Platform.Shared.svg?label=GonFox.GameBoy.Platform.Shared)](https://www.nuget.org/packages/GonFox.GameBoy.Platform.Shared)
[![NuGet](https://img.shields.io/nuget/v/GonFox.GameBoy.Platform.Windows.svg?label=GonFox.GameBoy.Platform.Windows)](https://www.nuget.org/packages/GonFox.GameBoy.Platform.Windows)
[![NuGet](https://img.shields.io/nuget/v/GonFox.GameBoy.Platform.Android.svg?label=GonFox.GameBoy.Platform.Android)](https://www.nuget.org/packages/GonFox.GameBoy.Platform.Android)

A Game Boy (DMG) emulator for .NET 10: an environment-neutral emulator core, shared building blocks for hosts, and the platform parts for Windows and Android, with a WPF example and a .NET MAUI Android example.

- SM83 CPU with every instruction (undefined opcodes lock the CPU as on hardware), timer, interrupts, HALT and STOP
- PPU with the DMG pixel FIFO (variable mode 3, writes during drawing, window quirks), OAM DMA bus conflicts and the OAM corruption bug
- APU with four channels, mixed to 48 kHz stereo PCM
- Cartridges: ROM only, MBC1 and MBC1M, MBC2, MMM01, MBC3 with the real-time clock, MBC5 with rumble, Game Boy Camera, HuC1, HuC3; battery RAM and clocks
- Save states as bytes, state slots and rewind on the host side; an optional DMG boot ROM run from power-on (none is included)
- Verified with Mooneye, gbmicrotest, dmg-acid2, mealybug-tearoom-tests, rtc3test, AGE, SameSuite and firstwhite, and with homebrew test ROMs checked against SameBoy

CGB (Game Boy Color) and SGB (Super Game Boy) are out of scope.

## Packages

| Package | Target | Contents |
| --- | --- | --- |
| GonFox.GameBoy.Core | net10.0 | The emulator. No threads, real clocks, files or devices; the caller owns the system and serializes calls. |
| GonFox.GameBoy.Platform.Shared | net10.0 | Building blocks for any host: the run thread (`EmulationRunner`), sessions with battery saves and state slots (`EmulationSession`), the frame exchange, the audio buffer and playback control, input merging. No wording: states, save results and the audio device come as values for the host to word, and exception messages are in English. |
| GonFox.GameBoy.Platform.Windows | net10.0-windows10.0.19041.0 | WASAPI audio output, settings and storage locations for Windows hosts. |
| GonFox.GameBoy.Platform.Android | net10.0-android | AudioTrack audio output, storage, button haptics and rumble for Android hosts. |

## Usage

### Core

```csharp
using GonFox.GameBoy.Core;
using GonFox.GameBoy.Core.Audio;
using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Video;

var loaded = CartridgeLoader.Load(File.ReadAllBytes("game.gb"));
var system = new GameBoySystem();
system.InsertCartridge(loaded.Cartridge);

var pixels = new byte[VideoOutput.BufferSize];                             // 160 x 144, BGRA32
var pcm = new short[AudioOutput.CapacityFrames * AudioOutput.ChannelCount]; // 48 kHz stereo, L/R interleaved

system.Joypad.SetButtonState(JoypadButton.Start, true);
system.RunForTCycles(GameBoySystem.CyclesPerSecond / 60);
var frame = system.Video.CopyLatestFrame(pixels);
var frames = system.Audio.ReadFrames(pcm);

var bytes = system.CaptureState().Serialize();
system.RestoreState(GameBoyState.Deserialize(bytes));
```

### Platform

```csharp
using GonFox.GameBoy.Platform;
using GonFox.GameBoy.Platform.Audio;
using GonFox.GameBoy.Platform.Windows;

using var audio = new AudioPlayback(() => new WasapiAudioDevice());
using var runner = new EmulationRunner(audio.Buffer);
using var session = new EmulationSession(runner, new BatterySaveStore(new FileRecordStore(WindowsStorage.SavesDirectory)));

await session.LoadAsync(await File.ReadAllBytesAsync("game.gb"), "game.gb"); // Loads the battery save and runs.

// On the UI thread, for each display frame:
audio.Update(runner.Status.Running);
if (runner.Frames.TryTake(shown) is { } slot)
{
    shown = slot.Info.Sequence;
    Draw(slot.Pixels);
}

await runner.SetButtonsAsync(mask);  // Bit n is JoypadButton n.
await session.SaveForCloseAsync();   // Before the host closes.
```

## Examples

| Project | Platform | |
| --- | --- | --- |
| Example.GameBoy.WpfHost | Windows (WPF) | A handheld-style window with SkiaSharp or WriteableBitmap drawing, WASAPI audio, saves, state slots, rewind and a debugging panel. |
| Example.GameBoy.MauiHost | Android 8.0+ (.NET MAUI) | A portrait handheld with multi-touch buttons, AudioTrack audio, haptics, rumble, saves and resume after the app is stopped. |

```powershell
dotnet run --project Example.GameBoy.WpfHost -c Release
```

Both examples include the demo ROMs below. See [docs/Examples.md](docs/Examples.md) (Japanese) for how to use them and how to build and install the Android example.

## Build and test

The solution needs the .NET 10 SDK and, for the Android projects, the `maui-android` workload.

```powershell
dotnet build GonFox.GameBoy.slnx -c Release
dotnet run --project GonFox.GameBoy.Core.Tests -c Release --no-build
dotnet run --project GonFox.GameBoy.Platform.Tests -c Release --no-build
dotnet run --project Example.GameBoy.WpfHost.Tests -c Release --no-build
```

Every test ROM is in the repository and the tests never use the network. For the unit tests alone, add `-- --filter-trait "Category=Unit"`. `GonFox.GameBoy.Ci.slnf` builds every project except the MAUI host, whose Release build compiles the app ahead of time and takes minutes; CI builds and inspects through it. The `Benchmark` project measures the core with BenchmarkDotNet ([docs/Benchmark.md](docs/Benchmark.md)).

## ROMs

`Roms` holds homebrew ROMs made for this project, with their sources, under the MIT license:

| ROM | |
| --- | --- |
| [Mega demo](Roms/MegaDemo/README.md) | Three effects and a four-channel song controlled by the buttons |
| [RPG demo (USAGI QUEST)](Roms/RpgDemo/README.md) | A title, hero and difficulty selection, command battles and a key test, with music |
| [Background demo](Roms/BackgroundDemo/README.md) | Scrolling, also as an MBC1 version that keeps its position in battery RAM |
| [Sound check](Roms/SoundCheck/README.md) | Left, right and both channels in turn |
| [Save counter](Roms/SaveCounter/README.md) | Counts starts in battery RAM |
| [Test suite](Roms/TestSuite/README.md) | Test ROMs for CPU instructions and timing, the HALT and OAM bugs, interrupts, OAM DMA, PPU timing and the APU, checked against SameBoy |

`Roms/External` holds public test ROMs (Mooneye, gbmicrotest, mealybug-tearoom-tests, dmg-acid2, rtc3test, AGE, SameSuite, firstwhite) and SameBoy's DMG boot ROM, each with its license and a manifest of pinned hashes.

## Documentation (Japanese)

- [Specification](docs/Specification.md): blocks, APIs, timing, video, audio, cartridges, state and persistence
- [Compatibility](docs/Compatibility.md): test ROMs, success criteria, results and known limitations
- [CPU instructions](docs/CpuInstructions.md): instructions, flags, cycles and interrupts
- [Examples](docs/Examples.md): the WPF and Android examples
- [Benchmark](docs/Benchmark.md): how the core is measured

## License

[MIT](LICENSE). The public test ROMs in `Roms/External` keep their own licenses (MIT, Zlib, Unlicense), which are next to them. No Nintendo boot ROM is included.
