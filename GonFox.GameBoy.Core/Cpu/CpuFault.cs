namespace GonFox.GameBoy.Core.Cpu;

// An undefined opcode that locked up the CPU, and the address it was fetched from.
public readonly record struct CpuFault(ushort Address, byte Opcode)
{
    public string Message => $"Opcode 0x{Opcode:X2} at PC 0x{Address:X4} is undefined on SM83.";
}
