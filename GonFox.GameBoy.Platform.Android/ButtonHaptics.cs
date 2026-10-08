namespace GonFox.GameBoy.Platform.Android;

using global::Android.Views;

// Plays the system's virtual-key haptic tick for an on-screen button; returns whether it ran.
public static class ButtonHaptics
{
    public static bool Press(View? view) => view?.PerformHapticFeedback(FeedbackConstants.VirtualKey) == true;
}
