namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Audio;
using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Cpu;
using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Memory;
using GonFox.GameBoy.Core.Video;

using Timer = GonFox.GameBoy.Core.Devices.Timer;

// Synchronous and single-owner: the host decides when and where to call it.
public sealed class GameBoySystem
{
    public const int TCyclesPerSecond = Clock.TCyclesPerSecond;
    private readonly Clock clock = new();
    private readonly MemoryBus bus = new();
    private readonly Sm83Cpu cpu;
    private readonly Interrupts interrupts = new();
    private readonly Timer timer;
    private readonly Apu apu;
    private readonly Ppu ppu;
    private readonly OamDma dma;
    private ICartridge? currentCartridge;
    public Joypad Joypad { get; }
    public Serial Serial { get; }
    public VideoOutput Video { get; } = new();
    public AudioOutput Audio { get; } = new();

    public GameBoySystem()
    {
        timer = new Timer(interrupts);
        apu = new Apu(timer, clock, Audio);
        timer.ConnectApu(apu);
        Joypad = new Joypad(interrupts);
        Serial = new Serial(interrupts);
        timer.ConnectSerial(Serial);
        bus.ConnectDevices(interrupts, timer, Serial, Joypad, apu);
        clock.ConnectTimer(timer);
        ppu = new Ppu(interrupts, Video);
        bus.ConnectPpu(ppu);
        clock.ConnectPpu(ppu);
        dma = new OamDma(bus, ppu);
        bus.ConnectDma(dma);
        clock.ConnectDma(dma);
        cpu = new Sm83Cpu(bus, clock, interrupts, Joypad);
        Reset();
    }

    public bool IsRomLoaded => currentCartridge is not null;
    public bool IsFaulted => cpu.IsFaulted;
    public CpuFault? Fault => cpu.Fault;
    public bool IsHalted => cpu.IsHalted;
    public bool IsStopped => cpu.IsStopped;
    public ulong TotalTCycles => clock.TotalTCycles;
    internal const string BootBypassProfile = "BootBypass-v2";
    internal const string BootRomProfile = "BootRom-v1";
    private byte[]? bootRom;

    public const int BootRomSize = MemoryBus.BootRomSize;

    // Starts from this DMG boot ROM at the next Reset or insertion instead of the post-boot state.
    public void UseBootRom(ReadOnlySpan<byte> image)
    {
        if (image.Length != BootRomSize)
        {
            throw new ArgumentException($"A DMG boot ROM is {BootRomSize} bytes, not {image.Length}.", nameof(image));
        }

        bootRom = image.ToArray();
    }

    public void UseBootBypass() => bootRom = null;
    public bool UsesBootRom => bootRom is not null;
    public bool IsBootRomMapped => bus.BootRomMapped;

    public void InsertCartridge(ICartridge cartridge)
    {
        ArgumentNullException.ThrowIfNull(cartridge);
        (currentCartridge as IClockedCartridge)?.DetachClock(); // Stops the removed cartridge's clock.
        currentCartridge = cartridge;
        bus.ConnectCartridge(cartridge);
        Reset();
    }

    public RunResult StepInstruction()
    {
        EnsureCartridge();
        var executed = cpu.StepInstruction();
        apu.Synchronize();
        return new RunResult(executed, cpu.IsStopped);
    }

    public RunResult RunForTCycles(int minimumTCycles)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumTCycles);
        EnsureCartridge();

        var executed = cpu.Run(minimumTCycles);
        apu.Synchronize();
        return new RunResult(executed, cpu.IsStopped);
    }

    public void Reset()
    {
        // A cartridge clock runs on through the Reset and then follows the restarted count.
        var clocked = currentCartridge as IClockedCartridge;
        clocked?.DetachClock();
        currentCartridge?.ResetController();
        bus.Reset();
        clock.Reset();
        clocked?.AttachClock(clock);
        interrupts.Reset();
        timer.Reset();
        Serial.Reset();
        Joypad.Reset();
        Audio.Clear();
        apu.Reset();
        Video.Reset();
        ppu.Reset();
        dma.Reset();
        cpu.Reset();
        if (bootRom is null)
        {
            return;
        }

        // Sets the power-on state and maps the boot ROM; RAMs stay zero instead of random.
        cpu.PowerOn();
        timer.PowerOn();
        Serial.PowerOn();
        interrupts.PowerOn();
        apu.PowerOn();
        ppu.PowerOn();
        bus.MapBootRom(bootRom);
    }

    private IStatefulCartridge StatefulCartridge() => currentCartridge as IStatefulCartridge ??
        throw new NotSupportedException("Machine states require a loaded built-in cartridge (ROM Only, MBC1, MBC2, MMM01, MBC3, MBC5, Game Boy Camera, HuC1 or HuC3).");

    public GameBoyState CaptureState()
    {
        var cartridge = StatefulCartridge();
        return new()
        {
            BootProfile = bootRom is null ? BootBypassProfile : BootRomProfile,
            RomSha256 = cartridge.RomSha256,
            CartridgeType = cartridge.TypeCode,
            TotalTCycles = TotalTCycles,
            Cpu = cpu.CaptureState(),
            Memory = bus.CaptureState(),
            Cartridge = cartridge.CaptureState(),
            Interrupts = interrupts.CaptureState(),
            Timer = timer.CaptureState(),
            Serial = Serial.CaptureState(),
            Joypad = Joypad.CaptureState(),
            Apu = apu.CaptureState(),
            Audio = Audio.CaptureState(),
            Dma = dma.CaptureState(),
            Ppu = ppu.CaptureState(),
            Video = Video.CaptureState()
        };
    }

    public void RestoreState(GameBoyState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var cartridge = StatefulCartridge();

        // Validates every component before changing any model state.
        StateValidation.Require(state.FormatVersion == GameBoyState.CurrentFormat && state.Model == "DMG" && state.BootProfile is BootBypassProfile or BootRomProfile &&
            state.RomSha256 == cartridge.RomSha256 && state.CartridgeType == cartridge.TypeCode && state.TotalTCycles % 4 == 0, "identity / clock");
        Sm83Cpu.ValidateState(state.Cpu);
        MemoryBus.ValidateState(state.Memory);
        cartridge.ValidateState(state.Cartridge);
        Interrupts.ValidateState(state.Interrupts);
        Timer.ValidateState(state.Timer);
        Serial.ValidateState(state.Serial);
        Joypad.ValidateState(state.Joypad);
        Apu.ValidateState(state.Apu, state.TotalTCycles);
        StateValidation.Require(AudioOutput.IsValid(state.Audio), "audio queue");
        OamDma.ValidateState(state.Dma);
        ppu.ValidateState(state.Ppu);
        Video.ValidateState(state.Video);
        StateValidation.Require(state.Ppu.DmaActive == state.Dma.Active &&
            ((state.Ppu.Lcdc & 0x80) != 0) == state.Video.LcdEnabled &&
            (!state.Apu.SkipStep || (state.Timer.Divider & 0x1000) != 0), "device connections");
        var clocked = currentCartridge as IClockedCartridge;
        clocked?.DetachClock(); // Replaced by the saved clock.
        cpu.RestoreState(state.Cpu);
        bus.RestoreState(state.Memory);
        cartridge.RestoreState(state.Cartridge);
        interrupts.RestoreState(state.Interrupts);
        timer.RestoreState(state.Timer);
        Serial.RestoreState(state.Serial);
        Joypad.RestoreState(state.Joypad);
        apu.RestoreState(state.Apu, state.TotalTCycles);
        Audio.RestoreState(state.Audio);
        dma.RestoreState(state.Dma);
        ppu.RestoreState(state.Ppu);
        clock.RestoreState(state.TotalTCycles);
        Video.RestoreState(state.Video);
        clocked?.AttachClock(clock);
    }

    public DebugSnapshot GetDebugSnapshot() => cpu.GetDebugSnapshot() with
    {
        DividerCounter = timer.DividerCounter,
        TimerCounter = timer.Tima,
        TimerModulo = timer.Tma,
        TimerControl = timer.Tac,
        InterruptFlags = interrupts.Flags,
        InterruptEnable = interrupts.Enable,
        Ly = ppu.Ly,
        PpuMode = ppu.Mode,
        LcdControl = ppu.ReadRegister(0xFF40),
        ScrollX = ppu.ReadRegister(0xFF43),
        ScrollY = ppu.ReadRegister(0xFF42),
        PpuDot = ppu.Dot,
        DmaActive = dma.Active,
        RomBank0 = currentCartridge is IBankedCartridge bank0 ? bank0.LowerRomBank : 0,
        RomBank1 = currentCartridge is IBankedCartridge bank1 ? bank1.UpperRomBank : 1,
        RamBank = currentCartridge is IBankedCartridge ramBank ? ramBank.RamBank : 0,
        RamEnabled = currentCartridge is IBankedCartridge ramEnable && ramEnable.RamEnabled,
        BankingMode = currentCartridge is IBankedCartridge mode ? mode.BankingMode : (byte)0
    };

    public void CopyMemory(ushort startAddress, Span<byte> destination)
    {
        if (destination.Length > 0x10000 - startAddress)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "The requested range extends beyond 0xFFFF.");
        }

        for (var index = 0; index < destination.Length; index++)
        {
            destination[index] = bus.PeekByte((ushort)(startAddress + index));
        }
    }

    private void EnsureCartridge()
    {
        if (currentCartridge is null)
        {
            throw new InvalidOperationException("Insert a cartridge before executing the system.");
        }
    }
}
