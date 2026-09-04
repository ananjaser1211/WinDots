using Microsoft.UI.Xaml.Controls;

namespace WinDots.App.Shell.Pages;

/// <summary>
/// Placeholder surface for the Weather tab. Weather is a network feature, so this page never fetches anything; it only
/// reports whether the user has granted consent (<c>WeatherSettings.ConsentGranted</c>) until the real UI is built.
/// </summary>
public sealed partial class WeatherPage : UserControl
{
    public WeatherPage() => InitializeComponent();

    /// <summary>Reflects the current consent state in the affordance line. No network access.</summary>
    public void SetConsent(bool consentGranted) =>
        ConsentLine.Text = consentGranted
            ? "Location weather is enabled. The forecast view will appear here."
            : "Weather is off. Grant consent in Settings to fetch the forecast for your location.";
}
