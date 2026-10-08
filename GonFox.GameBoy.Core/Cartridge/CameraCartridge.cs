namespace GonFox.GameBoy.Core.Cartridge;

using System.Diagnostics;

// The camera's registers, stopped flag, T-cycles left of the capture and developed picture.
internal sealed record CameraState(byte[] Registers, bool Paused, int Remaining, byte[] Picture);

// The Game Boy Camera (type FC), whose captures catch up with the console clock on each access.
internal sealed class CameraCartridge : IStatefulCartridge, IBankedCartridge, IBatteryBackedCartridge,
    IClockedCartridge, ICameraCartridge
{
    private const int Width = ICameraCartridge.ImageWidth;
    private const int Height = ICameraCartridge.ImageHeight;
    private const int Pixels = Width * Height;
    private const byte DefaultLuminance = 128;
    internal const int RegisterCount = 0x36;
    internal const int PictureBytes = Pixels / 4;
    internal const int PictureOffset = 0x100;
    internal const int MaxCaptureTCycles = 4 * (32446 + 512 + (16 * 0xFFFF));
    private static ReadOnlySpan<byte> EdgeRatioTimes4 => [2, 3, 4, 5, 8, 12, 16, 20];
    private readonly byte[] rom;
    private readonly byte[] ram;
    private readonly int ramMask;
    private byte romBank = 1;
    private byte select;
    private bool paused;
    private readonly byte[] registers = new byte[RegisterCount];
    private readonly byte[] picture = new byte[PictureBytes];
    private int remaining; // T-cycles left of the capture.
    private readonly byte[] sensor = new byte[Pixels];
    private readonly int[] signal = new int[Pixels];
    private readonly int[] filtered = new int[Pixels];
    private Clock? attachedClock;
    private ulong countedTo; // Last clock count applied.
    public string RomSha256 { get; }
    public byte TypeCode => 0xFC;
    public int LowerRomBank => 0;
    public int UpperRomBank => romBank & field;
    public int RamBank => (select & 0x10) != 0 ? select : select & ramMask; // 10-1F: the registers.
    public bool RamEnabled { get; private set; }
    public byte BankingMode => 0;
    private bool Capturing => (registers[0] & 1) != 0;

    internal CameraCartridge(ReadOnlySpan<byte> image, int ramSize)
    {
        rom = image.ToArray();
        ram = new byte[ramSize];
        UpperRomBank = (image.Length / 0x4000) - 1;
        ramMask = (ramSize / 0x2000) - 1;
        Array.Fill(sensor, DefaultLuminance);
        RomSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(rom));
    }

    public void SetImage(ReadOnlySpan<byte> luminance)
    {
        if (luminance.Length != Pixels)
        {
            throw new ArgumentException($"Expected {Pixels} luminance bytes ({Width} x {Height}), got {luminance.Length}.", nameof(luminance));
        }

        luminance.CopyTo(sensor);
    }

    public void ClearImage() => Array.Fill(sensor, DefaultLuminance);

    public void AttachClock(Clock clock)
    {
        Update();
        attachedClock = clock;
        countedTo = clock.TotalTCycles;
    }

    public void DetachClock()
    {
        Update();
        attachedClock = null;
    }

    public CartridgeState CaptureState()
    {
        Update();
        return new(romBank, 0, 0, RamEnabled, ram.ToArray(), select,
            Camera: new(registers.ToArray(), paused, remaining, picture.ToArray()));
    }

    public void ValidateState(CartridgeState? state) => StateValidation.Require(state is not null && state.Bank <= 0x3F &&
        state.Upper == 0 && state.Mode == 0 && state.RamBank <= 0x1F && StateValidation.HasLength(state.Ram, ram.Length) &&
        state.HasNoClock() && IsValid(state.Camera), "Game Boy Camera");

    // Checks that A000 keeps three bits and that only a capture in progress or stopped has time left.
    private static bool IsValid(CameraState? camera)
    {
        if (camera is not { Registers.Length: RegisterCount, Picture.Length: PictureBytes } || (camera.Registers[0] & ~7) != 0)
        {
            return false;
        }

        var capturing = (camera.Registers[0] & 1) != 0;
        if (capturing && camera.Paused)
        {
            return false;
        }

        return capturing || camera.Paused ? camera.Remaining is > 0 and <= MaxCaptureTCycles : camera.Remaining == 0;
    }

    // Restores the state, normally while the clock is detached.
    public void RestoreState(CartridgeState state)
    {
        var camera = state.Camera!;
        romBank = state.Bank;
        select = state.RamBank;
        RamEnabled = state.RamEnabled;
        state.Ram.CopyTo(ram, 0);
        camera.Registers.CopyTo(registers, 0);
        camera.Picture.CopyTo(picture, 0);
        paused = camera.Paused;
        remaining = camera.Remaining;
        if (attachedClock is not null)
        {
            countedTo = attachedClock.TotalTCycles;
        }
    }

    public byte[] ExportRam()
    {
        Update(); // Writes an ended capture's picture.
        return ram.ToArray();
    }

    public void ImportRam(ReadOnlySpan<byte> data)
    {
        if (data.Length != ram.Length)
        {
            throw new ArgumentException($"Expected {ram.Length} RAM bytes, got {data.Length}.", nameof(data));
        }

        data.CopyTo(ram);
    }

    public byte Read(ushort address)
    {
        if (address <= 0x3FFF)
        {
            return rom[address];
        }

        if (address <= 0x7FFF)
        {
            return rom[(UpperRomBank * 0x4000) + (address & 0x3FFF)];
        }

        if (address is >= 0xA000 and <= 0xBFFF)
        {
            Update();
            if ((select & 0x10) != 0)
            {
                return (address & 0x7F) == 0 ? registers[0] : (byte)0; // A001-A07F: write-only.
            }

            return Capturing ? (byte)0 : ram[RamOffset(address)];
        }
        throw new ArgumentOutOfRangeException(nameof(address), "Address is outside the cartridge windows.");
    }

    public void Write(ushort address, byte value)
    {
        switch (address)
        {
            case <= 0x1FFF: RamEnabled = (value & 15) == 10; break;
            case <= 0x3FFF: romBank = (byte)(value & 0x3F); break;
            case <= 0x5FFF: select = (byte)(value & 0x1F); break;
            case <= 0x7FFF: break; // No register here.
            case >= 0xA000 and <= 0xBFFF:
                Update();
                if ((select & 0x10) != 0)
                {
                    WriteRegister(address & 0x7F, value);
                }
                else if (RamEnabled && !Capturing)
                {
                    ram[RamOffset(address)] = value;
                }

                break;
            default: throw new ArgumentOutOfRangeException(nameof(address), "Address is outside the cartridge windows.");
        }
    }

    private int RamOffset(ushort address) => ((select & ramMask) * 0x2000) + address - 0xA000;

    // Writes a camera register, starting or stopping a capture through A000; A036-A07F hold none.
    private void WriteRegister(int index, byte value)
    {
        if (index >= RegisterCount)
        {
            return;
        }

        if (index != 0)
        {
            registers[index] = value;
            return;
        }
        var capturing = Capturing;
        registers[0] = (byte)(value & 7); // Develop reads the new bits 1-2.
        if ((value & 1) == 0)
        {
            paused |= capturing;
        }
        else if (!capturing)
        {
            if (!paused)
            {
                Develop();
                remaining = CaptureTCycles();
            }
            paused = false;
        }
    }

    // The length of a capture in T-cycles.
    private int CaptureTCycles() => 4 * (32446 + ((registers[1] & 0x80) != 0 ? 0 : 512) + (16 * ((registers[2] << 8) | registers[3])));

    // Resets the banks and registers, dropping any capture; the RAM keeps its contents.
    public void ResetController()
    {
        romBank = 1;
        select = 0;
        RamEnabled = false;
        paused = false;
        remaining = 0;
        Array.Clear(registers);
    }

    private void Update()
    {
        if (attachedClock is null)
        {
            return;
        }

        var now = attachedClock.TotalTCycles;
        Debug.Assert(now >= countedTo, "The console clock jumped back without detaching the cartridge clock.");
        if (Capturing)
        {
            var elapsed = now - countedTo;
            if (elapsed < (ulong)remaining)
            {
                remaining -= (int)elapsed;
            }
            else
            {
                registers[0] &= 6;
                remaining = 0;
                picture.CopyTo(ram, PictureOffset);
            }
        }
        countedTo = now;
    }

    // Develops the sensor image into 2bpp tiles: exposure, edge filter, then the dither matrix.
    private void Develop()
    {
        byte trigger = registers[0], edgeMode = registers[1], edgeInvert = registers[4];
        var exposure = (registers[2] << 8) | registers[3];
        for (var i = 0; i < Pixels; i++)
        {
            var value = Math.Clamp(128 + (((sensor[i] * exposure / 0x300) - 128) / 8), 0, 255);
            if ((edgeInvert & 0x08) != 0)
            {
                value = 255 - value;
            }

            signal[i] = value - 128;
        }

        int alpha4 = EdgeRatioTimes4[(edgeInvert >> 4) & 7];
        var result = filtered;
        switch (((edgeMode & 0x80) >> 4) | ((edgeMode & 0x60) >> 4) | (edgeInvert >> 7))
        {
            case 0x0: Filter1D(signal, filtered, trigger); break;
            case 0x1: Array.Clear(filtered); break;
            case 0x2 or 0x3: // Clamps to 0-255 on purpose.
                Edge(signal, filtered, true, false, (edgeInvert & 0x80) != 0, alpha4, 0, 255);
                Filter1D(filtered, signal, trigger); result = signal; break;
            case 0xC or 0xD: Edge(signal, filtered, false, true, (edgeInvert & 0x80) != 0, alpha4, -128, 127); break;
            case 0xE or 0xF: Edge(signal, filtered, true, true, (edgeInvert & 0x80) != 0, alpha4, -128, 127); break;
            default: result = signal; break;
        }

        Array.Clear(picture);
        for (int y = 0, i = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++, i++)
            {
                int value = result[i] + 128, element = 6 + ((((y & 3) * 4) + (x & 3)) * 3);
                var shade = value < registers[element] ? 3 : value < registers[element + 1] ? 2 : value < registers[element + 2] ? 1 : 0;
                int offset = ((y >> 3) * 0x100) + ((x >> 3) * 16) + ((y & 7) * 2), bit = 0x80 >> (x & 7);
                if ((shade & 1) != 0)
                {
                    picture[offset] |= (byte)bit;
                }

                if ((shade & 2) != 0)
                {
                    picture[offset + 1] |= (byte)bit;
                }
            }
        }
    }

    // Applies the 1-D filter chosen by A000 bits 1-2 to each pixel and the one below it.
    private static void Filter1D(int[] source, int[] destination, byte trigger)
    {
        var select = (trigger >> 1) & 3;
        for (var i = 0; i < Pixels; i++)
        {
            int pixel = source[i], below = source[i < Pixels - Width ? i + Width : i];
            destination[i] = Math.Clamp(select switch { 0 => -pixel, 1 => pixel, _ => pixel - below }, -128, 127);
        }
    }

    // Enhances or extracts edges against the horizontal and vertical neighbours, scaled by alpha.
    private static void Edge(int[] source, int[] destination, bool horizontal, bool vertical, bool extract, int alpha4, int min, int max)
    {
        for (int y = 0, i = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++, i++)
            {
                int pixel = source[i], difference = 0;
                if (horizontal)
                {
                    difference += (2 * pixel) - source[x > 0 ? i - 1 : i] - source[x < Width - 1 ? i + 1 : i];
                }

                if (vertical)
                {
                    difference += (2 * pixel) - source[y > 0 ? i - Width : i] - source[y < Height - 1 ? i + Width : i];
                }

                destination[i] = Math.Clamp(((extract ? 0 : 4 * pixel) + (difference * alpha4)) / 4, min, max);
            }
        }
    }
}
