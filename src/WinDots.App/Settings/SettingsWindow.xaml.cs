using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;
using Windows.Graphics;
using WinDots.App.Diagnostics;
using WinDots.Core.Contracts;
using WinDots.Core.Settings;
using CoreSettings = WinDots.Core.Settings.Settings;

namespace WinDots.App.Settings;

/// <summary>
/// The product settings window: a plain WinUI window with normal chrome, shown in switchers, kept to a single
/// instance by <see cref="App"/>. Sections mirror <c>_docs/06-settings-schema.md</c>. Edits are staged in the
/// controls and only persisted when the user presses Save (which calls <see cref="ISettingsStore.SaveAsync"/>);
/// Revert reloads the controls from the store's current values. The "Start with Windows" toggle is the one
/// exception: it drives the packaged <see cref="StartupTask"/> immediately, since that state lives in Windows,
/// not in settings.json.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private const string StartupTaskId = "WinDotsStartup";
    private static readonly Regex HexColor = new("^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);

    private readonly ISettingsStore _store;
    private readonly IMonitorService? _monitors;
    private readonly List<CheckBox> _monitorChecks = new();

    // Suppresses control-changed handlers while the UI is being populated from settings.
    private bool _loading;
    private StartupTask? _startupTask;

    public SettingsWindow(ISettingsStore store, IMonitorService? monitors)
    {
        _store = store;
        _monitors = monitors;
        InitializeComponent();

        Title = "WinDots settings";
        AppWindow.IsShownInSwitchers = true;
        try
        {
            AppWindow.Resize(new SizeInt32(760, 720));
        }
        catch (Exception ex)
        {
            ShellLog.Write($"settings window: resize failed: {ex.Message}");
        }

        BuildMonitorList();
        LoadFromStore(_store.Current);
        _ = InitializeStartupAsync();
        Closed += (_, _) => { };
    }

    private static readonly string[] SectionTags =
    {
        "Drawer", "Media", "Appearance", "Monitors", "Privacy", "Diagnostics", "Startup",
    };

    private void OnSectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        DrawerSection.Visibility = Vis(tag == "Drawer");
        MediaSection.Visibility = Vis(tag == "Media");
        AppearanceSection.Visibility = Vis(tag == "Appearance");
        MonitorsSection.Visibility = Vis(tag == "Monitors");
        PrivacySection.Visibility = Vis(tag == "Privacy");
        DiagnosticsSection.Visibility = Vis(tag == "Diagnostics");
        StartupSection.Visibility = Vis(tag == "Startup");
    }

    private static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void BuildMonitorList()
    {
        MonitorList.Children.Clear();
        _monitorChecks.Clear();
        if (_monitors is null)
        {
            return;
        }

        foreach (MonitorInfo monitor in _monitors.Monitors)
        {
            var check = new CheckBox
            {
                Content = monitor.IsPrimary ? $"{monitor.DeviceId} (primary)" : monitor.DeviceId,
                Tag = monitor.DeviceId,
            };
            check.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, $"Enable handle on {monitor.DeviceId}");
            _monitorChecks.Add(check);
            MonitorList.Children.Add(check);
        }
    }

    private void LoadFromStore(CoreSettings s)
    {
        _loading = true;
        try
        {
            DrawerEnabled.IsOn = s.Drawer.Enabled;
            ShowOnHover.IsOn = s.Drawer.ShowOnHover;
            DragThreshold.Value = s.Drawer.DragThresholdPx;
            ToggleShortcut.Text = s.Drawer.ToggleShortcut;
            AutoHideMs.Value = s.Drawer.AutoHideMs;
            HideAfterCommand.IsOn = s.Drawer.HideAfterCommand;
            HideInFullscreen.IsOn = s.Drawer.HideInFullscreen;
            AlwaysOnTop.IsOn = s.Drawer.AlwaysOnTop;
            ValidateShortcut(ToggleShortcut.Text);

            PreferredPlayer.Text = s.Media.PreferredPlayer;
            IgnoredPlayers.Text = string.Join(Environment.NewLine, s.Media.IgnoredPlayers);
            PlayerAliases.Text = string.Join(
                Environment.NewLine,
                s.Media.PlayerAliases.Select(p => $"{p.Key}={p.Value}"));
            TimelineTick.Value = s.Media.TimelineTickMs;

            Theme.SelectedIndex = (int)s.Appearance.Theme;
            Backdrop.SelectedIndex = (int)s.Appearance.Backdrop;
            FontScale.Value = s.Appearance.FontScale;
            BlobDeform.Value = s.Appearance.BlobDeform;
            PaletteSource.SelectedIndex = (int)s.Appearance.PaletteSource;
            FixedAccent.Text = s.Appearance.FixedAccent;
            ReduceMotion.SelectedIndex = (int)s.Appearance.ReduceMotion;
            BackgroundBlobs.IsOn = s.Appearance.BackgroundBlobs;

            MonitorMode.SelectedIndex = (int)s.Monitors.Mode;
            foreach (CheckBox check in _monitorChecks)
            {
                check.IsChecked = s.Monitors.EnabledDeviceIds.Contains((string)check.Tag, StringComparer.Ordinal);
            }

            HandleOffset.Value = s.Monitors.HandleOffsetPercent;
            UpdateMonitorListEnabled();

            HistoryEnabled.IsOn = s.Privacy.HistoryEnabled;
            HistoryRetention.Value = s.Privacy.HistoryRetentionDays;

            LogLevel.SelectedIndex = (int)s.Diagnostics.LogLevel;
            IncludeMediaText.IsOn = s.Diagnostics.IncludeMediaText;

            StatusText.Text = string.Empty;
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnShortcutChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        ValidateShortcut(ToggleShortcut.Text);
    }

    private bool ValidateShortcut(string text)
    {
        if (ShortcutParser.TryParse(text, out Shortcut? parsed))
        {
            ShortcutError.Visibility = Visibility.Collapsed;
            ShortcutError.Text = string.Empty;
            return true;
        }

        ShortcutError.Text = $"'{text}' is not a valid shortcut (e.g. Win+Shift+M).";
        ShortcutError.Visibility = Visibility.Visible;
        return false;
    }

    private void OnMonitorModeChanged(object sender, SelectionChangedEventArgs e) => UpdateMonitorListEnabled();

    private void UpdateMonitorListEnabled()
    {
        // The per-monitor checkboxes only matter in "Selected monitors" (List) mode.
        bool listMode = MonitorMode.SelectedIndex == (int)global::WinDots.Core.Settings.MonitorMode.List;
        foreach (CheckBox check in _monitorChecks)
        {
            check.IsEnabled = listMode;
        }
    }

    private void OnOpenLogFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            string? folder = System.IO.Path.GetDirectoryName(ShellLog.FilePath);
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            System.IO.Directory.CreateDirectory(folder);
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            ShellLog.Write($"settings: open log folder failed: {ex.Message}");
            StatusText.Text = "Could not open the log folder.";
        }
    }

    private async Task InitializeStartupAsync()
    {
        try
        {
            _startupTask = await StartupTask.GetAsync(StartupTaskId);
        }
        catch (Exception ex)
        {
            ShellLog.Write($"settings: StartupTask.GetAsync failed: {ex.Message}");
            StartWithWindows.IsEnabled = false;
            StartupNote.Text = "Start-with-Windows is unavailable (the app must be installed as a package).";
            return;
        }

        ReflectStartupState();
    }

    private void ReflectStartupState()
    {
        if (_startupTask is null)
        {
            return;
        }

        _loading = true;
        try
        {
            switch (_startupTask.State)
            {
                case StartupTaskState.Enabled:
                case StartupTaskState.EnabledByPolicy:
                    StartWithWindows.IsOn = true;
                    StartWithWindows.IsEnabled = _startupTask.State == StartupTaskState.Enabled;
                    StartupNote.Text = _startupTask.State == StartupTaskState.EnabledByPolicy
                        ? "Enabled by system policy; this cannot be changed here."
                        : "WinDots starts automatically when you sign in.";
                    break;

                case StartupTaskState.Disabled:
                    StartWithWindows.IsOn = false;
                    StartWithWindows.IsEnabled = true;
                    StartupNote.Text = "WinDots does not start automatically.";
                    break;

                case StartupTaskState.DisabledByUser:
                    StartWithWindows.IsOn = false;
                    StartWithWindows.IsEnabled = false;
                    StartupNote.Text = "Disabled in Task Manager or Settings > Apps > Startup. Re-enable it there.";
                    break;

                case StartupTaskState.DisabledByPolicy:
                    StartWithWindows.IsOn = false;
                    StartWithWindows.IsEnabled = false;
                    StartupNote.Text = "Disabled by system policy; this cannot be changed here.";
                    break;

                default:
                    StartWithWindows.IsOn = false;
                    StartupNote.Text = string.Empty;
                    break;
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private async void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _startupTask is null)
        {
            return;
        }

        try
        {
            if (StartWithWindows.IsOn)
            {
                StartupTaskState result = await _startupTask.RequestEnableAsync();
                ShellLog.Write($"settings: startup RequestEnableAsync -> {result}");
            }
            else
            {
                _startupTask.Disable();
                ShellLog.Write("settings: startup disabled");
            }
        }
        catch (Exception ex)
        {
            ShellLog.Write($"settings: startup toggle failed: {ex.Message}");
        }

        ReflectStartupState();
    }

    private void OnRevert(object sender, RoutedEventArgs e)
    {
        LoadFromStore(_store.Current);
        StatusText.Text = "Reverted to saved values.";
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        // Inline validation must pass before anything is persisted.
        if (!ValidateShortcut(ToggleShortcut.Text))
        {
            SelectSection("Drawer");
            StatusText.Text = "Fix the toggle shortcut before saving.";
            return;
        }

        string accent = FixedAccent.Text.Trim();
        if (!HexColor.IsMatch(accent))
        {
            SelectSection("Appearance");
            StatusText.Text = "Fixed accent must be a hex colour such as #8FD3C8.";
            return;
        }

        CoreSettings current = _store.Current;
        CoreSettings updated = current with
        {
            Drawer = current.Drawer with
            {
                Enabled = DrawerEnabled.IsOn,
                ShowOnHover = ShowOnHover.IsOn,
                DragThresholdPx = ToInt(DragThreshold.Value, current.Drawer.DragThresholdPx),
                ToggleShortcut = ToggleShortcut.Text.Trim(),
                AutoHideMs = ToInt(AutoHideMs.Value, current.Drawer.AutoHideMs),
                HideAfterCommand = HideAfterCommand.IsOn,
                HideInFullscreen = HideInFullscreen.IsOn,
                AlwaysOnTop = AlwaysOnTop.IsOn,
            },
            Media = current.Media with
            {
                PreferredPlayer = PreferredPlayer.Text.Trim(),
                IgnoredPlayers = ParseLines(IgnoredPlayers.Text),
                PlayerAliases = ParseAliases(PlayerAliases.Text),
                TimelineTickMs = ToInt(TimelineTick.Value, current.Media.TimelineTickMs),
            },
            Appearance = current.Appearance with
            {
                Theme = (AppearanceTheme)Math.Max(0, Theme.SelectedIndex),
                Backdrop = (Backdrop)Math.Max(0, Backdrop.SelectedIndex),
                FontScale = ToDouble(FontScale.Value, current.Appearance.FontScale),
                BlobDeform = ToDouble(BlobDeform.Value, current.Appearance.BlobDeform),
                PaletteSource = (PaletteSource)Math.Max(0, PaletteSource.SelectedIndex),
                FixedAccent = accent,
                ReduceMotion = (ReduceMotion)Math.Max(0, ReduceMotion.SelectedIndex),
                BackgroundBlobs = BackgroundBlobs.IsOn,
            },
            Monitors = current.Monitors with
            {
                Mode = (MonitorMode)Math.Max(0, MonitorMode.SelectedIndex),
                EnabledDeviceIds = _monitorChecks
                    .Where(c => c.IsChecked == true)
                    .Select(c => (string)c.Tag)
                    .ToArray(),
                HandleOffsetPercent = ToInt(HandleOffset.Value, current.Monitors.HandleOffsetPercent),
            },
            Privacy = current.Privacy with
            {
                HistoryRetentionDays = ToInt(HistoryRetention.Value, current.Privacy.HistoryRetentionDays),
            },
            Diagnostics = current.Diagnostics with
            {
                LogLevel = (LogLevel)Math.Max(0, LogLevel.SelectedIndex),
                IncludeMediaText = IncludeMediaText.IsOn,
            },
        };

        SaveButton.IsEnabled = false;
        try
        {
            await _store.SaveAsync(updated, CancellationToken.None);
            StatusText.Text = $"Saved at {DateTime.Now:HH:mm:ss}.";
            ShellLog.Write("settings: saved from settings window");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Save failed; see the log.";
            ShellLog.Write($"settings: save failed: {ex.Message}");
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void SelectSection(string tag)
    {
        foreach (object raw in Nav.MenuItems)
        {
            if (raw is NavigationViewItem item && item.Tag is string t && t == tag)
            {
                Nav.SelectedItem = item;
                return;
            }
        }
    }

    private static int ToInt(double value, int fallback) =>
        double.IsNaN(value) ? fallback : (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static double ToDouble(double value, double fallback) =>
        double.IsNaN(value) ? fallback : value;

    private static IReadOnlyList<string> ParseLines(string text) =>
        text.Split('\n', '\r')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

    private static IReadOnlyDictionary<string, string> ParseAliases(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in ParseLines(text))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();
            if (key.Length > 0 && value.Length > 0)
            {
                map[key] = value;
            }
        }

        return map;
    }
}
