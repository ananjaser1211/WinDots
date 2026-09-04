using System.Text.Json;
using System.Text.Json.Serialization;
using WinDots.Core.Media;

namespace WinDots.Core.Settings;

/// <summary>
/// Immutable settings model mirroring <c>_docs/06-settings-schema.md</c>. Serialised as camelCase JSON with
/// enums as strings. Each section keeps a <see cref="JsonExtensionData"/> bag so unknown keys survive a
/// round-trip. <see cref="SchemaVersion"/> is the on-disk schema version (1 is current).
/// </summary>
public sealed record Settings
{
    /// <summary>Shared serializer options: camelCase properties, string enums, indented, ignore nulls off.</summary>
    public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public int SchemaVersion { get; init; } = 1;

    public DrawerSettings Drawer { get; init; } = new();

    public MediaSettings Media { get; init; } = new();

    public AppearanceSettings Appearance { get; init; } = new();

    public MonitorsSettings Monitors { get; init; } = new();

    public PrivacySettings Privacy { get; init; } = new();

    public DiagnosticsSettings Diagnostics { get; init; } = new();

    public LyricsSettings Lyrics { get; init; } = new();

    [JsonPropertyName("lastfm")]
    public LastFmSettings LastFm { get; init; } = new();

    public VisualiserSettings Visualiser { get; init; } = new();

    public WeatherSettings Weather { get; init; } = new();

    public PerformanceSettings Performance { get; init; } = new();

    /// <summary>Unknown top-level keys/sections, preserved verbatim across a round-trip.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = new(StringComparer.Ordinal);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            WriteIndented = true,
            PropertyNameCaseInsensitive = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public enum AppearanceTheme
{
    Auto,
    Dark,
    Light,
}

public enum Backdrop
{
    Acrylic,
    Opaque,
}

public enum PaletteSource
{
    Artwork,
    Fixed,
}

public enum ReduceMotion
{
    System,
    On,
    Off,
}

public enum MonitorMode
{
    All,
    Primary,
    List,
}

public enum LogLevel
{
    Warning,
    Info,
    Debug,
}

public enum LyricsProvider
{
    Off,
    Lrclib,
}

public sealed record DrawerSettings
{
    public bool Enabled { get; init; } = true;

    public bool ShowOnHover { get; init; }

    public int HoverOpenDelayMs { get; init; } = 600;

    public int DragThresholdPx { get; init; } = 50;

    public double OpenThreshold { get; init; } = 0.35;

    public int VelocityThresholdPxPerS { get; init; } = 600;

    public string ToggleShortcut { get; init; } = "Win+Shift+M";

    public int AutoHideMs { get; init; }

    public bool HideAfterCommand { get; init; }

    public bool HideInFullscreen { get; init; } = true;

    public bool AlwaysOnTop { get; init; } = true;

    public int Width { get; init; } = 720;

    public int Height { get; init; } = 300;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = new(StringComparer.Ordinal);
}

public sealed record MediaSettings
{
    public string PreferredPlayer { get; init; } = string.Empty;

    public IReadOnlyList<string> IgnoredPlayers { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string> PlayerAliases { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public int TimelineTickMs { get; init; } = 500;

    public bool AllowSharedVolume { get; init; }

    public int SeekStepS { get; init; } = 5;

    public int VolumeStepPercent { get; init; } = 2;

    /// <summary>Whether the coordinator surfaces only music sources (<c>tracked</c>) or every source (<c>all</c>).</summary>
    public SourceMode SourceMode { get; init; } = SourceMode.Tracked;

    /// <summary>Ordered per-source rules; user rules take precedence over the built-in defaults.</summary>
    public IReadOnlyList<SourceRule> SourceRules { get; init; } = SourceRule.Defaults;

    /// <summary>When on, the media transport keys are captured globally and routed to the active music session.</summary>
    public bool CaptureMediaKeys { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = new(StringComparer.Ordinal);
}

public sealed record AppearanceSettings
{
    public AppearanceTheme Theme { get; init; } = AppearanceTheme.Auto;

    public Backdrop Backdrop { get; init; } = Backdrop.Acrylic;

    public double FontScale { get; init; } = 1.0;

    public double BlobDeform { get; init; } = 1.0;

    public PaletteSource PaletteSource { get; init; } = PaletteSource.Artwork;

    public string FixedAccent { get; init; } = "#8FD3C8";

    public ReduceMotion ReduceMotion { get; init; } = ReduceMotion.System;

    public bool BackgroundBlobs { get; init; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = new(StringComparer.Ordinal);
}

public sealed record MonitorsSettings
{
    public MonitorMode Mode { get; init; } = MonitorMode.All;

    public IReadOnlyList<string> EnabledDeviceIds { get; init; } = Array.Empty<string>();

    public int HandleOffsetPercent { get; init; } = 50;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = new(StringComparer.Ordinal);
}

public sealed record PrivacySettings
{
    public bool HistoryEnabled { get; init; }

    public int HistoryRetentionDays { get; init; } = 90;

    public bool NetworkFeaturesAcknowledged { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = new(StringComparer.Ordinal);
}

public sealed record DiagnosticsSettings
{
    public LogLevel LogLevel { get; init; } = LogLevel.Warning;

    public bool IncludeMediaText { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = new(StringComparer.Ordinal);
}

public sealed record LyricsSettings
{
    public LyricsProvider Provider { get; init; } = LyricsProvider.Off;

    public int OffsetMs { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = new(StringComparer.Ordinal);
}

public sealed record LastFmSettings
{
    /// <summary>Master switch for the Last.fm integration. Nothing is sent while off.</summary>
    public bool Enabled { get; init; }

    /// <summary>Submit qualified plays as scrobbles.</summary>
    public bool Scrobble { get; init; } = true;

    /// <summary>Send now-playing notifications on track start.</summary>
    public bool NowPlaying { get; init; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = new(StringComparer.Ordinal);
}

public sealed record VisualiserSettings
{
    public bool Enabled { get; init; }

    public int Bars { get; init; } = 60;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = new(StringComparer.Ordinal);
}

public sealed record WeatherSettings
{
    public bool Enabled { get; init; }

    public string Location { get; init; } = string.Empty;

    public bool UseFahrenheit { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = new(StringComparer.Ordinal);
}

public sealed record PerformanceSettings
{
    public int SampleIntervalMs { get; init; } = 1000;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>Bridges the settings <c>media</c> section to the runtime <see cref="MediaOptions"/> record.</summary>
public static class SettingsExtensions
{
    public static MediaOptions ToMediaOptions(this Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        MediaSettings media = settings.Media;
        return new MediaOptions
        {
            PreferredPlayer = string.IsNullOrEmpty(media.PreferredPlayer) ? null : media.PreferredPlayer,
            IgnoredPlayers = media.IgnoredPlayers.ToArray(),
            PlayerAliases = new Dictionary<string, string>(media.PlayerAliases, StringComparer.Ordinal),
            TimelineTickMs = media.TimelineTickMs,
            AllowSharedVolume = media.AllowSharedVolume,
            VolumeStepPercent = media.VolumeStepPercent,
            SourceMode = media.SourceMode,
            SourceRules = media.SourceRules.ToArray(),
        };
    }
}
