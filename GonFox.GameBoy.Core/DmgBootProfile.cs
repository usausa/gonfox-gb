namespace GonFox.GameBoy.Core;

// A deterministic boot-ROM bypass profile, not an exact power-on simulation.
internal static class DmgBootProfile
{
    internal const ushort AF = 0x01B0;
    internal const ushort BC = 0x0013;
    internal const ushort DE = 0x00D8;
    internal const ushort HL = 0x014D;
    internal const ushort SP = 0xFFFE;
    internal const ushort PC = 0x0100;
    internal const byte RamFill = 0x00;
    internal const ushort DividerCounter = 0xABCC;
    internal const byte InterruptFlags = 0x01;
    internal const byte InterruptEnable = 0x00;

    // Both rows selected: P1 reads CF.
    internal const byte JoypadSelection = 0x00;
    internal const byte LcdControl = 0x91;
    internal const byte BackgroundPalette = 0xFC;

    // PC=0100 comes 56 dots before line 0, in mode 1 with LY already reading 0.
    internal const int PpuLine = 153;
    internal const int PpuDot = 400;

    // NR10-NR51 as read after the boot ROM; CH1 stays on, silent at the end of its envelope.
    internal static ReadOnlySpan<byte> ApuRegisters =>
        [0x80, 0x80, 0xF3, 0xC1, 0x87, 0, 0x3F, 0, 0xFF, 0xBF, 0x7F,
         0xFF, 0x9F, 0xFF, 0xBF, 0, 0xFF, 0, 0, 0xBF, 0x77, 0xF3];
    internal const int Pulse1Period = 0x7C1;

    // Frame-sequencer step 0 is done, and CH1 is on duty step 3 with its next step 276 T after PC=0100.
    internal const int ApuNextStep = 1;
    internal const int Pulse1Position = 3;
    internal const int Pulse1Timer = 276;
}
