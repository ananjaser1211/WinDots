using WinDots.Core.Settings;
using WinDots.Core.Visualiser;

namespace WinDots.Core.Tests.Visualiser;

public class VisualiserOptionsTests
{
    [Theory]
    [InlineData(10, 24)]
    [InlineData(24, 24)]
    [InlineData(60, 60)]
    [InlineData(96, 96)]
    [InlineData(150, 96)]
    public void ClampedBarsStayInRange(int bars, int expected)
    {
        var options = new VisualiserOptions { Bars = bars };
        Assert.Equal(expected, options.ClampedBars);
    }

    [Fact]
    public void SettingsBridgeCopiesFields()
    {
        var settings = new WinDots.Core.Settings.Settings
        {
            Visualiser = new VisualiserSettings
            {
                Enabled = true,
                Style = VisualiserStyle.Bars,
                Placement = VisualiserPlacement.Bottom,
                Bars = 80,
                Smoothing = 0.4,
                Mirrored = true,
            },
        };

        VisualiserOptions options = settings.ToVisualiserOptions();

        Assert.True(options.Enabled);
        Assert.Equal(VisualiserStyle.Bars, options.Style);
        Assert.Equal(VisualiserPlacement.Bottom, options.Placement);
        Assert.Equal(80, options.Bars);
        Assert.Equal(0.4, options.Smoothing);
        Assert.True(options.Mirrored);
    }

    [Fact]
    public void DefaultsMatchSpec()
    {
        var settings = new VisualiserSettings();
        Assert.False(settings.Enabled);
        Assert.Equal(VisualiserStyle.Ring, settings.Style);
        Assert.Equal(VisualiserPlacement.BehindArt, settings.Placement);
        Assert.Equal(60, settings.Bars);
        Assert.Equal(0.6, settings.Smoothing);
        Assert.False(settings.Mirrored);
    }

    [Fact]
    public void ConfigFromOptionsFasterAttackThanDecay()
    {
        var options = new VisualiserOptions { Bars = 48, Smoothing = 0.6 };
        AudioSpectrumConfig config = AudioSpectrumConfig.FromOptions(options);

        Assert.Equal(48, config.Bands);
        Assert.True(config.Attack > config.Decay, "attack should exceed decay");
    }

    [Fact]
    public void EnumsSerializeCamelCase()
    {
        var settings = new WinDots.Core.Settings.Settings
        {
            Visualiser = new VisualiserSettings { Style = VisualiserStyle.BlobPulse, Placement = VisualiserPlacement.BehindArt },
        };

        string json = System.Text.Json.JsonSerializer.Serialize(settings, WinDots.Core.Settings.Settings.JsonOptions);
        Assert.Contains("\"blobPulse\"", json, StringComparison.Ordinal);
        Assert.Contains("\"behindArt\"", json, StringComparison.Ordinal);

        WinDots.Core.Settings.Settings? roundTrip = System.Text.Json.JsonSerializer.Deserialize<WinDots.Core.Settings.Settings>(json, WinDots.Core.Settings.Settings.JsonOptions);
        Assert.NotNull(roundTrip);
        Assert.Equal(VisualiserStyle.BlobPulse, roundTrip!.Visualiser.Style);
        Assert.Equal(VisualiserPlacement.BehindArt, roundTrip.Visualiser.Placement);
    }
}
