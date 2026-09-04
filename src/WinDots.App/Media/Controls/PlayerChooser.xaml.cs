using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinDots.App.Media.Controls;

/// <summary>
/// One item in the <see cref="PlayerChooser"/> flyout.
/// </summary>
/// <param name="Id">Stable session identifier.</param>
/// <param name="Label">Display name shown in the menu.</param>
/// <param name="StateText">Secondary state text (e.g. "Playing").</param>
/// <param name="IsActive">Whether this is the currently active player.</param>
public sealed record PlayerChooserItem(string Id, string Label, string StateText, bool IsActive);

/// <summary>
/// A pill button showing the active player with a chevron that opens a menu of the available players plus an
/// "Automatic" entry. Selecting a player raises <see cref="PlayerSelected"/>; "Automatic" raises it with null.
/// </summary>
public sealed partial class PlayerChooser : UserControl
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items),
        typeof(IReadOnlyList<PlayerChooserItem>),
        typeof(PlayerChooser),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ActiveLabelProperty = DependencyProperty.Register(
        nameof(ActiveLabel),
        typeof(string),
        typeof(PlayerChooser),
        new PropertyMetadata("Automatic", OnActiveLabelChanged));

    public PlayerChooser()
    {
        InitializeComponent();
    }

    /// <summary>Raised when a player is chosen; null means "Automatic".</summary>
    public event EventHandler<string?>? PlayerSelected;

    /// <summary>The players available for selection.</summary>
    public IReadOnlyList<PlayerChooserItem>? Items
    {
        get => (IReadOnlyList<PlayerChooserItem>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>The label of the active player, shown on the pill.</summary>
    public string ActiveLabel
    {
        get => (string)GetValue(ActiveLabelProperty);
        set => SetValue(ActiveLabelProperty, value);
    }

    /// <summary>Opens the player menu (keyboard "P" shortcut).</summary>
    public void OpenFlyout() => Menu.ShowAt(ChooserButton);

    private static void OnActiveLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chooser = (PlayerChooser)d;
        chooser.LabelText.Text = e.NewValue as string ?? "Automatic";
    }

    private void OnMenuOpening(object? sender, object e)
    {
        Menu.Items.Clear();

        IReadOnlyList<PlayerChooserItem>? items = Items;
        if (items is not null)
        {
            foreach (PlayerChooserItem item in items)
            {
                var menuItem = new MenuFlyoutItem
                {
                    Text = string.IsNullOrEmpty(item.StateText) ? item.Label : $"{item.Label} — {item.StateText}",
                    Tag = item.Id,
                    Icon = item.IsActive ? new FontIcon { Glyph = "" } : null,
                };
                menuItem.Click += OnPlayerItemClick;
                Menu.Items.Add(menuItem);
            }
        }

        Menu.Items.Add(new MenuFlyoutSeparator());

        var automatic = new MenuFlyoutItem { Text = "Automatic" };
        automatic.Click += OnAutomaticClick;
        Menu.Items.Add(automatic);
    }

    private void OnPlayerItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string id })
        {
            PlayerSelected?.Invoke(this, id);
        }
    }

    private void OnAutomaticClick(object sender, RoutedEventArgs e) => PlayerSelected?.Invoke(this, null);
}
