using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinDots.App.Media.Controls;

namespace WinDots.App.Media;

/// <summary>
/// The Media tab surface: a single-item tab strip over a three-column body (blob artwork with a dotted progress ring,
/// metadata + seek + transport, and a lyrics slot with the player chooser). All state comes from a
/// <see cref="MediaViewModel"/> bound one-way; control events are routed to the view-model's fallible commands.
/// Keyboard handling follows _docs/03-ux-interaction-spec.md.
/// </summary>
public sealed partial class MediaPage : UserControl
{
    private MediaViewModel? _viewModel;

    public MediaPage()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    /// <summary>The bound presentation model. Set once via <see cref="Initialize"/> before the drawer is shown.</summary>
    public MediaViewModel ViewModel => _viewModel!;

    /// <summary>Wires the page to its view-model and refreshes all bindings. Idempotent.</summary>
    public void Initialize(MediaViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        Bindings.Update();
    }

    /// <summary>Moves initial focus to the play/pause button (called when the drawer opens).</summary>
    public void FocusDefault() => Transport.FocusPlayPause();

    // --- x:Bind helper functions ---

    private Visibility Shown(bool isEmpty) => isEmpty ? Visibility.Collapsed : Visibility.Visible;

    // The dotted ring shows in the empty state as an all-track ring (DottedRingLayout.ElapsedDots(null,..) is 0)
    // and whenever a duration is known. It is hidden only for an active unknown-duration livestream (A3).
    private Visibility RingVisible(bool isEmpty, TimeSpan? duration) =>
        isEmpty || duration is not null ? Visibility.Visible : Visibility.Collapsed;

    private Visibility HasText(string? text) =>
        string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    private Visibility Vis(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    private string Percent(int level) => level.ToString(System.Globalization.CultureInfo.CurrentCulture) + "%";

    private string MuteName(bool muted) => muted ? "Unmute" : "Mute";

    // Segoe Fluent Icons: E74F mute, E992 (silent), E993 low, E994 medium, E995 high.
    private string VolumeGlyph(bool muted, int level) => muted ? "" : level switch
    {
        0 => "",
        < 34 => "",
        < 67 => "",
        _ => "",
    };

    // --- Volume row ---

    private void OnMuteClick(object sender, RoutedEventArgs e) => _ = ViewModel.ToggleMuteAsync();

    private void OnVolumeSliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        // The slider also moves when the view-model pushes a new level; only user-originated changes go back.
        if (_viewModel is null)
        {
            return;
        }

        int value = (int)Math.Round(e.NewValue);
        if (value != ViewModel.VolumeLevel)
        {
            ViewModel.SetVolume(value);
        }
    }

    private void OnVolumeWheel(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        if (delta != 0)
        {
            ViewModel.NudgeVolume(delta > 0 ? 1 : -1);
            e.Handled = true;
        }
    }

    private async void OnLyricsWhyVolumeHidden(object? sender, EventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = ViewModel.VolumeAvailable ? "Volume is available" : "Why is volume hidden?",
            Content = ViewModel.VolumeExplanation,
            CloseButtonText = "Close",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    // --- Control event routing ---

    private void OnShuffleRequested(object? sender, EventArgs e) => _ = ViewModel.ToggleShuffleAsync();

    private void OnPreviousRequested(object? sender, EventArgs e) => _ = ViewModel.PreviousAsync();

    private void OnPlayPauseRequested(object? sender, EventArgs e) => _ = ViewModel.PlayPauseAsync();

    private void OnNextRequested(object? sender, EventArgs e) => _ = ViewModel.NextAsync();

    private void OnRepeatRequested(object? sender, EventArgs e) => _ = ViewModel.CycleRepeatAsync();

    private void OnSeekRequested(object? sender, TimeSpan target) => _ = ViewModel.SeekAsync(target);

    private void OnPlayerSelected(object? sender, string? id) => ViewModel.SelectPlayer(id);

    private void OnLyricsCopy(object? sender, EventArgs e)
    {
        // Copy/open/settings land in later milestones; the menu items exist for parity with the reference.
    }

    private void OnLyricsOpen(object? sender, EventArgs e)
    {
    }

    private void OnLyricsSettings(object? sender, EventArgs e)
    {
    }

    // --- Keyboard map (_docs/03-ux-interaction-spec.md) ---

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        // Seek nudges (+/-5 s, Shift +/-30 s) are handled inside SeekBar itself, because a focused Slider
        // consumes Left/Right in its own class handler before this bubbling handler would run.
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;

        switch (e.Key)
        {
            case global::Windows.System.VirtualKey.Left when IsWithin(focused, Transport):
                _ = ViewModel.PreviousAsync();
                e.Handled = true;
                break;

            case global::Windows.System.VirtualKey.Right when IsWithin(focused, Transport):
                _ = ViewModel.NextAsync();
                e.Handled = true;
                break;

            case global::Windows.System.VirtualKey.M:
                _ = ViewModel.ToggleMuteAsync();
                e.Handled = true;
                break;

            case global::Windows.System.VirtualKey.P:
                Chooser.OpenFlyout();
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    private static bool IsWithin(DependencyObject? node, DependencyObject ancestor)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }

            node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node);
        }

        return false;
    }
}
