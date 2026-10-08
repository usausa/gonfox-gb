namespace GonFox.GameBoy.Core;

using GonFox.GameBoy.Core.Cartridge;
using GonFox.GameBoy.Core.Cpu;
using GonFox.GameBoy.Core.Devices;
using GonFox.GameBoy.Core.Memory;

using Timer = GonFox.GameBoy.Core.Devices.Timer;

internal sealed class PeripheralTestMachine
{
    internal Interrupts Interrupts { get; } = new();
    internal Timer Timer { get; }
    internal Serial Serial { get; }
    internal Joypad Joypad { get; }
    internal Apu Apu { get; }
    internal Audio.AudioOutput Audio { get; } = new();
    internal MemoryBus Bus { get; } = new();
    internal Clock Clock { get; } = new();
    internal Sm83Cpu Cpu { get; }

    internal PeripheralTestMachine(params byte[] program)
    {
        Timer = new Timer(Interrupts);
        Apu = new Apu(Timer, Clock, Audio);
        Timer.ConnectApu(Apu);
        Serial = new Serial(Interrupts);
        Timer.ConnectSerial(Serial);
        Joypad = new Joypad(Interrupts);
        Bus.ConnectDevices(Interrupts, Timer, Serial, Joypad, Apu);
        Bus.ConnectCartridge(CartridgeLoader.Load(TestRom.Create(program)).Cartridge);
        Clock.ConnectTimer(Timer);
        Cpu = new Sm83Cpu(Bus, Clock, Interrupts, Joypad);
        Cpu.Reset();
        Cpu.StepInstruction(); // Entry JP to 0150.
        Clock.Reset();
        Interrupts.Reset();
        Interrupts.WriteFlags(0);
        Timer.Reset();
        Timer.ResetDivider();
        Serial.Reset();
        Joypad.Reset();
        Apu.Reset();
    }

    internal void TickTimer(int cycles)
    {
        for (var i = 0; i < cycles; i++)
        {
            Timer.Tick();
        }
    }
}
