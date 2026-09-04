using System.Text.Json;
using CoreSettings = WinDots.Core.Settings.Settings;
using WinDots.Core.Settings;

namespace WinDots.Core.Tests.Dashboard;

public class DashboardSettingsTests
{
    [Fact]
    public void SelectedTabDefaultsToMedia()
    {
        Assert.Equal(DashboardTab.Media, new CoreSettings().SelectedTab);
    }

    [Fact]
    public void WeatherConsentDefaultsOff()
    {
        Assert.False(new WeatherSettings().ConsentGranted);
    }

    [Fact]
    public void SampleIntervalDefaultIsOneSecond()
    {
        Assert.Equal(1000, new PerformanceSettings().SampleIntervalMs);
        Assert.Equal(1000, new PerformanceSettings().ClampedSampleIntervalMs);
    }

    [Theory]
    [InlineData(0, PerformanceSettings.MinSampleIntervalMs)]
    [InlineData(100, PerformanceSettings.MinSampleIntervalMs)]
    [InlineData(500, 500)]
    [InlineData(60000, PerformanceSettings.MaxSampleIntervalMs)]
    public void SampleIntervalIsClamped(int configured, int expected)
    {
        Assert.Equal(expected, new PerformanceSettings { SampleIntervalMs = configured }.ClampedSampleIntervalMs);
    }

    [Fact]
    public void SelectedTabRoundTripsAsCamelCaseString()
    {
        var settings = new CoreSettings { SelectedTab = DashboardTab.Dashboard };
        string json = JsonSerializer.Serialize(settings, CoreSettings.JsonOptions);
        Assert.Contains("\"selectedTab\": \"dashboard\"", json, StringComparison.Ordinal);

        CoreSettings? back = JsonSerializer.Deserialize<CoreSettings>(json, CoreSettings.JsonOptions);
        Assert.NotNull(back);
        Assert.Equal(DashboardTab.Dashboard, back!.SelectedTab);
    }

    [Fact]
    public void ClampedSampleIntervalIsNotSerialized()
    {
        string json = JsonSerializer.Serialize(new CoreSettings(), CoreSettings.JsonOptions);
        Assert.DoesNotContain("clampedSampleIntervalMs", json, StringComparison.OrdinalIgnoreCase);
    }
}
