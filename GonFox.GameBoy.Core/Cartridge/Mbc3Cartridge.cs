namespace GonFox.GameBoy.Core.Cartridge;

// MBC3 and MBC30: ROM and RAM banking, with the clock registers selected in place of a RAM bank.
internal class Mbc3Cartridge : ICartridge, IStatefulCartridge, IBankedCartridge
{
    private readonly byte[] rom;
    protected byte[] Ram { get; }
    private protected Mbc3Rtc? Rtc { get; }
    private readonly int ramMask;
    private readonly byte romSelectMask;
    private byte romBank;
    private byte select;

    public string RomSha256 { get; }
    public byte TypeCode => Rtc is not null ? (Ram.Length == 0 ? (byte)0x0F : (byte)0x10)
        : this is IBatteryBackedCartridge ? (byte)0x13 : Ram.Length == 0 ? (byte)0x11 : (byte)0x12;
    public int LowerRomBank => 0;
    public int UpperRomBank => (romBank == 0 ? 1 : romBank) & field;
    public int RamBank => (select & 8) != 0 ? select : select & ramMask; // 08-0F: a clock register.
    public bool RamEnabled { get; private set; }
    public byte BankingMode => 0;

    internal Mbc3Cartridge(ReadOnlySpan<byte> image, int ramSize, bool mbc30)
        : this(image, ramSize, mbc30, null)
    {
    }

    private protected Mbc3Cartridge(ReadOnlySpan<byte> image, int ramSize, bool mbc30, Mbc3Rtc? rtc)
    {
        rom = image.ToArray();
        Ram = new byte[ramSize];
        Rtc = rtc;
        UpperRomBank = (image.Length / 0x4000) - 1;
        ramMask = ramSize == 0 ? 0 : (ramSize / 0x2000) - 1;
        romSelectMask = mbc30 ? (byte)0xFF : (byte)0x7F;
        RomSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(rom));
    }

    public CartridgeState CaptureState() => new(romBank, 0, 0, RamEnabled, Ram.ToArray(), select, Rtc?.CaptureState());

    public void ValidateState(CartridgeState? state) => StateValidation.Require(state is not null &&
        state.Bank <= romSelectMask && state.Upper == 0 && state.Mode == 0 && state.RamBank <= 15 &&
        StateValidation.HasLength(state.Ram, Ram.Length) && state.Camera is null && state.Huc3 is null &&
        (Rtc is null ? state.Rtc is null : Mbc3Rtc.IsValid(state.Rtc)), "MBC3");

    public void RestoreState(CartridgeState state)
    {
        romBank = state.Bank;
        select = state.RamBank;
        RamEnabled = state.RamEnabled;
        state.Ram.CopyTo(Ram, 0);
        Rtc?.RestoreState(state.Rtc!);
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
            if (!RamEnabled)
            {
                return 0xFF;
            }

            if ((select & 8) != 0)
            {
                return Rtc is not null && select <= 12 ? Rtc.Read(select - 8) : (byte)0xFF;
            }

            return Ram.Length != 0 ? Ram[RamOffset(address)] : (byte)0xFF;
        }
        throw new ArgumentOutOfRangeException(nameof(address), "Address is outside the cartridge windows.");
    }

    public void Write(ushort address, byte value)
    {
        switch (address)
        {
            case <= 0x1FFF: RamEnabled = (value & 15) == 10; break;
            case <= 0x3FFF: romBank = (byte)(value & romSelectMask); break;
            case <= 0x5FFF: select = (byte)(value & 15); break;
            case <= 0x7FFF: Rtc?.WriteLatch(value); break;
            case >= 0xA000 and <= 0xBFFF:
                if (!RamEnabled)
                {
                    break;
                }

                if ((select & 8) != 0)
                {
                    if (Rtc is not null && select <= 12)
                    {
                        Rtc.Write(select - 8, value);
                    }
                }
                else if (Ram.Length != 0)
                {
                    Ram[RamOffset(address)] = value;
                }

                break;
            default: throw new ArgumentOutOfRangeException(nameof(address), "Address is outside the cartridge windows.");
        }
    }

    private int RamOffset(ushort address) => ((select & ramMask) * 0x2000) + address - 0xA000;

    // Resets the mapper registers; the battery-powered clock keeps running and keeps its latch.
    public void ResetController()
    {
        romBank = 0;
        select = 0;
        RamEnabled = false;
    }
}

// MBC3 with a battery (type 13) that keeps the RAM.
internal sealed class BatteryMbc3Cartridge(ReadOnlySpan<byte> image, int ramSize, bool mbc30)
    : Mbc3Cartridge(image, ramSize, mbc30), IBatteryBackedCartridge
{
    public byte[] ExportRam() => Ram.ToArray();

    public void ImportRam(ReadOnlySpan<byte> data)
    {
        if (data.Length != Ram.Length)
        {
            throw new ArgumentException($"Expected {Ram.Length} RAM bytes, got {data.Length}.", nameof(data));
        }

        data.CopyTo(Ram);
    }
}

// MBC3 with a clock (types 0F and 10), whose battery keeps the RAM and the clock.
internal sealed class ClockMbc3Cartridge(ReadOnlySpan<byte> image, int ramSize, bool mbc30)
    : Mbc3Cartridge(image, ramSize, mbc30, new Mbc3Rtc()), IRealTimeClockCartridge, IClockedCartridge
{
    public byte[] ExportRam() => Ram.ToArray();

    public void ImportRam(ReadOnlySpan<byte> data)
    {
        if (data.Length != Ram.Length)
        {
            throw new ArgumentException($"Expected {Ram.Length} RAM bytes, got {data.Length}.", nameof(data));
        }

        data.CopyTo(Ram);
    }

    public RtcSnapshot ExportClock() => Rtc!.Export();
    public void ImportClock(RtcSnapshot clock) => Rtc!.Import(clock);
    public void AdvanceClock(long seconds) => Rtc!.Advance(seconds);
    public void AttachClock(Clock clock) => Rtc!.AttachClock(clock);
    public void DetachClock() => Rtc!.DetachClock();
}
