using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using WinDots.Core.Contracts;
using WinDots.Core.Settings;

namespace WinDots.App.Shell;

/// <summary>
/// A hidden Win32 message window that carries the desktop integration the WinUI windows cannot: the global
/// <c>Win+Shift+M</c> hotkey (<c>RegisterHotKey</c>), and the notification-area (tray) icon with its context menu
/// (<c>Shell_NotifyIcon</c>). It owns a dedicated window class and <c>WNDPROC</c>; messages are pumped by the app's
/// existing UI-thread message loop because the window is created on that thread. All callbacks run on the UI thread,
/// so they may touch the drawer host and other windows directly.
/// </summary>
public sealed class ShellMessageWindow : IDisposable
{
    private const int HotkeyId = 1;

    // Media-key hotkey ids (E2 media.captureMediaKeys).
    private const int HotkeyMediaPlayPause = 10;
    private const int HotkeyMediaNext = 11;
    private const int HotkeyMediaPrevious = 12;
    private const int HotkeyMediaStop = 13;

    private const uint TrayIconId = 1;

    // Context-menu command ids (WM_COMMAND wParam low word).
    private const int CmdOpenDrawer = 100;
    private const int CmdInspector = 101;
    private const int CmdSettings = 103;
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
    public const int CommandOpenSettings = 10;
    public const int CommandAudioMatch = 11;
    public const int CommandSetVolume25 = 12;
    public const int CommandToggleMute = 13;

    private readonly string _className = ClassName;
    private readonly ISettingsStore _settings;
    private readonly Action _onToggleAtCursor;
    private readonly Action<int> _onToggleOnMonitor;
    private readonly Action _onDismiss;
    private readonly Action _onDumpState;
    private readonly Action _onShowInspector;
    private readonly Action _onShowSettings;
    private readonly Action _onQuit;
    private readonly Action _onPlayPause;
    private readonly Action _onNextCandidate;
    private readonly Action _onSeekForward;
    private readonly Action _onAudioMatch;
    private readonly Action _onSetVolume25;
    private readonly Action _onToggleMute;
    private readonly Action _onMediaPlayPause;
    private readonly Action _onMediaNext;
    private readonly Action _onMediaPrevious;
    private readonly Action _onMediaStop;

    private bool _mediaKeysRegistered;
    private bool _lastCaptureMediaKeys;

    /// <summary>
    /// The outcome of the most recent toggle-hotkey registration and the chord it was for. The settings UI reads this
    /// to surface a conflict inline; <see cref="ToggleHotkeyStatusChanged"/> fires whenever it is recomputed (including
    /// the live re-register after the chord is changed in Settings) so a fix clears the warning without a restart.
    /// </summary>
    public HotkeyRegistration ToggleHotkeyRegistration { get; private set; }

    /// <summary>The chord the last registration attempt used (after fallback), for display alongside a conflict.</summary>
    public Shortcut? ToggleHotkeyChord { get; private set; }

    /// <summary>Raised (on the UI thread) after every toggle-hotkey registration attempt.</summary>
    public event EventHandler? ToggleHotkeyStatusChanged;

    private nint _hwnd;
    private nint _iconHandle;
    private bool _iconOwned;
    private bool _trayAdded;
    private bool _hotkeyRegistered;
    private bool _disposed;

    public ShellMessageWindow(
        ISettingsStore settings,
        Action onToggleAtCursor,
        Action<int> onToggleOnMonitor,
        Action onDismiss,
        Action onDumpState,
        Action onShowInspector,
        Action onShowSettings,
        Action onQuit,
        Action onPlayPause,
        Action onNextCandidate,
        Action onSeekForward,
        Action onAudioMatch,
        Action onSetVolume25,
        Action onToggleMute,
        Action onMediaPlayPause,
        Action onMediaNext,
        Action onMediaPrevious,
        Action onMediaStop)
    {
        _settings = settings;
        _onAudioMatch = onAudioMatch;
        _onSetVolume25 = onSetVolume25;
        _onToggleMute = onToggleMute;
        _onMediaPlayPause = onMediaPlayPause;
        _onMediaNext = onMediaNext;
        _onMediaPrevious = onMediaPrevious;
        _onMediaStop = onMediaStop;
        _onToggleAtCursor = onToggleAtCursor;
        _onToggleOnMonitor = onToggleOnMonitor;
        _onDismiss = onDismiss;
        _onDumpState = onDumpState;
        _onShowInspector = onShowInspector;
        _onShowSettings = onShowSettings;
        _onQuit = onQuit;
        _onPlayPause = onPlayPause;
        _onNextCandidate = onNextCandidate;
        _onSeekForward = onSeekForward;
        _wndProc = WndProc;

        CreateWindow();
        RegisterHotkey();
        _lastCaptureMediaKeys = _settings.Current.Media.CaptureMediaKeys;
        ApplyMediaKeyCapture(_lastCaptureMediaKeys);
        AddTrayIcon();

        // Live-react to a changed toggle shortcut: unregister and re-register from the new value.
        _settings.Changed += OnSettingsChanged;
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

        if (_hotkeyRegistered)
        {
            _ = NativeInterop.UnregisterHotKey(_hwnd, HotkeyId);
            _hotkeyRegistered = false;
        }

        // The chord comes from drawer.toggleShortcut; on a parse failure fall back to Win+Shift+M and log it.
        string configured = _settings.Current.Drawer.ToggleShortcut;
        Shortcut chord;
        if (ShortcutParser.TryParse(configured, out Shortcut? parsed))
        {
            chord = parsed;
        }
        else
        {
            chord = new Shortcut(ShortcutModifiers.Win | ShortcutModifiers.Shift, (int)NativeInterop.VK_M);
            WinDots.App.Diagnostics.ShellLog.Write($"hotkey: '{configured}' is not a valid shortcut; falling back to Win+Shift+M");
        }

        uint mods = ToWin32Modifiers(chord.Modifiers) | NativeInterop.MOD_NOREPEAT;
        bool ok = NativeInterop.RegisterHotKey(_hwnd, HotkeyId, mods, (uint)chord.Key);
        // Capture the error immediately, before any other Win32 call can overwrite it.
        HotkeyRegistration outcome = HotkeyRegistration.Classify(ok, ok ? 0 : Marshal.GetLastWin32Error());
        _hotkeyRegistered = ok;
        ToggleHotkeyRegistration = outcome;
        ToggleHotkeyChord = chord;

        switch (outcome.Outcome)
        {
            case HotkeyOutcome.Registered:
                WinDots.App.Diagnostics.ShellLog.Write($"hotkey registered: {ShortcutParser.Format(chord)}");
                break;
            case HotkeyOutcome.Conflict:
                // 1409: the combination is already owned by another application (e.g. PowerToys). Keep running.
                WinDots.App.Diagnostics.ShellLog.Write(
                    $"hotkey: {ShortcutParser.Format(chord)} conflicts with another app that already owns this combination "
                    + $"(code {outcome.Code}); hotkey disabled until you pick a different chord");
                break;
            default:
                WinDots.App.Diagnostics.ShellLog.Write(
                    $"hotkey: RegisterHotKey {ShortcutParser.Format(chord)} failed ({outcome.Code}); hotkey disabled");
                break;
        }

        ToggleHotkeyStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private static uint ToWin32Modifiers(ShortcutModifiers modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(ShortcutModifiers.Win))
        {
            result |= NativeInterop.MOD_WIN;
        }

        if (modifiers.HasFlag(ShortcutModifiers.Ctrl))
        {
            result |= NativeInterop.MOD_CONTROL;
        }

        if (modifiers.HasFlag(ShortcutModifiers.Alt))
        {
            result |= NativeInterop.MOD_ALT;
        }

        if (modifiers.HasFlag(ShortcutModifiers.Shift))
        {
            result |= NativeInterop.MOD_SHIFT;
        }

        return result;
    }

    private string _lastShortcut = string.Empty;

    private void OnSettingsChanged(object? sender, WinDots.Core.Settings.Settings s)
    {
        // Only touch the hotkey when the chord actually changed, so unrelated saves do not churn registration.
        string shortcut = s.Drawer.ToggleShortcut;
        if (shortcut != _lastShortcut)
        {
            _lastShortcut = shortcut;
            RegisterHotkey();
        }

        // React to a changed media-key capture toggle.
        if (s.Media.CaptureMediaKeys != _lastCaptureMediaKeys)
        {
            _lastCaptureMediaKeys = s.Media.CaptureMediaKeys;
            ApplyMediaKeyCapture(_lastCaptureMediaKeys);
        }
    }

    /// <summary>
    /// Registers or unregisters the media transport keys (Play/Pause, Next, Previous, Stop) as modifier-less global
    /// hotkeys so a paused video in another window never steals them from the active music session. E2 opt-in.
    /// </summary>
    private void ApplyMediaKeyCapture(bool enabled)
    {
        if (enabled)
        {
            RegisterMediaKeys();
        }
        else
        {
            UnregisterMediaKeys();
        }
    }

    private void RegisterMediaKeys()
    {
        if (_hwnd == 0 || _mediaKeysRegistered)
        {
            return;
        }

        bool all = true;
        all &= RegisterMediaKey(HotkeyMediaPlayPause, NativeInterop.VK_MEDIA_PLAY_PAUSE, "Play/Pause");
        all &= RegisterMediaKey(HotkeyMediaNext, NativeInterop.VK_MEDIA_NEXT_TRACK, "Next");
        all &= RegisterMediaKey(HotkeyMediaPrevious, NativeInterop.VK_MEDIA_PREV_TRACK, "Previous");
        all &= RegisterMediaKey(HotkeyMediaStop, NativeInterop.VK_MEDIA_STOP, "Stop");
        _mediaKeysRegistered = true;
        WinDots.App.Diagnostics.ShellLog.Write($"media keys: capture enabled (allRegistered={all})");
    }

    private bool RegisterMediaKey(int id, uint vk, string name)
    {
        bool ok = NativeInterop.RegisterHotKey(_hwnd, id, NativeInterop.MOD_NOREPEAT, vk);
        if (!ok)
        {
            WinDots.App.Diagnostics.ShellLog.Write(
                $"media keys: RegisterHotKey {name} failed ({Marshal.GetLastWin32Error()})");
        }

        return ok;
    }

    private void UnregisterMediaKeys()
    {
        if (_hwnd == 0 || !_mediaKeysRegistered)
        {
            return;
        }

        _ = NativeInterop.UnregisterHotKey(_hwnd, HotkeyMediaPlayPause);
        _ = NativeInterop.UnregisterHotKey(_hwnd, HotkeyMediaNext);
        _ = NativeInterop.UnregisterHotKey(_hwnd, HotkeyMediaPrevious);
        _ = NativeInterop.UnregisterHotKey(_hwnd, HotkeyMediaStop);
        _mediaKeysRegistered = false;
        WinDots.App.Diagnostics.ShellLog.Write("media keys: capture disabled");
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

            case NativeInterop.WM_HOTKEY when (int)wParam == HotkeyMediaPlayPause:
                SafeInvoke(_onMediaPlayPause);
                return 0;

            case NativeInterop.WM_HOTKEY when (int)wParam == HotkeyMediaNext:
                SafeInvoke(_onMediaNext);
                return 0;

            case NativeInterop.WM_HOTKEY when (int)wParam == HotkeyMediaPrevious:
                SafeInvoke(_onMediaPrevious);
                return 0;

            case NativeInterop.WM_HOTKEY when (int)wParam == HotkeyMediaStop:
                SafeInvoke(_onMediaStop);
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
            case CommandOpenSettings:
                SafeInvoke(_onShowSettings);
                break;
            case CommandAudioMatch:
                SafeInvoke(_onAudioMatch);
                break;
            case CommandSetVolume25:
                SafeInvoke(_onSetVolume25);
                break;
            case CommandToggleMute:
                SafeInvoke(_onToggleMute);
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
            case CmdSettings:
                SafeInvoke(_onShowSettings);
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
            _ = NativeInterop.AppendMenu(menu, NativeInterop.MF_STRING, (nuint)CmdSettings, "Settings");
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

        UnregisterMediaKeys();

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
