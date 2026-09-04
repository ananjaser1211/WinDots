using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinDots.App.Media.Controls;

/// <summary>
/// The lyrics slot: a header with an overflow menu and a body that either lists lyric lines or, when there are
/// none, shows the "No lyrics found" placeholder matching the static reference.
/// </summary>
public sealed partial class LyricsPanel : UserControl
{
    public static readonly DependencyProperty LinesProperty = DependencyProperty.Register(
        nameof(Lines),
        typeof(IReadOnlyList<string>),
        typeof(LyricsPanel),
        new PropertyMetadata(null, OnLinesChanged));

    public LyricsPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateLines();
    }

    /// <summary>Raised when "Copy track info" is chosen.</summary>
    public event EventHandler? CopyRequested;

    /// <summary>Raised when "Open in player" is chosen.</summary>
    public event EventHandler? OpenInPlayerRequested;

    /// <summary>Raised when "Settings" is chosen.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>The lyric lines; empty or null shows the placeholder.</summary>
    public IReadOnlyList<string>? Lines
    {
        get => (IReadOnlyList<string>?)GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    private static void OnLinesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((LyricsPanel)d).UpdateLines();
    }

    private void UpdateLines()
    {
        IReadOnlyList<string>? lines = Lines;
        bool hasLines = lines is { Count: > 0 };

        LinesHost.ItemsSource = lines;
        LyricsScroller.Visibility = hasLines ? Visibility.Visible : Visibility.Collapsed;
        Placeholder.Visibility = hasLines ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e) => CopyRequested?.Invoke(this, EventArgs.Empty);

    private void OnOpenInPlayerClick(object sender, RoutedEventArgs e) => OpenInPlayerRequested?.Invoke(this, EventArgs.Empty);

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
}
