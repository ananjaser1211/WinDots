using System.Text.Json;
using System.Text.Json.Nodes;
using WinDots.Core.Settings;

namespace WinDots.Core.Tests.Settings;

public class SettingsSerializationTests
{
    [Fact]
    public void DefaultsRoundTrip()
    {
        var original = new WinDots.Core.Settings.Settings();

        string json = JsonSerializer.Serialize(original, WinDots.Core.Settings.Settings.JsonOptions);
        var restored = JsonSerializer.Deserialize<WinDots.Core.Settings.Settings>(json, WinDots.Core.Settings.Settings.JsonOptions)!;

        Assert.Equal(1, restored.SchemaVersion);
        Assert.Equal(original.Drawer.ToggleShortcut, restored.Drawer.ToggleShortcut);
        Assert.Equal(original.Drawer.Width, restored.Drawer.Width);
        Assert.Equal(original.Drawer.OpenThreshold, restored.Drawer.OpenThreshold);
        Assert.Equal(original.Appearance.Theme, restored.Appearance.Theme);
        Assert.Equal(original.Appearance.FixedAccent, restored.Appearance.FixedAccent);
        Assert.Equal(original.Privacy.HistoryRetentionDays, restored.Privacy.HistoryRetentionDays);
        Assert.Equal(original.Diagnostics.LogLevel, restored.Diagnostics.LogLevel);
        Assert.Equal(original.Media.TimelineTickMs, restored.Media.TimelineTickMs);
        Assert.Empty(restored.Media.IgnoredPlayers);
        Assert.Empty(restored.Monitors.EnabledDeviceIds);
        Assert.Equal(original.Performance.SampleIntervalMs, restored.Performance.SampleIntervalMs);
    }

    [Fact]
    public void DefaultSourceRulesRoundTrip()
    {
        var original = new WinDots.Core.Settings.Settings();
        string json = JsonSerializer.Serialize(original, WinDots.Core.Settings.Settings.JsonOptions);
        var restored = JsonSerializer.Deserialize<WinDots.Core.Settings.Settings>(json, WinDots.Core.Settings.Settings.JsonOptions)!;

        Assert.Equal(WinDots.Core.Media.SourceMode.Tracked, restored.Media.SourceMode);
        Assert.False(restored.Media.CaptureMediaKeys);
        Assert.Equal(original.Media.SourceRules.Count, restored.Media.SourceRules.Count);
        Assert.Equal(original.Media.SourceRules[0], restored.Media.SourceRules[0]);
    }

    [Fact]
    public void CustomSourceRulesRoundTrip()
    {
        var settings = new WinDots.Core.Settings.Settings
        {
            Media = new MediaSettings
            {
                SourceMode = WinDots.Core.Media.SourceMode.All,
                CaptureMediaKeys = true,
                SourceRules = new[]
                {
                    new WinDots.Core.Media.SourceRule("com.example.player", WinDots.Core.Media.SourceRuleMode.Never),
                },
            },
        };

        string json = JsonSerializer.Serialize(settings, WinDots.Core.Settings.Settings.JsonOptions);
        Assert.Contains("\"sourceMode\": \"all\"", json, StringComparison.Ordinal);
        Assert.Contains("\"mode\": \"never\"", json, StringComparison.Ordinal);

        var restored = JsonSerializer.Deserialize<WinDots.Core.Settings.Settings>(json, WinDots.Core.Settings.Settings.JsonOptions)!;
        Assert.True(restored.Media.CaptureMediaKeys);
        Assert.Single(restored.Media.SourceRules);
        Assert.Equal("com.example.player", restored.Media.SourceRules[0].Match);
        Assert.Equal(WinDots.Core.Media.SourceRuleMode.Never, restored.Media.SourceRules[0].Mode);
    }

    [Fact]
    public void EnumsSerializeAsCamelCaseStrings()
    {
        var settings = new WinDots.Core.Settings.Settings
        {
            Appearance = new AppearanceSettings { Theme = AppearanceTheme.Dark, PaletteSource = PaletteSource.Fixed },
        };

        string json = JsonSerializer.Serialize(settings, WinDots.Core.Settings.Settings.JsonOptions);

        Assert.Contains("\"theme\": \"dark\"", json, StringComparison.Ordinal);
        Assert.Contains("\"paletteSource\": \"fixed\"", json, StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownKeysArePreservedOnRoundTrip()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "drawer": { "enabled": true, "futureKnob": 42 },
          "media": { "mysteryList": [1, 2, 3] }
        }
        """;

        var settings = JsonSerializer.Deserialize<WinDots.Core.Settings.Settings>(json, WinDots.Core.Settings.Settings.JsonOptions)!;

        Assert.True(settings.Drawer.Extra.ContainsKey("futureKnob"));
        Assert.True(settings.Media.Extra.ContainsKey("mysteryList"));

        string reserialized = JsonSerializer.Serialize(settings, WinDots.Core.Settings.Settings.JsonOptions);
        var node = JsonNode.Parse(reserialized)!;

        Assert.Equal(42, node["drawer"]!["futureKnob"]!.GetValue<int>());
        Assert.Equal(3, node["media"]!["mysteryList"]!.AsArray().Count);
    }

    [Fact]
    public void TopLevelUnknownKeysArePreservedOnRoundTrip()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "experimentalSection": { "foo": 1 },
          "mysteryScalar": "hello",
          "drawer": { "width": 720 }
        }
        """;

        var settings = JsonSerializer.Deserialize<WinDots.Core.Settings.Settings>(json, WinDots.Core.Settings.Settings.JsonOptions)!;

        Assert.True(settings.Extra.ContainsKey("experimentalSection"));
        Assert.True(settings.Extra.ContainsKey("mysteryScalar"));

        string reserialized = JsonSerializer.Serialize(settings, WinDots.Core.Settings.Settings.JsonOptions);
        var node = JsonNode.Parse(reserialized)!;

        Assert.Equal(1, node["experimentalSection"]!["foo"]!.GetValue<int>());
        Assert.Equal("hello", node["mysteryScalar"]!.GetValue<string>());
        Assert.Equal(720, node["drawer"]!["width"]!.GetValue<int>());
    }

    [Fact]
    public void ToMediaOptionsMapsMediaSection()
    {
        var settings = new WinDots.Core.Settings.Settings
        {
            Media = new MediaSettings
            {
                PreferredPlayer = "Spotify.exe",
                IgnoredPlayers = new[] { "chrome.exe" },
                PlayerAliases = new Dictionary<string, string> { ["Spotify.exe"] = "Spotify" },
                TimelineTickMs = 250,
            },
        };

        var options = settings.ToMediaOptions();

        Assert.Equal("Spotify.exe", options.PreferredPlayer);
        Assert.Equal(new[] { "chrome.exe" }, options.IgnoredPlayers);
        Assert.Equal("Spotify", options.PlayerAliases["Spotify.exe"]);
        Assert.Equal(250, options.TimelineTickMs);
    }

    [Fact]
    public void ToMediaOptionsEmptyPreferredPlayerBecomesNull()
    {
        var options = new WinDots.Core.Settings.Settings().ToMediaOptions();
        Assert.Null(options.PreferredPlayer);
    }
}
