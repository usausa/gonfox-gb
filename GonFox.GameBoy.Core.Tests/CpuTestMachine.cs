namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Cpu;
using GonFox.GameBoy.Core.Memory;

// CPU-only fixture that sets its registers by running real instructions.
internal sealed class CpuTestMachine
{
    private readonly TestCartridge cartridge;
    internal MemoryBus Bus { get; } = new();
    internal Clock Clock { get; } = new();
    internal Sm83Cpu Cpu { get; }
    internal DebugSnapshot State => Cpu.GetDebugSnapshot();
    internal List<(bool Write, ushort Address, byte Value, ulong Time)> Accesses => cartridge.Accesses;

    internal CpuTestMachine(byte[] program, ushort af = 0x01B0, ushort bc = 0x0013,
        ushort de = 0x00D8, ushort hl = 0xC100, ushort sp = 0xD000)
    {
        cartridge = new TestCartridge(Clock);
        Bus.ConnectCartridge(cartridge);
        Cpu = new Sm83Cpu(Bus, Clock);
        Reset(program, af, bc, de, hl, sp);
    }

    internal void Reset(byte[] program, ushort af = 0x01B0, ushort bc = 0x0013,
        ushort de = 0x00D8, ushort hl = 0xC100, ushort sp = 0xD000)
    {
        byte[] setup =
        [
            0x31, 0x00, 0xD0, 0x01, (byte)af, (byte)(af >> 8), 0xC5, 0xF1,
            0x01, (byte)bc, (byte)(bc >> 8), 0x11, (byte)de, (byte)(de >> 8),
            0x21, (byte)hl, (byte)(hl >> 8), 0x31, (byte)sp, (byte)(sp >> 8),
            0xC3, 0x00, 0x02
        ];
        setup.CopyTo(cartridge.Rom, 0x100);
        program.CopyTo(cartridge.Rom, 0x200);
        Bus.Reset();
        Cpu.Reset();
        Clock.Reset();
        for (var i = 0; i < 9; i++)
        {
            Cpu.StepInstruction();
        }

        Clock.Reset();
        Accesses.Clear();
    }

    private sealed class TestCartridge(Clock clock) : ICartridge
    {
        internal byte[] Rom { get; } = new byte[0x8000];
        private readonly byte[] ram = new byte[0x2000];
        internal List<(bool Write, ushort Address, byte Value, ulong Time)> Accesses { get; } = [];

        public byte Read(ushort address)
        {
            var value = address < 0x8000 ? Rom[address] : ram[address - 0xA000];
            Accesses.Add((false, address, value, clock.TotalTCycles));
            return value;
        }

        public void Write(ushort address, byte value)
        {
            Accesses.Add((true, address, value, clock.TotalTCycles));
            if (address >= 0xA000)
            {
                ram[address - 0xA000] = value;
            }
        }

        public void ResetController()
        {
        }
    }
}
