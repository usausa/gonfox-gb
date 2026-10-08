namespace GonFox.GameBoy.Platform.Windows;

using System.Runtime.InteropServices;

// Names the current thread for Windows tools, which do not see .NET's Thread.Name.
public static class WindowsThread
{
    // A failure (an older Windows) only leaves the thread unnamed.
    public static void DescribeCurrent(string description) => _ = SetThreadDescription(GetCurrentThread(), description);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SetThreadDescription(IntPtr thread, string description);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetCurrentThread();
}
