namespace GonFox.GameBoy.Platform;

public static class DisplayPolicy
{
    // Tells whether a display should draw every vsync frame rather than refresh on a slow timer.
    public static bool WantsEveryFrame(bool closed, bool visible, EmulationStatus status) =>
        !closed && visible && status.Running && !status.Stopped;
}
