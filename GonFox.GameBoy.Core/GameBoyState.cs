namespace GonFox.GameBoy.Core;

using System.Diagnostics.CodeAnalysis;

using GonFox.GameBoy.Core.Audio;
using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Cpu;
using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Memory;
using GonFox.GameBoy.Core.Video;

using Timer = GonFox.GameBoy.Core.Devices.Timer;

// An opaque, owned in-memory snapshot without public setters, arrays or device references.
public sealed record GameBoyState
{
    internal GameBoyState()
    {
    }

    // The format of the byte form, raised whenever the state's members change.
    internal const int CurrentFormat = 12;
    public int FormatVersion { get; internal init; } = CurrentFormat;
    public string Model { get; internal init; } = "DMG";

    // How the run began: the boot bypass or a boot ROM from power-on.
    public string BootProfile { get; internal init; } = GameBoySystem.BootBypassProfile;
    public string RomSha256 { get; internal init; } = string.Empty;
    public byte CartridgeType { get; internal init; }
    public ulong TotalTCycles { get; internal init; }
    internal Sm83Cpu.State Cpu { get; init; } = null!;
    internal MemoryBus.State Memory { get; init; } = null!;
    internal CartridgeState Cartridge { get; init; } = null!;
    internal Interrupts.State Interrupts { get; init; } = null!;
    internal Timer.State Timer { get; init; } = null!;
    internal Serial.State Serial { get; init; } = null!;
    internal Joypad.State Joypad { get; init; } = null!;
    internal Apu.State Apu { get; init; } = null!;
    internal AudioOutput.State Audio { get; init; } = null!;
    internal OamDma.State Dma { get; init; } = null!;
    internal Ppu.State Ppu { get; init; } = null!;
    internal VideoOutput.State Video { get; init; } = null!;

    // Converts to and from a byte form for the host to keep, in this build's format only.
    public byte[] Serialize() => StateSerializer.Serialize(this);
    public static GameBoyState Deserialize(ReadOnlySpan<byte> data) => StateSerializer.Deserialize(data);
}

// Validators that take nullable values, as a deserialized state may lack any block or array.
internal static class StateValidation
{
    // The caller validates its parameter "state".
    internal static void Require([DoesNotReturnIf(false)] bool valid, string component, string parameter = "state")
    {
        if (!valid)
        {
            throw new ArgumentException($"Invalid or incompatible state: {component}.", parameter);
        }
    }

    internal static void Length<T>([NotNull] T[]? data, int length, string component) => Require(data?.Length == length, component);

    internal static bool HasLength<T>(T[]? data, int length) => data?.Length == length;
}
