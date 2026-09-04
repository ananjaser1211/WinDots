using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace WinDots.App.Shell;

/// <summary>
/// A hidden Win32 message window that carries the desktop integration the WinUI windows cannot: the global
/// <c>Win+Shift+M</c> hotkey (<c>RegisterHotKey</c>), and the notification-area (tray) icon with its context menu
/// (<c>Shell_NotifyIcon</c>). It owns a dedicated window class and <c>WNDPROC</c>; messages are pumped by the app's
/// existing UI-thread message loop because the window is created on that thread. All callbacks run on the UI thread,
/// so they may touch the drawer host and other windows directly.
/// </summary>
internal sealed class ShellMessageWindow : IDisposable
{
    private const int HotkeyId = 1;
    private const uint TrayIconId = 1;

    // Context-menu command ids (WM_COMMAND wParam low word).
    private const int CmdOpenDrawer = 100;
    private const int CmdInspector = 101;
    private const int CmdQuit = 102;

    private readonly NativeInterop.WndProcDelegate _wndProc; // Kept alive for the window's lifetime.
    /// <summary>Fixed class name so diagnostics can FindWindow it and post <see cref="NativeInterop.WM_APP_COMMAND"/>.</summary>
    public const string ClassName = "WinDots.ShellMessageWindow";

    // WM_APP_COMMAND wParam values (diagnostics hook; see _docs/07-testing-and-compatibility.md).
    public const int CommandToggleAtCursor = 1;
    public const int CommandToggleOnMonitor = 2; // lParam = monitor index
    public const int CommandDismiss = 3;
    public const int CommandShowInspector = 4;
    public const int CommandQuit = 5;
    public const int CommandDumpState = 6;
    public const int CommandPlayPause = 7;
    public const int CommandNextCandidate = 8;
    public const int CommandSeekForward = 9;

    private readonly string _className = ClassName;
    private readonly Action _onToggleAtCursor;
    private readonly Action<int> _onToggleOnMonitor;
    private readonly Action _onDismiss;
    private readonly Action _onDumpState;
    private readonly Action _onShowInspector;
    private readonly Action _onQuit;
    private readonly Action _onPlayPause;
    private readonly Action _onNextCandidate;
    private readonly Action _onSeekForward;

    private nint _hwnd;
    private nint _iconHandle;
    private bool _iconOwned;
    private bool _trayAdded;
    private bool _hotkeyRegistered;
    private bool _disposed;

    public ShellMessageWindow(
        Action onToggleAtCursor,
        Action<int> onToggleOnMonitor,
        Action onDismiss,
        Action onDumpState,
        Action onShowInspector,
        Action onQuit,
        Action onPlayPause,
        Action onNextCandidate,
        Action onSeekForward)
    {
        _onToggleAtCursor = onToggleAtCursor;
        _onToggleOnMonitor = onToggleOnMonitor;
        _onDismiss = onDismiss;
        _onDumpState = onDumpState;
        _onShowInspector = onShowInspector;
        _onQuit = onQuit;
        _onPlayPause = onPlayPause;
        _onNextCandidate = onNextCandidate;
        _onSeekForward = onSeekForward;
        _wndProc = WndProc;

        CreateWindow();
        RegisterHotkey();
        AddTrayIcon();
    }

    public nint Handle => _hwnd;

    private void CreateWindow()
    {
        var hInstance = NativeInterop.GetModuleHandle(null);
        var wndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProc);
        var wc = new NativeInterop.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeInterop.WNDCLASSEX>(),
            lpfnWndProc = wndProcPtr,
            hInstance = hInstance,
            lpszClassName = Marshal.StringToHGlobalUni(_className),
        };

        try
        {
            if (NativeInterop.RegisterClassEx(ref wc) == 0)
            {
                Debug.WriteLine($"ShellMessageWindow: RegisterClassEx failed ({Marshal.GetLastWin32Error()}).");
                return;
            }

            // A hidden top-level tool window (never shown): receives the hotkey and tray callbacks, and — unlike a
            // message-only HWND_MESSAGE window — can become the foreground window, which TrackPopupMenu requires so
            // the tray context menu does not dismiss itself instantly. WS_EX_TOOLWINDOW keeps it out of Alt+Tab.
            _hwnd = NativeInterop.CreateWindowEx(
                (uint)NativeInterop.WS_EX_TOOLWINDOW, _className, "WinDots", (uint)NativeInterop.WS_POPUP,
                0, 0, 0, 0, 0, 0, hInstance, 0);

            if (_hwnd == 0)
            {
                Debug.WriteLine($"ShellMessageWindow: CreateWindowEx failed ({Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(wc.lpszClassName);
        }
    }

    private void RegisterHotkey()
    {
        if (_hwnd == 0)
        {
            return;
        }

        // Win+Shift+M, parsed from the constant for now; the configurable store arrives in M5.
        _hotkeyRegistered = NativeInterop.RegisterHotKey(
            _hwnd, HotkeyId,
            NativeInterop.MOD_WIN | NativeInterop.MOD_SHIFT | NativeInterop.MOD_NOREPEAT,
            NativeInterop.VK_M);

        if (!_hotkeyRegistered)
        {
            // Another app may already own the combination; log and keep running.
            Debug.WriteLine($"ShellMessageWindow: RegisterHotKey Win+Shift+M failed ({Marshal.GetLastWin32Error()}); hotkey disabled.");
        }
    }

    private void AddTrayIcon()
    {
        if (_hwnd == 0)
        {
            return;
        }

        (_iconHandle, _iconOwned) = LoadTrayIcon();

        var data = new NativeInterop.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NativeInterop.NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = TrayIconId,
            uFlags = NativeInterop.NIF_MESSAGE | NativeInterop.NIF_ICON | NativeInterop.NIF_TIP,
            uCallbackMessage = NativeInterop.WM_APP_TRAY,
            hIcon = _iconHandle,
            szTip = "WinDots",
        };

        _trayAdded = NativeInterop.Shell_NotifyIcon(NativeInterop.NIM_ADD, ref data);
        if (!_trayAdded)
        {
            Debug.WriteLine("ShellMessageWindow: Shell_NotifyIcon(NIM_ADD) failed; tray icon unavailable.");
        }
    }

    /// <summary>Returns the icon handle and whether we own it (must DestroyIcon it on dispose).</summary>
    private static (nint Handle, bool Owned) LoadTrayIcon()
    {
        // Prefer the packaged Square44x44Logo; fall back to the default application icon.
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Square44x44Logo.png");
            if (File.Exists(path))
            {
                var handle = NativeInterop.LoadImage(0, path, NativeInterop.IMAGE_ICON, 0, 0, NativeInterop.LR_LOADFROMFILE | NativeInterop.LR_DEFAULTSIZE);
                if (handle != 0)
                {
                    return (handle, true);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ShellMessageWindow: tray icon load failed: {ex.Message}");
        }

        // IDI_APPLICATION (32512): always available, never needs freeing (LR_SHARED).
        return (NativeInterop.LoadImage(0, "#32512", NativeInterop.IMAGE_ICON, 0, 0, NativeInterop.LR_SHARED | NativeInterop.LR_DEFAULTSIZE), false);
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case NativeInterop.WM_HOTKEY when (int)wParam == HotkeyId:
                SafeInvoke(_onToggleAtCursor);
                return 0;

            case NativeInterop.WM_APP_TRAY:
                var mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
                if (mouseMsg == NativeInterop.WM_RBUTTONUP || mouseMsg == NativeInterop.WM_CONTEXTMENU)
                {
                    ShowContextMenu();
                }
                else if (mouseMsg == NativeInterop.WM_LBUTTONUP)
                {
                    SafeInvoke(_onToggleAtCursor);
                }

                return 0;

            case NativeInterop.WM_COMMAND:
                HandleCommand((int)(wParam.ToInt64() & 0xFFFF));
                return 0;

            case NativeInterop.WM_APP_COMMAND:
                HandleDiagnosticsCommand((int)wParam.ToInt64(), (int)lParam.ToInt64());
                return 0;

            default:
                return NativeInterop.DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    private void HandleDiagnosticsCommand(int command, int argument)
    {
        WinDots.App.Diagnostics.ShellLog.Write($"diagnostics command {command} arg {argument}");
        switch (command)
        {
            case CommandToggleAtCursor:
                SafeInvoke(_onToggleAtCursor);
                break;
            case CommandToggleOnMonitor:
                SafeInvoke(() => _onToggleOnMonitor(argument));
                break;
            case CommandDismiss:
                SafeInvoke(_onDismiss);
                break;
            case CommandShowInspector:
                SafeInvoke(_onShowInspector);
                break;
            case CommandQuit:
                SafeInvoke(_onQuit);
                break;
            case CommandDumpState:
                SafeInvoke(_onDumpState);
                break;
            case CommandPlayPause:
                SafeInvoke(_onPlayPause);
                break;
            case CommandNextCandidate:
                SafeInvoke(_onNextCandidate);
                break;
            case CommandSeekForward:
                SafeInvoke(_onSeekForward);
                break;
            default:
                break;
        }
    }

    private void HandleCommand(int commandId)
    {
        switch (commandId)
        {
            case CmdOpenDrawer:
                SafeInvoke(_onToggleAtCursor);
                break;
            case CmdInspector:
                SafeInvoke(_onShowInspector);
                break;
            case CmdQuit:
                SafeInvoke(_onQuit);
                break;
            default:
                break;
        }
    }

    private void ShowContextMenu()
    {
        var menu = NativeInterop.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            _ = NativeInterop.AppendMenu(menu, NativeInterop.MF_STRING, (nuint)CmdOpenDrawer, "Open drawer");
            _ = NativeInterop.AppendMenu(menu, NativeInterop.MF_STRING, (nuint)CmdInspector, "Session inspector");
            _ = NativeInterop.AppendMenu(menu, NativeInterop.MF_SEPARATOR, 0, null);
            _ = NativeInterop.AppendMenu(menu, NativeInterop.MF_STRING, (nuint)CmdQuit, "Quit");

            _ = NativeInterop.GetCursorPos(out var pt);

            // Foreground + a trailing WM_NULL are the documented TrackPopupMenu dance so the menu does not
            // dismiss itself the instant the pointer moves off it. Remember the real foreground first so the
            // drawer, when opened from the menu, records the user's app (not this hidden window) as the place
            // to return focus to.
            var previousForeground = NativeInterop.GetForegroundWindow();
            _ = NativeInterop.SetForegroundWindow(_hwnd);

            var chosen = NativeInterop.TrackPopupMenu(
                menu,
                NativeInterop.TPM_RIGHTBUTTON | NativeInterop.TPM_RETURNCMD,
                pt.X, pt.Y, 0, _hwnd, 0);

            _ = NativeInterop.PostMessage(_hwnd, NativeInterop.WM_NULL, 0, 0);

            if (previousForeground != 0 && previousForeground != _hwnd)
            {
                _ = NativeInterop.SetForegroundWindow(previousForeground);
            }

            if (chosen != 0)
            {
                HandleCommand(chosen);
            }
        }
        finally
        {
            _ = NativeInterop.DestroyMenu(menu);
        }
    }

    private static void SafeInvoke(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ShellMessageWindow: command handler threw: {ex}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_trayAdded)
        {
            var data = new NativeInterop.NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NativeInterop.NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = TrayIconId,
                szTip = string.Empty,
            };
            _ = NativeInterop.Shell_NotifyIcon(NativeInterop.NIM_DELETE, ref data);
            _trayAdded = false;
        }

        if (_hotkeyRegistered)
        {
            _ = NativeInterop.UnregisterHotKey(_hwnd, HotkeyId);
            _hotkeyRegistered = false;
        }

        // The file-loaded icon is owned by us; the IDI_APPLICATION fallback is shared and must not be destroyed.
        if (_iconOwned && _iconHandle != 0)
        {
            _ = NativeInterop.DestroyIcon(_iconHandle);
            _iconHandle = 0;
            _iconOwned = false;
        }

        if (_hwnd != 0)
        {
            _ = NativeInterop.DestroyWindow(_hwnd);
            _hwnd = 0;
        }
    }
}
