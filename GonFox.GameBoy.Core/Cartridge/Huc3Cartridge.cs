namespace GonFox.GameBoy.Core.Cartridge;

using System.Diagnostics;

// HuC3 (type FE): ROM and RAM banking, a clock MCU reached through a mailbox, and an infrared port.
internal sealed class Huc3Cartridge : IStatefulCartridge, IBankedCartridge, IHuc3ClockCartridge, IClockedCartridge
{
    private readonly byte[] rom;
    private readonly byte[] ram;
    private readonly Huc3Mcu mcu = new();
    private byte romBank = 1;
    private byte ramBank;

    public string RomSha256 { get; }
    public byte TypeCode => 0xFE;
    public int LowerRomBank => 0;
    public int UpperRomBank => romBank & field;
    public int RamBank => ramBank & field;
    public bool RamEnabled => BankingMode is 0 or 0x0A; // Read-only in mode 0.
    public byte BankingMode { get; private set; } // What A000-BFFF shows.
    public long ClockChanges => mcu.Changes;
    internal bool InfraredLed { get; private set; }

    internal Huc3Cartridge(ReadOnlySpan<byte> image, int ramSize)
    {
        rom = image.ToArray();
        ram = new byte[ramSize];
        UpperRomBank = (image.Length / 0x4000) - 1;
        RamBank = ramSize == 0 ? 0 : (ramSize / 0x2000) - 1;
        RomSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(rom));
    }

    public CartridgeState CaptureState() =>
        new(romBank, InfraredLed ? (byte)1 : (byte)0, BankingMode, false, ram.ToArray(), ramBank, Huc3: mcu.CaptureState());

    public void ValidateState(CartridgeState? state) => StateValidation.Require(state is not null && state.Bank <= 0x7F &&
        state.Upper <= 1 && state.Mode <= 15 && !state.RamEnabled && state.RamBank <= 3 && StateValidation.HasLength(state.Ram, ram.Length) &&
        state.Rtc is null && state.Camera is null && Huc3Mcu.IsValid(state.Huc3), "HuC3");

    public void RestoreState(CartridgeState state)
    {
        romBank = state.Bank;
        InfraredLed = state.Upper != 0;
        BankingMode = state.Mode;
        ramBank = state.RamBank;
        state.Ram.CopyTo(ram, 0);
        mcu.RestoreState(state.Huc3!);
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
            return BankingMode switch
            {
                0 or 0x0A => ram.Length != 0 ? ram[RamOffset(address)] : (byte)0xFF,
                0x0C => mcu.Response,
                0x0D => 0x81, // Ready, never busy.
                0x0E => 0xC0, // No light.
                _ => 0xFF // Write-only or unmapped.
            };
        }

        throw new ArgumentOutOfRangeException(nameof(address), "Address is outside the cartridge windows.");
    }

    public void Write(ushort address, byte value)
    {
        switch (address)
        {
            case <= 0x1FFF: BankingMode = (byte)(value & 15); break;
            case <= 0x3FFF: romBank = (byte)(value & 0x7F); break;
            case <= 0x5FFF: ramBank = (byte)(value & 3); break;
            case <= 0x7FFF: break; // No known register here.
            case >= 0xA000 and <= 0xBFFF:
                switch (BankingMode)
                {
                    case 0x0A:
                        if (ram.Length != 0)
                        {
                            ram[RamOffset(address)] = value;
                        }
                        break;
                    case 0x0B: mcu.WriteMailbox(value); break;
                    case 0x0D:
                        if ((value & 1) == 0)
                        {
                            mcu.Execute();
                        }
                        break;
                    case 0x0E: InfraredLed = (value & 1) != 0; break;
                }
                break;
            default: throw new ArgumentOutOfRangeException(nameof(address), "Address is outside the cartridge windows.");
        }
    }

    private int RamOffset(ushort address) => (RamBank * 0x2000) + address - 0xA000;

    // Resets the mapper registers; the battery-powered MCU keeps its state.
    public void ResetController()
    {
        BankingMode = 0;
        romBank = 1;
        ramBank = 0;
        InfraredLed = false;
    }

    public byte[] ExportRam() => ram.ToArray();

    public void ImportRam(ReadOnlySpan<byte> data)
    {
        if (data.Length != ram.Length)
        {
            throw new ArgumentException($"Expected {ram.Length} RAM bytes, got {data.Length}.", nameof(data));
        }

        data.CopyTo(ram);
    }

    public Huc3ClockSnapshot ExportClock() => mcu.Export();
    public void ImportClock(Huc3ClockSnapshot clock) => mcu.Import(clock);
    public void AdvanceClock(long seconds) => mcu.Advance(seconds);
    public void AttachClock(Clock clock) => mcu.AttachClock(clock);
    public void DetachClock() => mcu.DetachClock();
}

// The clock MCU's nibbles, T-cycles into the minute, address and mailbox; equal by content.
internal sealed record Huc3State(byte[] Memory, int SubMinute, byte Address, byte Command, byte Argument, byte Result)
{
    public bool Equals(Huc3State? other) => other is not null && Memory.AsSpan().SequenceEqual(other.Memory) &&
        (SubMinute, Address, Command, Argument, Result) == (other.SubMinute, other.Address, other.Command, other.Argument, other.Result);

    public override int GetHashCode() => HashCode.Combine(SubMinute, Address, Command, Argument, Result);
}

// The HuC3 clock MCU: a 256-nibble memory with a minute and day clock, driven by mailbox commands.
internal sealed class Huc3Mcu
{
    internal const int MemorySize = 256;
    internal const int CyclesPerMinute = 60 * Clock.CyclesPerSecond;
    private const int MinutesPerDay = 1440;
    private const int Days = 0x1000;

    // Times kept as a 3-nibble minute and a 3-nibble day: the program's copy, the clock and the event.
    private const int Copy = 0x00;
    private const int Counter = 0x10;
    private const int Event = 0x58;
    private readonly byte[] memory = new byte[MemorySize];
    private byte address;
    private byte command;
    private byte argument;
    private byte result;
    private int subMinute; // T-cycles into the minute.
    private Clock? attachedClock;
    private ulong countedTo; // Last clock count applied.
    internal long Changes { get; private set; }

    internal byte Response => (byte)(0x80 | (command << 4) | result);

    internal void WriteMailbox(byte value)
    {
        command = (byte)((value >> 4) & 7);
        argument = (byte)(value & 15);
    }

    internal void Execute()
    {
        Update();
        switch (command)
        {
            case 1: result = memory[address++]; break;
            case 2: Store(argument); break;
            case 3: Store(argument); address++; break;
            case 4: address = (byte)((address & 0xF0) | argument); break;
            case 5: address = (byte)((address & 0x0F) | (argument << 4)); break;
            case 6 when argument == 0: memory.AsSpan(Counter, 6).CopyTo(memory.AsSpan(Copy)); break;
            case 6 when argument == 1: SetClock(); break;
            case 6 when argument == 2: result = 1; break;
        }
    }

    private void Store(byte value)
    {
        if (memory[address] == value)
        {
            return;
        }

        memory[address] = value;
        Changes++;
    }

    private void SetClock()
    {
        if (memory.AsSpan(Copy, 6).SequenceEqual(memory.AsSpan(Counter, 6)))
        {
            return;
        }

        var moved = Minutes(Copy) - Minutes(Counter);
        memory.AsSpan(Copy, 6).CopyTo(memory.AsSpan(Counter));
        if (moved != 0)
        {
            const long range = (long)Days * MinutesPerDay;
            var time = (((Minutes(Event) + moved) % range) + range) % range;
            Write(Event, time % MinutesPerDay);
            Write(Event + 3, time / MinutesPerDay);
        }
        Changes++;
    }

    internal void AttachClock(Clock clock)
    {
        Update();
        attachedClock = clock;
        countedTo = clock.TotalTCycles;
    }

    internal void DetachClock()
    {
        Update();
        attachedClock = null;
    }

    internal Huc3ClockSnapshot Export()
    {
        Update();
        return new(memory.ToArray(), subMinute / Clock.CyclesPerSecond);
    }

    internal void Import(Huc3ClockSnapshot clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (!StateValidation.HasLength(clock.Memory, MemorySize))
        {
            throw new ArgumentException($"Expected {MemorySize} nibbles of MCU memory.", nameof(clock));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(clock.Seconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(clock.Seconds, 59);
        Update();
        for (var i = 0; i < MemorySize; i++)
        {
            memory[i] = (byte)(clock.Memory[i] & 15);
        }

        subMinute = clock.Seconds * Clock.CyclesPerSecond;
    }

    internal void Advance(long seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        Update();
        long minutes = seconds / 60, part = subMinute + ((seconds % 60) * Clock.CyclesPerSecond);
        if (part >= CyclesPerMinute)
        {
            part -= CyclesPerMinute;
            minutes++;
        }
        subMinute = (int)part;
        AdvanceMinutes(minutes);
    }

    internal Huc3State CaptureState()
    {
        Update();
        return new(memory.ToArray(), subMinute, address, command, argument, result);
    }

    internal static bool IsValid(Huc3State? state) => state is not null && StateValidation.HasLength(state.Memory, MemorySize) &&
        state.Memory.AsSpan().IndexOfAnyExceptInRange((byte)0, (byte)15) < 0 && state.SubMinute is >= 0 and < CyclesPerMinute &&
        state.Command <= 7 && state.Argument <= 15 && state.Result <= 15;

    // Restores the state while the clock is detached.
    internal void RestoreState(Huc3State state)
    {
        Debug.Assert(attachedClock is null, "Detach the cartridge clock before restoring a state.");
        state.Memory.CopyTo(memory, 0);
        (subMinute, address, command, argument, result) = (state.SubMinute, state.Address, state.Command, state.Argument, state.Result);
        Changes++;
    }

    private void Update()
    {
        if (attachedClock is null)
        {
            return;
        }

        var now = attachedClock.TotalTCycles;
        Debug.Assert(now >= countedTo, "The console clock jumped back without detaching the cartridge clock.");
        var total = (ulong)subMinute + (now - countedTo);
        if (total >= CyclesPerMinute)
        {
            AdvanceMinutes((long)(total / CyclesPerMinute));
        }

        subMinute = (int)(total % CyclesPerMinute);
        countedTo = now;
    }

    // Adds minutes to the clock; a minute counter past 1439 wraps at $FFF to 0 without a day.
    private void AdvanceMinutes(long count)
    {
        var minute = Read(Counter);
        if (minute >= MinutesPerDay)
        {
            if (count < 0x1000 - minute)
            {
                Write(Counter, minute + count);
                return;
            }
            count -= 0x1000 - minute;
            minute = 0;
        }
        var total = minute + count;
        Write(Counter, total % MinutesPerDay);
        Write(Counter + 3, (Read(Counter + 3) + (total / MinutesPerDay)) % Days);
    }

    // A time in minutes: the minute plus 1440 per day.
    private long Minutes(int at) => (Read(at + 3) * (long)MinutesPerDay) + Read(at);

    private int Read(int at) => memory[at] | (memory[at + 1] << 4) | (memory[at + 2] << 8);

    private void Write(int at, long value)
    {
        for (var i = 0; i < 3; i++)
        {
            memory[at + i] = (byte)((value >> (4 * i)) & 15);
        }
    }
}
