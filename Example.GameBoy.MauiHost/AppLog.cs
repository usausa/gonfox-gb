namespace Example.GameBoy.MauiHost;

// Writes info and debug lines to logcat for the device checks.
internal static class AppLog
{
    internal const string Tag = "GonFox.GameBoy";
    internal static void Info(string message) => Android.Util.Log.Info(Tag, message);
    internal static void Debug(string message) => Android.Util.Log.Debug(Tag, message);
}
