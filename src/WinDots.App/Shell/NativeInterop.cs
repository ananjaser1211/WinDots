using System;
using System.Runtime.InteropServices;

namespace WinDots.App.Shell;

/// <summary>
/// The narrow slice of Win32/DWM used to shape the two drawer windows: non-activating topmost tool-window styles,
/// foreground capture/restore for focus handoff, and DWM rounded corners. Kept as hand-written P/Invoke so the App
/// project does not need the CsWin32 generator; the signatures here are exercised by <see cref="HandleWindow"/>,
/// <see cref="DrawerWindow"/>, <see cref="DrawerHost"/>, and <see cref="ShellMessageWindow"/> (global hotkey and
/// the tray icon).
/// </summary>
internal static class NativeInterop
{
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    public const long WS_POPUP = 0x80000000L;
    public const long WS_CHILD = 0x40000000L;
    public const long WS_CAPTION = 0x00C00000L;
    public const long WS_THICKFRAME = 0x00040000L;

    public const long WS_EX_TOOLWINDOW = 0x00000080L;
    public const long WS_EX_NOACTIVATE = 0x08000000L;
    public const long WS_EX_TOPMOST = 0x00000008L;
    public const long WS_EX_LAYERED = 0x00080000L;
    public const long WS_EX_APPWINDOW = 0x00040000L;

    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_ROUND = 2;

    // GetSystemMetrics: nonzero when the process is running in a Remote Desktop / Terminal Services session,
    // where a live acrylic backdrop is unavailable and must fall back to an opaque surface.
    public const int SM_REMOTESESSION = 0x1000;

    // Hotkey modifiers (RegisterHotKey) and the 'M' virtual-key.
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;
    public const uint VK_M = 0x4D;

    // Media transport virtual-keys (E2 media.captureMediaKeys).
    public const uint VK_MEDIA_NEXT_TRACK = 0xB0;
    public const uint VK_MEDIA_PREV_TRACK = 0xB1;
    public const uint VK_MEDIA_STOP = 0xB2;
    public const uint VK_MEDIA_PLAY_PAUSE = 0xB3;

    // Window messages we handle on the hidden message window.
    public const uint WM_NULL = 0x0000;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint WM_APP_TRAY = 0x0400 + 1; // WM_USER + 1: our tray-icon callback message.
    public const uint WM_APP_COMMAND = 0x8000 + 2; // WM_APP + 2: diagnostics command hook (wParam = command, lParam = argument).

    public const int HWND_MESSAGE = -3;

    // Shell_NotifyIcon.
    public const uint NIM_ADD = 0x00000000;
    public const uint NIM_MODIFY = 0x00000001;
    public const uint NIM_DELETE = 0x00000002;
    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON = 0x00000002;
    public const uint NIF_TIP = 0x00000004;

    // LoadImage / images.
    public const uint IMAGE_ICON = 1;
    public const uint LR_LOADFROMFILE = 0x00000010;
    public const uint LR_DEFAULTSIZE = 0x00000040;
    public const uint LR_SHARED = 0x00008000;

    // TrackPopupMenu / AppendMenu.
    public const uint MF_STRING = 0x00000000;
    public const uint MF_SEPARATOR = 0x00000800;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_RETURNCMD = 0x0100;

    public delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public nint lpszMenuName;
        public nint lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint LoadImage(nint hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    public static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AppendMenu(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    public static extern int TrackPopupMenu(nint hMenu, uint uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentProcessId();

    public static void ApplyStyles(nint hwnd, long styleSet, long styleClear, long exSet, long exClear)
    {
        var style = GetWindowLongPtr(hwnd, GWL_STYLE);
        var newStyle = (style & ~styleClear) | styleSet;
        _ = SetWindowLongPtr(hwnd, GWL_STYLE, (nint)newStyle);

        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        var newEx = (ex & ~exClear) | exSet;
        _ = SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)newEx);
    }

    /// <summary>True when the current foreground window belongs to this process (a handle or the inspector).</summary>
    public static bool IsForegroundOwnedByThisProcess()
    {
        var foreground = GetForegroundWindow();
        if (foreground == 0)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(foreground, out var pid);
        return pid == GetCurrentProcessId();
    }

    /// <summary>
    /// Makes <paramref name="hwnd"/> the foreground window. Tries the plain call first; if Windows refuses (the
    /// foreground lock), temporarily attaches to the current foreground thread's input queue, which is the
    /// documented workaround for a process that has just received input on another of its windows.
    /// </summary>
    public static bool ForceForeground(nint hwnd)
    {
        var current = GetForegroundWindow();
        if (current == hwnd)
        {
            return true;
        }

        if (SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd)
        {
            return true;
        }

        var foregroundThread = current == 0 ? 0 : GetWindowThreadProcessId(current, out _);
        var thisThread = GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != thisThread && AttachThreadInput(foregroundThread, thisThread, true);
        try
        {
            _ = BringWindowToTop(hwnd);
            _ = SetForegroundWindow(hwnd);
            _ = SetFocus(hwnd);
        }
        finally
        {
            if (attached)
            {
                _ = AttachThreadInput(foregroundThread, thisThread, false);
            }
        }

        return GetForegroundWindow() == hwnd;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint hWnd);

    /// <summary>True when this process is running inside a Remote Desktop session (acrylic must fall back).</summary>
    public static bool IsRemoteSession() => GetSystemMetrics(SM_REMOTESESSION) != 0;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    public static void SetRoundedCorners(nint hwnd)
    {
        var pref = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
    }

    /// <summary>
    /// Clips <paramref name="hwnd"/> to a fully rounded (stadium) region the size of its client area, in physical
    /// pixels, so the window itself reads as the collapsed pill and its corners are transparent and click-through.
    /// Call after every move/resize; <see cref="SetWindowRgn"/> takes ownership of the region handle, so the caller
    /// must not delete it. A radius equal to the height rounds each end into a semicircle.
    /// </summary>
    public static void SetPillRegion(nint hwnd, int physicalWidth, int physicalHeight)
    {
        if (physicalWidth <= 0 || physicalHeight <= 0)
        {
            return;
        }

        // +1 because CreateRoundRectRgn's right/bottom are exclusive; the ellipse axes are the full height for a stadium.
        var region = CreateRoundRectRgn(0, 0, physicalWidth + 1, physicalHeight + 1, physicalHeight, physicalHeight);
        _ = SetWindowRgn(hwnd, region, bRedraw: true);
    }

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(nint hWnd, nint hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
