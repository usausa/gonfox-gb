namespace GonFox.GameBoy.Core.Devices;

// NRx2 of the pulse and noise channels: the DAC (upper five bits) and the volume envelope.
internal sealed class VolumeEnvelope
{
    private byte register;
    private int timer;
    private bool running;

    internal sealed record State(byte Register, int Volume, int Timer, bool Running);

    internal State CaptureState() => new(register, Volume, timer, running);
    internal static bool IsValid(State? s) => s?.Volume is >= 0 and <= 15 && s.Timer is >= 0 and <= 9;
    internal void RestoreState(State s) => (register, Volume, timer, running) = (s.Register, s.Volume, s.Timer, s.Running);

    internal bool DacOn => (register & 0xF8) != 0;
    internal int Volume { get; private set; }

    // Writes NRx2 and returns whether the DAC is on; while playing, a running period 0 adds 1 volume.
    internal bool Write(byte value, bool channelOn)
    {
        if ((value & 0xF8) != 0 && channelOn && (register & 7) == 0 && running)
        {
            Volume = (Volume + 1) & 15;
        }

        register = value;
        return DacOn;
    }

    // Restarts the envelope; envelopeNext (the next DIV-APU step is 7) delays its first step.
    internal void Trigger(bool envelopeNext)
    {
        Volume = register >> 4;
        timer = ((register & 7) == 0 ? 8 : register & 7) + (envelopeNext ? 1 : 0);
        running = true;
    }

    internal void Clock()
    {
        var period = register & 7;
        if (period == 0)
        {
            // Period 0 counts as 8 but never steps.
            timer = 8;
            return;
        }

        if (!running || --timer != 0)
        {
            return;
        }

        timer = period;
        var volume = Volume + ((register & 8) != 0 ? 1 : -1);
        if (volume is >= 0 and <= 15)
        {
            Volume = volume;
        }
        else
        {
            running = false;
        }
    }

    internal void PowerOff() => (register, Volume, timer, running) = (0, 0, 0, false);

    // BootBypass: the envelope has already run down to 0 and stopped.
    internal void Boot(byte value) => (register, Volume, timer, running) = (value, 0, value & 7, false);
}
