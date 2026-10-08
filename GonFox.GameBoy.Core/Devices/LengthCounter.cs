namespace GonFox.GameBoy.Core.Devices;

// A channel's length timer: 64 steps, or 256 for the wave channel.
internal sealed class LengthCounter(int max)
{
    private int counter;
    private bool enabled;

    internal sealed record State(int Counter, bool Enabled);

    internal State CaptureState() => new(counter, enabled);
    internal static bool IsValid(State? s, int max) => s is not null && s.Counter >= 0 && s.Counter <= max;
    internal void RestoreState(State s) => (counter, enabled) = (s.Counter, s.Enabled);

    internal void Load(int length) => counter = max - length;

    // Applies an NRx4 write (lengthNext: the next DIV-APU step clocks lengths); false stops the channel.
    internal bool Write(bool enable, bool trigger, bool lengthNext)
    {
        bool wasEnabled = enabled, keep = true;
        enabled = enable;

        // Enabling the counter before a step that does not clock it clocks it once more.
        if (!wasEnabled && enable && !lengthNext && counter > 0 && --counter == 0 && !trigger)
        {
            keep = false;
        }

        if (trigger && counter == 0)
        {
            counter = enable && !lengthNext ? max - 1 : max;
        }

        return keep;
    }

    // Clocks the counter; returns false when it runs out and the channel stops.
    internal bool Clock() => !(enabled && counter > 0 && --counter == 0);

    internal void PowerOff() => (counter, enabled) = (0, false);
    internal void Boot(int length) => (counter, enabled) = (length, false);
}
