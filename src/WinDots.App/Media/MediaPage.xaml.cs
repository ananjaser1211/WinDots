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
                // Reserved for mute; a no-op until the volume milestone. Handled so it never leaks elsewhere.
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
