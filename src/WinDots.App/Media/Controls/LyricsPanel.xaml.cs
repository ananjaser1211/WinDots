using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinDots.App.Media;

namespace WinDots.App.Media.Controls;

/// <summary>
/// One rendered lyric line. Its visual properties (size, opacity, colour) are mutated by <see cref="LyricsPanel"/> as
/// the current line advances, so only the two affected lines re-render rather than the whole list.
/// </summary>
public sealed class LyricLineView : INotifyPropertyChanged
{
    private double _fontSize = 14;
    private double _opacity = 0.85;
    private Brush? _foreground;

    public LyricLineView(string text) => Text = text;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Text { get; }

    public double FontSize { get => _fontSize; set => Set(ref _fontSize, value); }

    public double Opacity { get => _opacity; set => Set(ref _opacity, value); }

    public Brush? Foreground { get => _foreground; set => Set(ref _foreground, value); }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

/// <summary>
/// The lyrics slot: a header with an overflow menu and a body that shows an off prompt, a loading spinner, the lines
/// (synced lines highlight the current line in the palette accent and auto-scroll to keep it centred; plain lines
/// scroll manually), or a "No lyrics found" placeholder. Attribution ("Lyrics from LRCLIB") sits at the bottom.
/// See _docs/10-enhancement-plan.md (E3).
/// </summary>
public sealed partial class LyricsPanel : UserControl
{
    private const double CurrentFontSize = 18;
    private const double OtherFontSize = 14;

    public static readonly DependencyProperty LinesProperty = DependencyProperty.Register(
        nameof(Lines), typeof(IReadOnlyList<string>), typeof(LyricsPanel), new PropertyMetadata(null, OnLinesChanged));

    public static readonly DependencyProperty CurrentIndexProperty = DependencyProperty.Register(
        nameof(CurrentIndex), typeof(int), typeof(LyricsPanel), new PropertyMetadata(-1, OnCurrentIndexChanged));

    public static readonly DependencyProperty SyncedProperty = DependencyProperty.Register(
        nameof(Synced), typeof(bool), typeof(LyricsPanel), new PropertyMetadata(false, OnLinesChanged));

    public static readonly DependencyProperty AttributionTextProperty = DependencyProperty.Register(
        nameof(AttributionText), typeof(string), typeof(LyricsPanel), new PropertyMetadata(string.Empty, OnAttributionChanged));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(LyricsState), typeof(LyricsPanel), new PropertyMetadata(LyricsState.Off, OnStateChanged));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(Brush), typeof(LyricsPanel), new PropertyMetadata(null, OnCurrentIndexChanged));

    private readonly List<LyricLineView> _items = new();
    private int _appliedIndex = -1;
    private bool _reduceMotion;

    public LyricsPanel()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Rebuild();
            UpdateVisibility();
        };
    }

    /// <summary>Raised when "Copy track info" is chosen.</summary>
    public event EventHandler? CopyRequested;

    /// <summary>Raised when "Open in player" is chosen.</summary>
    public event EventHandler? OpenInPlayerRequested;

    /// <summary>Raised when "Settings" is chosen.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Raised when the user asks why the volume row is hidden.</summary>
    public event EventHandler? WhyVolumeHiddenRequested;

    /// <summary>Raised when the user asks to enable lyrics.</summary>
    public event EventHandler? EnableLyricsRequested;

    /// <summary>Raised to nudge the lyrics offset later (+) — later text, by 500 ms.</summary>
    public event EventHandler? OffsetIncreaseRequested;

    /// <summary>Raised to nudge the lyrics offset earlier (-), by 500 ms.</summary>
    public event EventHandler? OffsetDecreaseRequested;

    /// <summary>Raised to reset the lyrics offset to the default.</summary>
    public event EventHandler? OffsetResetRequested;

    /// <summary>The lyric lines' text.</summary>
    public IReadOnlyList<string>? Lines
    {
        get => (IReadOnlyList<string>?)GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    /// <summary>The current synced line index, or -1.</summary>
    public int CurrentIndex
    {
        get => (int)GetValue(CurrentIndexProperty);
        set => SetValue(CurrentIndexProperty, value);
    }

    /// <summary>Whether the lines carry timestamps (enables highlight + auto-scroll).</summary>
    public bool Synced
    {
        get => (bool)GetValue(SyncedProperty);
        set => SetValue(SyncedProperty, value);
    }

    /// <summary>The attribution caption text.</summary>
    public string AttributionText
    {
        get => (string)GetValue(AttributionTextProperty);
        set => SetValue(AttributionTextProperty, value);
    }

    /// <summary>The lyrics slot state.</summary>
    public LyricsState State
    {
        get => (LyricsState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>The palette accent brush used for the current line.</summary>
    public Brush? AccentBrush
    {
        get => (Brush?)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    /// <summary>Sets whether auto-scroll should animate. Called by the page on accessibility changes.</summary>
    public void SetReduceMotion(bool reduceMotion) => _reduceMotion = reduceMotion;

    private static void OnLinesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var panel = (LyricsPanel)d;
        panel.Rebuild();
        panel.UpdateVisibility();
    }

    private static void OnCurrentIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((LyricsPanel)d).ApplyCurrent();

    private static void OnAttributionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((LyricsPanel)d).UpdateVisibility();

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((LyricsPanel)d).UpdateVisibility();

    private void Rebuild()
    {
        if (!IsLoaded)
        {
            return;
        }

        _items.Clear();
        _appliedIndex = -1;
        IReadOnlyList<string>? lines = Lines;
        if (lines is not null)
        {
            Brush normal = OnSurfaceBrush();
            foreach (string line in lines)
            {
                _items.Add(new LyricLineView(line) { Foreground = normal, FontSize = OtherFontSize, Opacity = 0.85 });
            }
        }

        LinesHost.ItemsSource = null;
        LinesHost.ItemsSource = _items;
        ApplyCurrent();
    }

    private void ApplyCurrent()
    {
        if (!IsLoaded || _items.Count == 0 || !Synced)
        {
            return;
        }

        int index = CurrentIndex;
        Brush accent = AccentBrush ?? OnSurfaceBrush();
        Brush muted = MutedBrush();
        Brush normal = OnSurfaceBrush();

        for (int i = 0; i < _items.Count; i++)
        {
            LyricLineView item = _items[i];
            if (i == index)
            {
                item.Foreground = accent;
                item.FontSize = CurrentFontSize;
                item.Opacity = 1.0;
            }
            else if (i < index)
            {
                item.Foreground = muted;
                item.FontSize = OtherFontSize;
                item.Opacity = 0.5;
            }
            else
            {
                item.Foreground = normal;
                item.FontSize = OtherFontSize;
                item.Opacity = 0.85;
            }
        }

        if (index >= 0 && index != _appliedIndex)
        {
            _appliedIndex = index;
            ScrollToCurrent(index);
        }
    }

    private void ScrollToCurrent(int index)
    {
        if (LinesHost.ContainerFromIndex(index) is not FrameworkElement container)
        {
            // Container not realised yet; try again after layout.
            DispatcherQueue?.TryEnqueue(() =>
            {
                if (LinesHost.ContainerFromIndex(index) is FrameworkElement fe)
                {
                    CenterOn(fe);
                }
            });
            return;
        }

        CenterOn(container);
    }

    private void CenterOn(FrameworkElement container)
    {
        try
        {
            GeneralTransform transform = container.TransformToVisual(LinesHost);
            global::Windows.Foundation.Point pos = transform.TransformPoint(new global::Windows.Foundation.Point(0, 0));
            double target = pos.Y - (LyricsScroller.ViewportHeight / 2) + (container.ActualHeight / 2);
            target = Math.Max(0, target);
            LyricsScroller.ChangeView(null, target, null, disableAnimation: _reduceMotion);
        }
        catch (Exception)
        {
            // Transform can fail transiently during layout; the next tick recovers.
        }
    }

    private void UpdateVisibility()
    {
        if (!IsLoaded)
        {
            return;
        }

        bool hasLines = _items.Count > 0;
        LyricsState state = State;

        OffPrompt.Visibility = state == LyricsState.Off ? Visibility.Visible : Visibility.Collapsed;
        LoadingPanel.Visibility = state == LyricsState.Loading ? Visibility.Visible : Visibility.Collapsed;
        bool showLines = state == LyricsState.Found && hasLines;
        LyricsScroller.Visibility = showLines ? Visibility.Visible : Visibility.Collapsed;
        Placeholder.Visibility = state == LyricsState.NotFound || (state == LyricsState.Found && !hasLines)
            ? Visibility.Visible
            : Visibility.Collapsed;

        Attribution.Text = AttributionText ?? string.Empty;
        Attribution.Visibility = showLines && !string.IsNullOrEmpty(AttributionText)
            ? Visibility.Visible
            : Visibility.Collapsed;

        EnableLyricsItem.Visibility = state == LyricsState.Off ? Visibility.Visible : Visibility.Collapsed;
        bool offsetsUsable = showLines && Synced;
        OffsetPlusItem.IsEnabled = offsetsUsable;
        OffsetMinusItem.IsEnabled = offsetsUsable;
        OffsetResetItem.IsEnabled = offsetsUsable;
    }

    private static Brush OnSurfaceBrush() => ThemeBrush("WdOnSurfaceBrush");

    private static Brush MutedBrush() => ThemeBrush("WdOnSurfaceMutedBrush");

    private static Brush ThemeBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out object? value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);

    private void OnCopyClick(object sender, RoutedEventArgs e) => CopyRequested?.Invoke(this, EventArgs.Empty);

    private void OnOpenInPlayerClick(object sender, RoutedEventArgs e) => OpenInPlayerRequested?.Invoke(this, EventArgs.Empty);

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnWhyVolumeHiddenClick(object sender, RoutedEventArgs e) => WhyVolumeHiddenRequested?.Invoke(this, EventArgs.Empty);

    private void OnEnableLyricsClick(object sender, RoutedEventArgs e) => EnableLyricsRequested?.Invoke(this, EventArgs.Empty);

    private void OnOffsetPlusClick(object sender, RoutedEventArgs e) => OffsetIncreaseRequested?.Invoke(this, EventArgs.Empty);

    private void OnOffsetMinusClick(object sender, RoutedEventArgs e) => OffsetDecreaseRequested?.Invoke(this, EventArgs.Empty);

    private void OnOffsetResetClick(object sender, RoutedEventArgs e) => OffsetResetRequested?.Invoke(this, EventArgs.Empty);
}
