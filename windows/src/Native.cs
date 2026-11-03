using System.Runtime.InteropServices;

namespace Nimbus;

/// <summary>The few Win32 calls WinForms does not wrap: DWM window styling, taskbar edge, monitor DPI.</summary>
internal static class Native
{
    // ---- DWM (dwmapi.dll) ----
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;   // Windows 10 20H1+ / 11
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;  // Windows 11
    public const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void SetDwmInt(IntPtr hwnd, int attribute, int value)
    {
        // Returns an HRESULT; older Windows builds reject unknown attributes, which is fine.
        _ = DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
    }

    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    // ---- Taskbar edge (shell32.dll) ----
    public const uint ABM_GETTASKBARPOS = 0x00000005;
    public const uint ABE_LEFT = 0, ABE_TOP = 1, ABE_RIGHT = 2, ABE_BOTTOM = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")]
    public static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    /// <summary>Which screen edge the taskbar is on (ABE_*), or bottom when it cannot be read.</summary>
    public static uint TaskbarEdge()
    {
        var data = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        return SHAppBarMessage(ABM_GETTASKBARPOS, ref data) != UIntPtr.Zero ? data.uEdge : ABE_BOTTOM;
    }

    // ---- Monitor DPI (user32.dll / shcore.dll) ----
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>Scale factor (1.0 = 96 DPI) of the monitor under a screen point, in physical pixels.</summary>
    public static double ScaleAt(Point p)
    {
        var monitor = MonitorFromPoint(new POINT { X = p.X, Y = p.Y }, MONITOR_DEFAULTTONEAREST);
        return GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0 ? dpiX / 96.0 : 1.0;
    }

    // ---- Focus ----
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    public const int ASFW_ANY = -1;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(int dwProcessId);

    // ---- Tray icon position (for exact anchoring and the automated test) ----
    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONIDENTIFIER
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [DllImport("shell32.dll")]
    public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    // ---- Tray icon size ----
    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    public static extern int GetSystemMetricsForDpi(int index, uint dpi);
    public const int SM_CXSMICON = 49;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr handle);
}
