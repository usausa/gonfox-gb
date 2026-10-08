namespace GonFox.GameBoy.Core.Memory;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Video;

using Timer = GonFox.GameBoy.Core.Devices.Timer;

internal sealed class MemoryBus
{
    private readonly byte[] workRam = new byte[0x2000];
    private readonly byte[] highRam = new byte[0x7F];

    // BootRom: the image while it is mapped over the cartridge, otherwise empty.
    internal sealed record State(byte[] WorkRam, byte[] HighRam, byte[] BootRom);

    internal State CaptureState() => new(workRam.ToArray(), highRam.ToArray(), bootRom?.ToArray() ?? []);

    internal static void ValidateState(State? state)
    {
        StateValidation.Require(state is not null, "Bus");
        StateValidation.Length(state.WorkRam, 0x2000, "WRAM");
        StateValidation.Length(state.HighRam, 0x7F, "HRAM");
        StateValidation.Require(state.BootRom is { Length: 0 or BootRomSize }, "boot ROM");
    }

    internal void RestoreState(State state)
    {
        state.WorkRam.CopyTo(workRam, 0);
        state.HighRam.CopyTo(highRam, 0);
        if (state.BootRom.Length == BootRomSize)
        {
            MapBootRom(state.BootRom.ToArray());
        }
        else
        {
            UnmapBootRom();
        }
    }

    internal const int BootRomSize = 0x100;

    // The connected cartridge, or the boot ROM overlaid on it while mapped.
    private ICartridge? cartridge;
    private ICartridge? connected;
    private byte[]? bootRom;
    private Interrupts? interrupts;
    private Timer? timer;
    private Serial? serial;
    private Joypad? joypad;
    private Apu? apu;
    private Ppu? ppu;
    private OamDma? dma;
    internal void ConnectPpu(Ppu target) => ppu = target;
    internal void ConnectDma(OamDma target) => dma = target;

    internal void ConnectDevices(Interrupts interruptsDevice, Timer timerDevice, Serial serialDevice, Joypad joypadDevice, Apu apuDevice)
    {
        interrupts = interruptsDevice;
        timer = timerDevice;
        serial = serialDevice;
        joypad = joypadDevice;
        apu = apuDevice;
    }

    internal void ConnectCartridge(ICartridge inserted)
    {
        ArgumentNullException.ThrowIfNull(inserted);
        cartridge = connected = inserted;
        bootRom = null;
    }

    // Maps the boot ROM over reads of 0000-00FF until FF50 bit 0 is set.
    internal void MapBootRom(byte[] image)
    {
        bootRom = image;
        cartridge = connected is null ? null : new BootRomOverlay(image, connected);
    }

    internal bool BootRomMapped => bootRom is not null;

    private void UnmapBootRom()
    {
        bootRom = null;
        cartridge = connected;
    }

    private sealed class BootRomOverlay(byte[] image, ICartridge cartridge) : ICartridge
    {
        public byte Read(ushort address) => address < BootRomSize ? image[address] : cartridge.Read(address);
        public void Write(ushort address, byte value) => cartridge.Write(address, value);
        public void ResetController() => cartridge.ResetController();
    }

    // Reads a byte as the CPU sees it while OAM DMA occupies OAM and the bus of its source.
    internal byte ReadByte(ushort address)
    {
        if (dma?.Active == true && address < 0xFF00)
        {
            if (address >= 0xFE00)
            {
                return 0xFF;
            }

            if (dma.SharesBus(address))
            {
                return dma.ConflictingRead();
            }
        }
        return ReadMapped(address);
    }

    // Pauses OAM DMA while the CPU is halted.
    internal void HoldDma(bool held)
    {
        dma?.Held = held;
    }

    // Puts the IDU's value on the address bus, which corrupts OAM in FE00-FEFF.
    internal void IduAddress(ushort value)
    {
        if ((value & 0xFF00) == 0xFE00)
        {
            ppu?.CorruptOamByWrite();
        }
    }

    // ReSharper disable PatternIsRedundant
    // Reads a mapped address; the arms spell out every range as the memory map does.
    private byte ReadMapped(ushort address) => address switch
    {
        <= 0x7FFF or (>= 0xA000 and <= 0xBFFF) => cartridge?.Read(address) ?? 0xFF,
        (>= 0x8000 and <= 0x9FFF) or (>= 0xFE00 and <= 0xFE9F) => ppu?.ReadMemory(address) ?? 0xFF,
        >= 0xC000 and <= 0xDFFF => workRam[address - 0xC000],
        >= 0xE000 and <= 0xFDFF => workRam[address - 0xE000],
        >= 0xFF80 and <= 0xFFFE => highRam[address - 0xFF80],
        >= 0xFEA0 and <= 0xFEFF => ppu?.ReadUnusable() ?? 0xFF,
        0xFF00 => joypad?.ReadRegister() ?? 0xFF,
        0xFF01 => serial?.Data ?? 0xFF,
        0xFF02 => serial?.Control ?? 0xFF,
        >= 0xFF04 and <= 0xFF07 => timer?.ReadRegister(address) ?? 0xFF,
        0xFF0F => interrupts?.Flags ?? 0xFF,
        0xFFFF => interrupts?.Enable ?? 0xFF,
        0xFF46 => dma?.Register ?? 0xFF,
        0xFF50 => bootRom is null ? (byte)0xFF : (byte)0xFE, // Bit 0: unmapped.
        >= 0xFF10 and <= 0xFF3F => apu?.ReadRegister(address) ?? 0xFF,
        (>= 0xFF40 and <= 0xFF45) or (>= 0xFF47 and <= 0xFF4B) => ppu?.ReadRegister(address) ?? 0xFF,
        _ => 0xFF
    };

    internal void WriteByte(ushort address, byte value)
    {
        if (dma?.Active == true && address < 0xFF00 && (address >= 0xFE00 || dma.SharesBus(address)))
        {
            if (address < 0xFE00)
            {
                dma.ConflictingWrite(value);
            }

            return;
        }
        switch (address)
        {
            case <= 0x7FFF or (>= 0xA000 and <= 0xBFFF):
                cartridge?.Write(address, value);
                break;
            case >= 0xC000 and <= 0xDFFF:
                workRam[address - 0xC000] = value;
                break;
            case (>= 0x8000 and <= 0x9FFF) or (>= 0xFE00 and <= 0xFE9F):
                ppu?.WriteMemory(address, value);
                break;
            case >= 0xE000 and <= 0xFDFF:
                workRam[address - 0xE000] = value;
                break;
            case >= 0xFF80 and <= 0xFFFE:
                highRam[address - 0xFF80] = value;
                break;
            case >= 0xFEA0 and <= 0xFEFF: ppu?.WriteUnusable(); break;
            case 0xFF00: joypad?.WriteRegister(value); break;
            case 0xFF01: serial?.WriteData(value); break;
            case 0xFF02: serial?.WriteControl(value); break;
            case >= 0xFF04 and <= 0xFF07: timer?.WriteRegister(address, value); break;
            case 0xFF0F: interrupts?.WriteFlags(value); break;
            case 0xFFFF: interrupts?.WriteEnable(value); break;
            case 0xFF46: dma?.Start(value); break;
            case 0xFF50:
                if ((value & 1) != 0)
                {
                    UnmapBootRom();
                }
                break; // One way until the next power-on.
            case >= 0xFF10 and <= 0xFF3F: apu?.WriteRegister(address, value); break;
            case (>= 0xFF40 and <= 0xFF45) or (>= 0xFF47 and <= 0xFF4B): ppu?.WriteRegister(address, value); break;
        }
    }
    // ReSharper restore PatternIsRedundant

    internal byte PeekByte(ushort address)
    {
        // Debug reads bypass CPU/DMA/PPU/APU restrictions without ticking the model.
        if (address is (>= 0x8000 and <= 0x9FFF) or (>= 0xFE00 and <= 0xFE9F))
        {
            return ppu?.PeekMemory(address) ?? 0xFF;
        }

        if (address is >= 0xFF30 and <= 0xFF3F)
        {
            return apu?.PeekWaveRam(address) ?? 0xFF;
        }

        return ReadMapped(address);
    }

    internal byte ReadDmaByte(ushort address)
    {
        // Source pages E0-FF mirror WRAM C0-DF, FE and FF included.
        if (address >= 0xE000)
        {
            address -= 0x2000;
        }

        return PeekByte(address);
    }

    internal void Reset()
    {
        Array.Fill(workRam, DmgBootProfile.RamFill);
        Array.Fill(highRam, DmgBootProfile.RamFill);
        UnmapBootRom();
    }
}
