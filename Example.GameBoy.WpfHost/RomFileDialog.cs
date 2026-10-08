namespace Example.GameBoy.WpfHost;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

using Microsoft.Win32;

// Runs the file dialog on its own STA thread so its slow shutdown does not freeze the window.
internal static class RomFileDialog
{
    internal const string Filter = "Game Boy ROM (*.gb)|*.gb|All files|*.*";
    internal const string BootRomFilter = "Boot ROM (*.bin)|*.bin|All files|*.*";

    private const string DialogClass = "#32770";
    private const int GwlpHwndParent = -8;
    private const uint GwOwner = 4;
    private const int SwHide = 0;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpHideWindow = 0x0080;
    private const int WhCallWndProc = 4;
    private const uint WmWindowPosChanging = 0x0046;

    internal static Task<string?> ChooseAsync(Window owner, string filter = Filter)
    {
        var session = new Session(new WindowInteropHelper(owner).Handle, filter);
        GetWindowRect(session.Owner, out var bounds);
        EnableWindow(session.Owner, false);
        var thread = new Thread(() => session.Run(bounds)) { IsBackground = true, Name = "ROM file dialog" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return session.Choice.Task;
    }

    // Shows one dialog on its thread and gives the main window back before the dialog hides.
    private sealed class Session(IntPtr owner, string filter)
    {
        private IntPtr anchor;
        private bool released;

        internal IntPtr Owner { get; } = owner;

        internal TaskCompletionSource<string?> Choice { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Run(NativeRect bounds)
        {
            var anchorWindow = new Window { WindowStyle = WindowStyle.None, ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize };
            HookProc watch = Watch;
            var hook = IntPtr.Zero;
#pragma warning disable CA1031
            try
            {
                anchor = new WindowInteropHelper(anchorWindow) { Owner = Owner }.EnsureHandle();

                // Places the never-shown anchor window over the main window.
                SetWindowPos(anchor, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top,
                    SwpNoActivate | SwpNoZOrder);
                hook = SetWindowsHookEx(WhCallWndProc, watch, IntPtr.Zero, GetCurrentThreadId()); // This thread's windows only.
                var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
                dialog.FileOk += (_, _) =>
                {
                    if (Release(FindDialog()))
                    {
                        Choice.TrySetResult(dialog.FileName);
                    }
                };
                var chosen = dialog.ShowDialog(anchorWindow) == true;
                if (Release(IntPtr.Zero))
                {
                    Choice.TrySetResult(chosen ? dialog.FileName : null); // The hide was not seen.
                }
            }
            catch (Exception exception)
            {
                Release(IntPtr.Zero);
                Choice.TrySetException(exception);
            }
            finally
            {
                if (hook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(hook);
                }

                GC.KeepAlive(watch);
                anchorWindow.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
#pragma warning restore CA1031
        }

        // Watches this thread's messages and treats the dialog hiding without FileOk as a cancel.
        private IntPtr Watch(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && !released)
            {
                var sent = Marshal.PtrToStructure<SentMessage>(lParam);
                if (sent.Message == WmWindowPosChanging && (Marshal.PtrToStructure<WindowPos>(sent.LParam).Flags & SwpHideWindow) != 0 &&
                    GetWindow(sent.Window, GwOwner) == anchor && IsWindowVisible(sent.Window) && Release(IntPtr.Zero))
                {
                    Choice.TrySetResult(null);
                }
            }

            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        // Gives the main window back once, while the dialog is still visible, and detaches the anchor.
        private bool Release(IntPtr dialog)
        {
            if (released)
            {
                return false;
            }

            released = true;
            EnableWindow(Owner, true);
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || (GetWindowThreadProcessId(foreground, out var process) != 0 && process == (uint)Environment.ProcessId))
            {
                SetForegroundWindow(Owner);
            }

            if (dialog != IntPtr.Zero)
            {
                ShowWindow(dialog, SwHide);
            }

            if (anchor != IntPtr.Zero)
            {
                SetWindowLongPtr(anchor, GwlpHwndParent, IntPtr.Zero); // Clears the owner.
            }

            return true;
        }
    }

    // Finds the visible dialog window of the calling thread.
    private static IntPtr FindDialog()
    {
        var found = IntPtr.Zero;
        EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
        {
            var name = new char[16];
            var length = GetClassName(window, name, name.Length);
            if (!name.AsSpan(0, length).SequenceEqual(DialogClass) || !IsWindowVisible(window))
            {
                return true;
            }

            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // Mirrors the Win32 CWPSTRUCT.
    [StructLayout(LayoutKind.Sequential)]
    private struct SentMessage
    {
        public IntPtr LParam;
        public IntPtr WParam;
        public uint Message;
        public IntPtr Window;
    }

    // Mirrors the Win32 WINDOWPOS.
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr Window;
        public IntPtr InsertAfter;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public uint Flags;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool EnableWindow(IntPtr window, bool enable);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool EnumThreadWindows(uint thread, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetClassName(IntPtr window, [Out] char[] name, int capacity);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr SetWindowsHookEx(int type, HookProc callback, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetCurrentThreadId();
}
