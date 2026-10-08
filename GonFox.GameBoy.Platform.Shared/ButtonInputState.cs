namespace GonFox.GameBoy.Platform;

using GonFox.GameBoy.Core.Devices;

// Each physical key, mouse button or touch contact owns its press independently.
public sealed class ButtonInputState
{
    private readonly Dictionary<string, JoypadButton> held = [];
    public byte Mask { get; private set; }

    public bool Set(string source, JoypadButton button, bool pressed)
    {
        if (pressed)
        {
            held[source] = button;
        }
        else
        {
            held.Remove(source);
        }

        byte next = 0;
        foreach (var value in held.Values)
        {
            next |= (byte)(1 << (int)value);
        }

        var changed = next != Mask;
        Mask = next;
        return changed;
    }

    public void Clear()
    {
        held.Clear();
        Mask = 0;
    }
}
