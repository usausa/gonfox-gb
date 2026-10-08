namespace GonFox.GameBoy.Core.Cpu;

using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Memory;

internal sealed partial class Sm83Cpu(MemoryBus bus, Clock clock, Interrupts? interrupts = null, Joypad? joypad = null)
{
    private byte a;
    private byte f;
    private byte b;
    private byte c;
    private byte d;
    private byte e;
    private byte h;
    private byte l;
    private ushort sp;
    private ushort pc;
    private bool ime;
    private bool imeEnablePending;
    private bool haltBug;
    private bool justHalted;

    internal bool IsFaulted => Fault is not null;
    internal CpuFault? Fault { get; private set; }
    internal bool IsHalted { get; private set; }
    internal bool IsStopped { get; private set; }
    private ushort HL => (ushort)((h << 8) | l);

    internal sealed record State(ushort AF, ushort BC, ushort DE, ushort HL, ushort SP, ushort PC,
        bool Ime, bool EiPending, bool Halted, bool Stopped, bool HaltBug, ushort? FaultAddress, byte FaultOpcode,
        bool JustHalted);

    internal State CaptureState() => new((ushort)((a << 8) | f), GetRegisterPair(0), GetRegisterPair(1), HL,
        sp, pc, ime, imeEnablePending, IsHalted, IsStopped, haltBug,
        Fault?.Address, Fault?.Opcode ?? 0, justHalted);

    internal static void ValidateState(State? state) => StateValidation.Require(state is not null &&
        (state.AF & 15) == 0 && !(state.Halted && state.Stopped) && (state.Halted || !state.JustHalted) &&
        (state.FaultAddress is null ? state.FaultOpcode == 0 : state.FaultOpcode is
            0xD3 or 0xDB or 0xDD or 0xE3 or 0xE4 or 0xEB or 0xEC or 0xED or 0xF4 or 0xFC or 0xFD), "CPU");

    internal void RestoreState(State state)
    {
        a = (byte)(state.AF >> 8);
        f = (byte)state.AF;
        SetRegisterPair(0, state.BC);
        SetRegisterPair(1, state.DE);
        SetRegisterPair(2, state.HL);
        sp = state.SP;
        pc = state.PC;
        ime = state.Ime;
        imeEnablePending = state.EiPending;
        IsHalted = state.Halted;
        IsStopped = state.Stopped;
        haltBug = state.HaltBug;
        justHalted = state.JustHalted;
        Fault = state.FaultAddress is { } address ? new CpuFault(address, state.FaultOpcode) : null;
        bus.HoldDma(IsHalted && !justHalted); // As WaitHalted leaves it.
    }

    internal void Reset()
    {
        a = DmgBootProfile.AF >> 8;
        f = DmgBootProfile.AF & 0xF0;
        SetRegisterPair(0, DmgBootProfile.BC);
        SetRegisterPair(1, DmgBootProfile.DE);
        SetRegisterPair(2, DmgBootProfile.HL);
        sp = DmgBootProfile.SP;
        pc = DmgBootProfile.PC;
        Fault = null;
        ime = imeEnablePending = IsHalted = IsStopped = false;
        haltBug = justHalted = false;
    }

    // Clears every register so that a boot ROM starts at 0000.
    internal void PowerOn()
    {
        a = f = 0;
        SetRegisterPair(0, 0);
        SetRegisterPair(1, 0);
        SetRegisterPair(2, 0);
        sp = pc = 0;
    }

    internal DebugSnapshot GetDebugSnapshot() => new(
        (ushort)((a << 8) | f), (ushort)((b << 8) | c),
        (ushort)((d << 8) | e), HL, sp, pc, clock.TotalTCycles)
    {
        InterruptMasterEnable = ime,
        InterruptEnablePending = imeEnablePending,
        IsHalted = IsHalted,
        IsStopped = IsStopped
    };

    // Steps until at least the requested T-cycles have passed or STOP waits for input.
    internal long Run(long minimumTCycles)
    {
        long executed = 0;
        while (executed < minimumTCycles)
        {
            if (Fault is not null)
            {
                executed += WaitLocked();
                continue;
            }
            if (IsHalted)
            {
                // Skips quiet M-cycles at once, except the first one after HALT or while a request is pending.
                if (!justHalted && (interrupts?.Pending ?? 0) == 0)
                {
                    var skip = Math.Min(clock.QuietMachineCycles(), (minimumTCycles - executed + 3) >> 2) - 1;
                    if (skip > 0)
                    {
                        clock.SkipMachineCycles((int)skip);
                        executed += skip << 2;
                    }
                }
                executed += WaitHalted();
                continue;
            }
            executed += StepInstruction();
            if (IsStopped)
            {
                break;
            }
        }
        return executed;
    }

    internal int StepInstruction()
    {
        if (Fault is not null)
        {
            return WaitLocked();
        }

        if (IsStopped)
        {
            if (joypad?.ConsumeStopWake() != true)
            {
                return 0;
            }

            IsStopped = false;
        }
        if (IsHalted)
        {
            return WaitHalted();
        }

        if (ime && (interrupts?.Pending ?? 0) != 0)
        {
            return ServiceInterrupt();
        }

        var start = clock.TotalTCycles;
        var enableImeAfterInstruction = imeEnablePending;
        var address = pc;
        var opcode = ReadNextByte();

        switch (opcode)
        {
            case 0x00: // NOP
                break;
            case 0x10: Stop(); break;
            case 0x76:
                if ((interrupts?.Pending ?? 0) == 0)
                {
                    IsHalted = justHalted = true;
                }
                else if (ime || enableImeAfterInstruction)
                {
                    pc = unchecked((ushort)(pc - 1)); // Returns to HALT.
                }
                else
                {
                    haltBug = true;
                }

                break;
            case 0x01 or 0x11 or 0x21 or 0x31: // LD rr,d16
                SetRegisterPair((opcode >> 4) & 3, ReadNextWord());
                break;
            case 0x02 or 0x12: // LD (BC/DE),A
                WriteCycle(GetRegisterPair((opcode >> 4) & 1), a);
                break;
            case 0x0A or 0x1A: // LD A,(BC/DE)
                a = ReadCycle(GetRegisterPair((opcode >> 4) & 1));
                break;
            case 0x22 or 0x32: // LD (HL+/-),A
                WriteCycle(HL, a);
                SetRegisterPair(2, unchecked((ushort)(HL + (opcode == 0x22 ? 1 : -1))));
                break;
            case 0x2A or 0x3A: // LD A,(HL+/-)
                a = ReadCycle(HL);
                SetRegisterPair(2, unchecked((ushort)(HL + (opcode == 0x2A ? 1 : -1))));
                break;
            case 0x03 or 0x13 or 0x23 or 0x33: // INC rr
                ChangeRegisterPair((opcode >> 4) & 3, 1);
                break;
            case 0x0B or 0x1B or 0x2B or 0x3B: // DEC rr
                ChangeRegisterPair((opcode >> 4) & 3, -1);
                break;
            case 0x09 or 0x19 or 0x29 or 0x39: // ADD HL,rr
                AddToHl(GetRegisterPair((opcode >> 4) & 3));
                IdleCycle();
                break;
            case 0x04 or 0x0C or 0x14 or 0x1C or 0x24 or 0x2C or 0x34 or 0x3C:
                WriteRegister((opcode >> 3) & 7, Increment(ReadRegister((opcode >> 3) & 7)));
                break;
            case 0x05 or 0x0D or 0x15 or 0x1D or 0x25 or 0x2D or 0x35 or 0x3D:
                WriteRegister((opcode >> 3) & 7, Decrement(ReadRegister((opcode >> 3) & 7)));
                break;
            case 0x06 or 0x0E or 0x16 or 0x1E or 0x26 or 0x2E or 0x36 or 0x3E:
                WriteRegister((opcode >> 3) & 7, ReadNextByte()); // LD r,d8
                break;
            case 0x07 or 0x0F or 0x17 or 0x1F: // RLCA, RRCA, RLA, RRA
                a = RotateShift((opcode >> 3) & 3, a);
                f &= Carry; // Always clears Z.
                break;
            case 0x08: // LD (a16),SP
                var destination = ReadNextWord();
                WriteCycle(destination, (byte)sp);
                WriteCycle(unchecked((ushort)(destination + 1)), (byte)(sp >> 8));
                break;
            case 0x18: // JR e8
                JumpRelative(true);
                break;
            case 0x20 or 0x28 or 0x30 or 0x38:
                JumpRelative(Condition((opcode >> 3) & 3));
                break;
            case 0x27: DecimalAdjust(); break;
            case 0x2F: a = (byte)~a; f |= Subtract | HalfCarry; break; // CPL
            case 0x37: f = (byte)((f & Zero) | Carry); break; // SCF
            case 0x3F: f = (byte)((f & Zero) | ((f ^ Carry) & Carry)); break; // CCF
            case >= 0x40 and <= 0x7F: // LD r,r except HALT
                WriteRegister((opcode >> 3) & 7, ReadRegister(opcode & 7));
                break;
            case >= 0x80 and <= 0xBF: // ALU A,r
                Arithmetic((opcode >> 3) & 7, ReadRegister(opcode & 7));
                break;
            case 0xC6 or 0xCE or 0xD6 or 0xDE or 0xE6 or 0xEE or 0xF6 or 0xFE:
                Arithmetic((opcode >> 3) & 7, ReadNextByte());
                break;
            case 0xC0 or 0xC8 or 0xD0 or 0xD8: // RET cc
                IdleCycle();
                if (Condition((opcode >> 3) & 3))
                {
                    Return();
                }

                break;
            case 0xC9: Return(); break;
            case 0xD9: // RETI
                Return();
                ime = true;
                imeEnablePending = false;
                break;
            case 0xC1 or 0xD1 or 0xE1 or 0xF1: // POP rr/AF
                var popped = Pop();
                if (opcode == 0xF1)
                {
                    a = (byte)(popped >> 8);
                    f = (byte)(popped & 0xF0);
                }
                else
                {
                    SetRegisterPair((opcode >> 4) & 3, popped);
                }

                break;
            case 0xC5 or 0xD5 or 0xE5 or 0xF5: // PUSH rr/AF
                IduCycle(sp);
                Push(opcode == 0xF5 ? (ushort)((a << 8) | f) : GetRegisterPair((opcode >> 4) & 3));
                break;
            case 0xC2 or 0xCA or 0xD2 or 0xDA: // JP cc,a16
                JumpAbsolute(Condition((opcode >> 3) & 3));
                break;
            case 0xC3: JumpAbsolute(true); break;
            case 0xE9: pc = HL; break; // JP HL
            case 0xC4 or 0xCC or 0xD4 or 0xDC: // CALL cc,a16
                Call(Condition((opcode >> 3) & 3));
                break;
            case 0xCD: Call(true); break;
            case 0xC7 or 0xCF or 0xD7 or 0xDF or 0xE7 or 0xEF or 0xF7 or 0xFF:
                IduCycle(sp);
                Push(pc);
                pc = (ushort)(opcode & 0x38); // RST
                break;
            case 0xCB: ExecuteCb(ReadNextByte()); break;
            case 0xE0: WriteCycle((ushort)(0xFF00 | ReadNextByte()), a); break;
            case 0xF0: a = ReadCycle((ushort)(0xFF00 | ReadNextByte())); break;
            case 0xE2: WriteCycle((ushort)(0xFF00 | c), a); break;
            case 0xF2: a = ReadCycle((ushort)(0xFF00 | c)); break;
            case 0xE8: // ADD SP,e8
                sp = AddSignedToSp(ReadNextByte());
                IdleCycle();
                IdleCycle();
                break;
            case 0xF8: // LD HL,SP+e8
                SetRegisterPair(2, AddSignedToSp(ReadNextByte()));
                IdleCycle();
                break;
            case 0xF9: sp = HL; IduCycle(HL); break; // LD SP,HL
            case 0xF3: ime = imeEnablePending = false; break; // DI
            case 0xFB: // EI
                if (!ime)
                {
                    imeEnablePending = true;
                }

                break;
            case 0xEA: // LD (a16),A
                WriteCycle(ReadNextWord(), a);
                break;
            case 0xFA: // LD A,(a16)
                a = ReadCycle(ReadNextWord());
                break;
            case 0xD3 or 0xDB or 0xDD or 0xE3 or 0xE4 or 0xEB or 0xEC or 0xED or 0xF4 or 0xFC or 0xFD:
                Fault = new CpuFault(address, opcode);
                break;
        }

        if (enableImeAfterInstruction && imeEnablePending)
        {
            ime = true;
            imeEnablePending = false;
        }

        return (int)(clock.TotalTCycles - start);
    }

    // Lets one M-cycle pass while the CPU is locked up and takes no interrupt.
    private int WaitLocked()
    {
        clock.AdvanceTCycles(4); // Not AdvanceMachineCycle: slower here.
        return 4;
    }

    // Waits one halted M-cycle, sampling IF two T-cycles in (at the start of the first one after HALT).
    private int WaitHalted()
    {
        if (justHalted)
        {
            justHalted = false;
            if ((interrupts?.Pending ?? 0) != 0)
            {
                IsHalted = false;
            }

            clock.AdvanceTCycles(4); // Not AdvanceMachineCycle: slower here.
            if (IsHalted)
            {
                bus.HoldDma(true);
            }

            return 4;
        }
        clock.AdvanceTCycles(2);
        if ((interrupts?.Pending ?? 0) != 0)
        {
            IsHalted = false;
        }

        clock.AdvanceTCycles(2);
        if (!IsHalted)
        {
            bus.HoldDma(false);
        }

        return 4;
    }

    // Enters STOP mode and resets DIV, or acts as NOP or HALT while a selected joypad line is low.
    private void Stop()
    {
        var pending = (interrupts?.Pending ?? 0) != 0;
        if (joypad?.AnyLineLow == true)
        {
            if (pending)
            {
                return;
            }

            ReadNextByte();
            IsHalted = justHalted = true;
            return;
        }
        if (!pending)
        {
            pc = unchecked((ushort)(pc + 1));
        }

        clock.ResetDivider();
        joypad?.ClearStopWake();
        IsStopped = true;
    }

    private byte ReadCycle(ushort address)
    {
        var value = bus.ReadByte(address);
        clock.AdvanceMachineCycle();
        return value;
    }

    private void WriteCycle(ushort address, byte value)
    {
        if (address == 0xFF0F)
        {
            // IF takes the write one T-cycle into the M-cycle, after what the PPU raises there.
            clock.AdvanceTCycles(1);
            bus.WriteByte(address, value);
            clock.AdvanceTCycles(3);
            return;
        }
        bus.WriteByte(address, value);
        clock.AdvanceMachineCycle();
    }

    private void IdleCycle() => clock.AdvanceMachineCycle();

    // An internal M-cycle with the IDU's value on the address bus, which may corrupt OAM.
    private void IduCycle(ushort value)
    {
        bus.IduAddress(value);
        clock.AdvanceMachineCycle();
    }

    private byte ReadNextByte()
    {
        var value = ReadCycle(pc);
        if (haltBug)
        {
            haltBug = false;
        }
        else
        {
            pc = unchecked((ushort)(pc + 1));
        }

        return value;
    }

    // Pushes PC and jumps to the vector chosen from IE and IF while the bytes are pushed.
    private int ServiceInterrupt()
    {
        ime = imeEnablePending = haltBug = false;
        IdleCycle();
        IduCycle(sp);
        IdleCycle();
        sp = unchecked((ushort)(sp - 1));
        WriteCycle(sp, (byte)(pc >> 8));
        var enabled = interrupts!.Enable;
        sp = unchecked((ushort)(sp - 1));
        var requested = interrupts.Flags; // Ignores a push into IF.
        bus.WriteByte(sp, (byte)pc);
        int pending = enabled & requested & 0x1F, bit = 0;
        while (bit < 5 && (pending & (1 << bit)) == 0)
        {
            bit++;
        }

        clock.AdvanceTCycles(2);
        if (bit < 5)
        {
            interrupts.Acknowledge(bit);
        }

        pc = bit < 5 ? (ushort)(0x40 + (bit * 8)) : (ushort)0; // Cancelled: PC=0000.
        clock.AdvanceTCycles(2);
        return 20;
    }

    private ushort ReadNextWord()
    {
        var low = ReadNextByte();
        var high = ReadNextByte();
        return (ushort)((high << 8) | low);
    }

    private byte ReadRegister(int index) => index switch
    {
        0 => b,
        1 => c,
        2 => d,
        3 => e,
        4 => h,
        5 => l,
        6 => ReadCycle(HL),
        7 => a,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    private void WriteRegister(int index, byte value)
    {
        switch (index)
        {
            case 0: b = value; break;
            case 1: c = value; break;
            case 2: d = value; break;
            case 3: e = value; break;
            case 4: h = value; break;
            case 5: l = value; break;
            case 6: WriteCycle(HL, value); break;
            case 7: a = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    private void SetRegisterPair(int index, ushort value)
    {
        var high = (byte)(value >> 8);
        var low = (byte)value;
        switch (index)
        {
            case 0: b = high; c = low; break;
            case 1: d = high; e = low; break;
            case 2: h = high; l = low; break;
            case 3: sp = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    private ushort GetRegisterPair(int index) => index switch
    {
        0 => (ushort)((b << 8) | c),
        1 => (ushort)((d << 8) | e),
        2 => HL,
        3 => sp,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    private void ChangeRegisterPair(int index, int change)
    {
        var value = GetRegisterPair(index);
        IduCycle(value);
        SetRegisterPair(index, unchecked((ushort)(value + change)));
    }

    private bool Condition(int index) => index switch
    {
        0 => (f & Zero) == 0,
        1 => (f & Zero) != 0,
        2 => (f & Carry) == 0,
        3 => (f & Carry) != 0,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    private void JumpRelative(bool taken)
    {
        var offset = unchecked((sbyte)ReadNextByte());
        if (!taken)
        {
            return;
        }

        IdleCycle();
        pc = unchecked((ushort)(pc + offset));
    }

    private void JumpAbsolute(bool taken)
    {
        var target = ReadNextWord();
        if (!taken)
        {
            return;
        }

        IdleCycle();
        pc = target;
    }

    private void Call(bool taken)
    {
        var target = ReadNextWord();
        if (!taken)
        {
            return;
        }

        IduCycle(sp);
        Push(pc);
        pc = target;
    }

    private void Return()
    {
        pc = Pop();
        IdleCycle();
    }

    private void Push(ushort value)
    {
        sp = unchecked((ushort)(sp - 1));
        WriteCycle(sp, (byte)(value >> 8));
        sp = unchecked((ushort)(sp - 1));
        WriteCycle(sp, (byte)value);
    }

    private ushort Pop()
    {
        var low = ReadCycle(sp);
        sp = unchecked((ushort)(sp + 1));
        var high = ReadCycle(sp);
        sp = unchecked((ushort)(sp + 1));
        return (ushort)((high << 8) | low);
    }
}
