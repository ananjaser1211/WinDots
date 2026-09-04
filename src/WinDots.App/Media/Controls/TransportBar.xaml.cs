using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinDots.Core.Media;

namespace WinDots.App.Media.Controls;

/// <summary>
/// The transport row: shuffle, previous, play/pause pill, next, repeat. Each button is enabled only when the
/// matching <see cref="Capabilities"/> flag is advertised and dims when unavailable. Commands are surfaced
/// as events; the control performs no media work itself.
/// </summary>
public sealed partial class TransportBar : UserControl
{
    public static readonly DependencyProperty CapabilitiesProperty = DependencyProperty.Register(
        nameof(Capabilities),
        typeof(Capabilities),
        typeof(TransportBar),
        new PropertyMetadata(Capabilities.None, OnStateChanged));

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying),
        typeof(bool),
        typeof(TransportBar),
        new PropertyMetadata(false, OnStateChanged));

    public static readonly DependencyProperty IsShuffleOnProperty = DependencyProperty.Register(
        nameof(IsShuffleOn),
        typeof(bool?),
        typeof(TransportBar),
        new PropertyMetadata(null, OnStateChanged));

    public static readonly DependencyProperty RepeatModeProperty = DependencyProperty.Register(
        nameof(RepeatMode),
        typeof(RepeatMode?),
        typeof(TransportBar),
        new PropertyMetadata(null, OnStateChanged));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush),
        typeof(Brush),
        typeof(TransportBar),
        new PropertyMetadata(null, OnStateChanged));

    public static readonly DependencyProperty OnAccentBrushProperty = DependencyProperty.Register(
        nameof(OnAccentBrush),
        typeof(Brush),
        typeof(TransportBar),
        new PropertyMetadata(null));

    public TransportBar()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            // Fall back to the static accent tokens until the view-model's animated brushes are bound.
            AccentBrush ??= ResolveBrush("WdAccentBrush");
            OnAccentBrush ??= ResolveBrush("WdOnAccentBrush");
            UpdateState();
        };
    }

    /// <summary>Raised when the shuffle button is invoked.</summary>
    public event EventHandler? ShuffleRequested;

    /// <summary>Raised when the previous button is invoked.</summary>
    public event EventHandler? PreviousRequested;

    /// <summary>Raised when the play/pause button is invoked.</summary>
    public event EventHandler? PlayPauseRequested;

    /// <summary>Raised when the next button is invoked.</summary>
    public event EventHandler? NextRequested;

    /// <summary>Raised when the repeat button is invoked.</summary>
    public event EventHandler? RepeatRequested;

    /// <summary>The advertised capability set; gates each button's enabled state.</summary>
    public Capabilities Capabilities
    {
        get => (Capabilities)GetValue(CapabilitiesProperty);
        set => SetValue(CapabilitiesProperty, value);
    }

    /// <summary>Whether playback is active; selects the pause glyph and accessible name.</summary>
    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    /// <summary>Whether shuffle is on; null when unknown.</summary>
    public bool? IsShuffleOn
    {
        get => (bool?)GetValue(IsShuffleOnProperty);
        set => SetValue(IsShuffleOnProperty, value);
    }

    /// <summary>The repeat mode; null when unknown.</summary>
    public RepeatMode? RepeatMode
    {
        get => (RepeatMode?)GetValue(RepeatModeProperty);
        set => SetValue(RepeatModeProperty, value);
    }

    /// <summary>The dynamic accent brush (artwork palette) for the play pill and active-state glyphs.</summary>
    public Brush? AccentBrush
    {
        get => (Brush?)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    /// <summary>The on-accent brush used for the play pill glyph.</summary>
    public Brush? OnAccentBrush
    {
        get => (Brush?)GetValue(OnAccentBrushProperty);
        set => SetValue(OnAccentBrushProperty, value);
    }

    /// <summary>Moves keyboard focus to the play/pause button (initial drawer focus).</summary>
    public void FocusPlayPause() => PlayPauseButton.Focus(FocusState.Programmatic);

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((TransportBar)d).UpdateState();
    }

    private void UpdateState()
    {
        Capabilities caps = Capabilities;

        bool canPlayPause = caps.HasFlag(Capabilities.PlayPause)
            || caps.HasFlag(Capabilities.Play)
            || caps.HasFlag(Capabilities.Pause);

        SetEnabled(ShuffleButton, caps.HasFlag(Capabilities.Shuffle));
        SetEnabled(PreviousButton, caps.HasFlag(Capabilities.Previous));
        SetEnabled(PlayPauseButton, canPlayPause);
        SetEnabled(NextButton, caps.HasFlag(Capabilities.Next));
        SetEnabled(RepeatButton, caps.HasFlag(Capabilities.Repeat));

        PlayPauseGlyph.Glyph = IsPlaying ? "" : "";
        AutomationProperties.SetName(PlayPauseButton, IsPlaying ? "Pause" : "Play");

        Brush accent = AccentBrush ?? ResolveBrush("WdAccentBrush");
        Brush onSurface = ResolveBrush("WdOnSurfaceBrush");

        bool shuffleActive = IsShuffleOn == true;
        ShuffleGlyph.Foreground = shuffleActive ? accent : onSurface;
        ShuffleDot.Visibility = shuffleActive ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(ShuffleButton, shuffleActive ? "Shuffle: on" : "Shuffle: off");

        RepeatMode? repeat = RepeatMode;
        bool repeatActive = repeat is not null and not WinDots.Core.Media.RepeatMode.None;
        RepeatGlyph.Foreground = repeatActive ? accent : onSurface;
        RepeatDot.Visibility = repeatActive ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(RepeatButton, repeat switch
        {
            WinDots.Core.Media.RepeatMode.Track => "Repeat: track",
            WinDots.Core.Media.RepeatMode.List => "Repeat: list",
            _ => "Repeat: off",
        });
        RepeatGlyph.Glyph = repeat == WinDots.Core.Media.RepeatMode.Track ? "" : "";
    }

    private static void SetEnabled(Control button, bool enabled)
    {
        button.IsEnabled = enabled;
        button.Opacity = enabled ? 1.0 : 0.4;
    }

    private Brush ResolveBrush(string key)
    {
        if (Resources.TryGetValue(key, out object? local) && local is Brush localBrush)
        {
            return localBrush;
        }

        if (Application.Current.Resources.TryGetValue(key, out object? value) && value is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    private void OnShuffleClick(object sender, RoutedEventArgs e) => ShuffleRequested?.Invoke(this, EventArgs.Empty);

    private void OnPreviousClick(object sender, RoutedEventArgs e) => PreviousRequested?.Invoke(this, EventArgs.Empty);

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => PlayPauseRequested?.Invoke(this, EventArgs.Empty);

    private void OnNextClick(object sender, RoutedEventArgs e) => NextRequested?.Invoke(this, EventArgs.Empty);

    private void OnRepeatClick(object sender, RoutedEventArgs e) => RepeatRequested?.Invoke(this, EventArgs.Empty);
}
