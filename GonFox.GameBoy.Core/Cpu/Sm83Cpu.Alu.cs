namespace GonFox.GameBoy.Core.Cpu;

internal sealed partial class Sm83Cpu
{
    private const byte Zero = 0x80;
    private const byte Subtract = 0x40;
    private const byte HalfCarry = 0x20;
    private const byte Carry = 0x10;

    private void Arithmetic(int operation, byte value)
    {
        var carryIn = (f & Carry) != 0 ? 1 : 0;
        switch (operation)
        {
            case 0: Add(value, 0); break;
            case 1: Add(value, carryIn); break;
            case 2: Sub(value, 0, store: true); break;
            case 3: Sub(value, carryIn, store: true); break;
            case 4: a &= value; f = (byte)((a == 0 ? Zero : 0) | HalfCarry); break;
            case 5: a ^= value; f = a == 0 ? Zero : (byte)0; break;
            case 6: a |= value; f = a == 0 ? Zero : (byte)0; break;
            case 7: Sub(value, 0, store: false); break;
        }
    }

    private void Add(byte value, int carryIn)
    {
        var sum = a + value + carryIn;
        var halfCarry = (a & 0x0F) + (value & 0x0F) + carryIn > 0x0F;
        a = unchecked((byte)sum);
        f = (byte)((a == 0 ? Zero : 0) | (halfCarry ? HalfCarry : 0) | (sum > 0xFF ? Carry : 0));
    }

    private void Sub(byte value, int carryIn, bool store)
    {
        var difference = a - value - carryIn;
        var halfBorrow = (a & 0x0F) < (value & 0x0F) + carryIn;
        var result = unchecked((byte)difference);
        f = (byte)((result == 0 ? Zero : 0) | Subtract | (halfBorrow ? HalfCarry : 0) |
            (difference < 0 ? Carry : 0));
        if (store)
        {
            a = result;
        }
    }

    private byte Increment(byte value)
    {
        var result = unchecked((byte)(value + 1));
        f = (byte)((f & Carry) | (result == 0 ? Zero : 0) | ((value & 0x0F) == 0x0F ? HalfCarry : 0));
        return result;
    }

    private byte Decrement(byte value)
    {
        var result = unchecked((byte)(value - 1));
        f = (byte)((f & Carry) | Subtract | (result == 0 ? Zero : 0) | ((value & 0x0F) == 0 ? HalfCarry : 0));
        return result;
    }

    private void AddToHl(ushort value)
    {
        var sum = HL + value;
        f = (byte)((f & Zero) | (((HL & 0x0FFF) + (value & 0x0FFF) > 0x0FFF) ? HalfCarry : 0) |
            (sum > 0xFFFF ? Carry : 0));
        SetRegisterPair(2, unchecked((ushort)sum));
    }

    private ushort AddSignedToSp(byte offset)
    {
        // Flags use the unsigned low byte; the resulting address uses a signed offset.
        f = (byte)(((sp & 0x0F) + (offset & 0x0F) > 0x0F ? HalfCarry : 0) |
            ((sp & 0xFF) + offset > 0xFF ? Carry : 0));
        return unchecked((ushort)(sp + (sbyte)offset));
    }

    private void DecimalAdjust()
    {
        var correction = 0;
        var carry = (f & Carry) != 0;
        if ((f & Subtract) == 0)
        {
            if (carry || a > 0x99)
            {
                correction |= 0x60;
                carry = true;
            }
            if ((f & HalfCarry) != 0 || (a & 0x0F) > 9)
            {
                correction |= 0x06;
            }

            a = unchecked((byte)(a + correction));
        }
        else
        {
            if (carry)
            {
                correction |= 0x60;
            }

            if ((f & HalfCarry) != 0)
            {
                correction |= 0x06;
            }

            a = unchecked((byte)(a - correction));
        }
        f = (byte)((f & Subtract) | (a == 0 ? Zero : 0) | (carry ? Carry : 0));
    }

    private void ExecuteCb(byte opcode)
    {
        var register = opcode & 7;
        var bit = (opcode >> 3) & 7;
        var value = ReadRegister(register);
        switch (opcode >> 6)
        {
            case 0: WriteRegister(register, RotateShift(bit, value)); break;
            case 1: // BIT, no write-back
                f = (byte)((f & Carry) | HalfCarry | ((value & (1 << bit)) == 0 ? Zero : 0));
                break;
            case 2: WriteRegister(register, (byte)(value & ~(1 << bit))); break;
            case 3: WriteRegister(register, (byte)(value | (1 << bit))); break;
        }
    }

    private byte RotateShift(int operation, byte value)
    {
        var carryIn = (f & Carry) != 0 ? 1 : 0;
        int result;
        int carryOut;
        switch (operation)
        {
            case 0: carryOut = value >> 7; result = (value << 1) | carryOut; break; // RLC
            case 1: carryOut = value & 1; result = (value >> 1) | (carryOut << 7); break; // RRC
            case 2: carryOut = value >> 7; result = (value << 1) | carryIn; break; // RL
            case 3: carryOut = value & 1; result = (value >> 1) | (carryIn << 7); break; // RR
            case 4: carryOut = value >> 7; result = value << 1; break; // SLA
            case 5: carryOut = value & 1; result = (value >> 1) | (value & 0x80); break; // SRA
            case 6: carryOut = 0; result = (value << 4) | (value >> 4); break; // SWAP
            case 7: carryOut = value & 1; result = value >> 1; break; // SRL
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
        var output = unchecked((byte)result);
        f = (byte)((output == 0 ? Zero : 0) | (carryOut != 0 ? Carry : 0));
        return output;
    }
}
