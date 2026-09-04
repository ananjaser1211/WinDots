using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinDots.Core.Settings;

namespace WinDots.App.Shell.Controls;

/// <summary>
/// The drawer's top tab strip: four tabs (Dashboard, Media, Performance, Weather), each a Segoe Fluent glyph over a
/// label, matching Widgets.png. The selected tab is accented with an underline; the rest are muted. Selection styling
/// is applied in code with <see cref="DependencyObject.ClearValue"/> so the muted foreground stays theme-aware.
/// Drag-to-close is handled by the host on this control; the tabs only raise <see cref="SelectionChanged"/>.
/// </summary>
public sealed partial class DrawerTabStrip : UserControl
{
    /// <summary>The accent brush for the selected tab's glyph, label and underline (the media palette accent).</summary>
    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(Brush), typeof(DrawerTabStrip), new PropertyMetadata(null, OnAccentBrushChanged));

    private DashboardTab _selected = DashboardTab.Media;

    public DrawerTabStrip()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplySelectionVisuals();
    }

    /// <summary>Raised when the user activates a tab (click or keyboard). Not raised by <see cref="Selected"/> setter.</summary>
    public event EventHandler<DashboardTab>? SelectionChanged;

    public Brush? AccentBrush
    {
        get => (Brush?)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    /// <summary>The currently selected tab. Setting it updates the visuals but does not raise <see cref="SelectionChanged"/>.</summary>
    public DashboardTab Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            ApplySelectionVisuals();
        }
    }

    /// <summary>
    /// Moves keyboard focus to the currently selected tab's button. The UserControl itself is <c>IsTabStop=False</c>,
    /// so focusing it is a no-op; a focused descendant is what lets Root's bubbling KeyDown (Escape / Ctrl+Tab) fire.
    /// Returns true when focus was actually taken.
    /// </summary>
    public bool FocusSelected()
    {
        Button button = _selected switch
        {
            DashboardTab.Dashboard => DashboardTabButton,
            DashboardTab.Media => MediaTabButton,
            DashboardTab.Performance => PerformanceTabButton,
            DashboardTab.Weather => WeatherTabButton,
            _ => MediaTabButton,
        };
        return button.Focus(FocusState.Programmatic);
    }

    /// <summary>Selects the next tab in enumeration order, wrapping; raises <see cref="SelectionChanged"/>.</summary>
    public void CycleNext(bool backward)
    {
        int count = Enum.GetValues<DashboardTab>().Length;
        int next = (((int)_selected + (backward ? -1 : 1)) % count + count) % count;
        Activate((DashboardTab)next);
    }

    private static void OnAccentBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DrawerTabStrip)d).ApplySelectionVisuals();

    private void OnTabClicked(object sender, RoutedEventArgs e)
    {
        DashboardTab tab = sender switch
        {
            _ when ReferenceEquals(sender, DashboardTabButton) => DashboardTab.Dashboard,
            _ when ReferenceEquals(sender, MediaTabButton) => DashboardTab.Media,
            _ when ReferenceEquals(sender, PerformanceTabButton) => DashboardTab.Performance,
            _ when ReferenceEquals(sender, WeatherTabButton) => DashboardTab.Weather,
            _ => _selected,
        };
        Activate(tab);
    }

    private void Activate(DashboardTab tab)
    {
        if (tab == _selected)
        {
            return;
        }

        _selected = tab;
        ApplySelectionVisuals();
        SelectionChanged?.Invoke(this, tab);
    }

    // Selected: accent glyph + label + visible underline. Unselected: ClearValue restores the muted ThemeResource.
    private void ApplySelectionVisuals()
    {
        Brush accent = AccentBrush
            ?? (Application.Current.Resources.TryGetValue("WdAccentBrush", out object? res) && res is Brush b
                ? b
                : new SolidColorBrush(Microsoft.UI.Colors.Teal));

        SetTab(DashboardTab.Dashboard, DashboardIcon, DashboardLabel, DashboardUnderline, accent);
        SetTab(DashboardTab.Media, MediaIcon, MediaLabel, MediaUnderline, accent);
        SetTab(DashboardTab.Performance, PerformanceIcon, PerformanceLabel, PerformanceUnderline, accent);
        SetTab(DashboardTab.Weather, WeatherIcon, WeatherLabel, WeatherUnderline, accent);
    }

    private void SetTab(DashboardTab tab, FontIcon icon, TextBlock label, FrameworkElement underline, Brush accent)
    {
        if (tab == _selected)
        {
            icon.Foreground = accent;
            label.Foreground = accent;
            underline.Visibility = Visibility.Visible;
        }
        else
        {
            icon.ClearValue(IconElement.ForegroundProperty);
            label.ClearValue(TextBlock.ForegroundProperty);
            underline.Visibility = Visibility.Collapsed;
        }
    }
}
