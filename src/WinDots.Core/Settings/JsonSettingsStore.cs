using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WinDots.Core.Contracts;

namespace WinDots.Core.Settings;

/// <summary>
/// File-backed <see cref="ISettingsStore"/>. Writes atomically (temp file + <see cref="File.Move(string,string,bool)"/>),
/// keeps a <c>.bak</c> copy, and recovers from corruption by renaming the bad file aside and falling back to the
/// backup or defaults. Invalid individual values fall back to their key default with a collected warning.
/// See _docs/06-settings-schema.md.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly string _backupPath;
    private readonly SettingsMigrator _migrator;
    private readonly List<string> _loadWarnings = new();

    public JsonSettingsStore(string path, SettingsMigrator? migrator = null)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _backupPath = _path + ".bak";
        _migrator = migrator ?? new SettingsMigrator();
        Current = new Settings();
    }

    public Settings Current { get; private set; }

    public event EventHandler<Settings>? Changed;

    /// <summary>Set when the main file could not be read and recovery kicked in; null on a clean load.</summary>
    public string? LastLoadProblem { get; private set; }

    /// <summary>Per-key warnings collected during the most recent load (invalid values, future versions).</summary>
    public IReadOnlyList<string> LoadWarnings => _loadWarnings;

    public Task LoadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _loadWarnings.Clear();
        LastLoadProblem = null;

        if (!File.Exists(_path))
        {
            if (TryLoadFile(_backupPath, out Settings? fromBackup))
            {
                LastLoadProblem = "Main settings file was missing; restored from backup.";
                Apply(fromBackup);
                return Task.CompletedTask;
            }

            Apply(new Settings());
            return Task.CompletedTask;
        }

        if (TryLoadFile(_path, out Settings? settings))
        {
            Apply(settings);
            return Task.CompletedTask;
        }

        // Main file is corrupt: move it aside, then try the backup, else defaults.
        string corruptPath = QuarantineCorruptFile();
        LastLoadProblem = $"Settings file was unreadable and was moved to '{Path.GetFileName(corruptPath)}'.";

        if (TryLoadFile(_backupPath, out Settings? recovered))
        {
            Apply(recovered);
        }
        else
        {
            Apply(new Settings());
        }

        return Task.CompletedTask;
    }

    public async Task SaveAsync(Settings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ct.ThrowIfCancellationRequested();

        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Back up the current file before overwriting it.
        if (File.Exists(_path))
        {
            File.Copy(_path, _backupPath, overwrite: true);
        }

        string json = JsonSerializer.Serialize(settings, Settings.JsonOptions);
        string tempPath = _path + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, new UTF8Encoding(false), ct).ConfigureAwait(false);
        File.Move(tempPath, _path, overwrite: true);

        Apply(settings);
    }

    private void Apply(Settings settings)
    {
        Current = settings;
        Changed?.Invoke(this, settings);
    }

    private string QuarantineCorruptFile()
    {
        string stamp = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        string? directory = Path.GetDirectoryName(_path);
        string corruptName = $"settings.corrupt-{stamp}.json";
        string corruptPath = string.IsNullOrEmpty(directory)
            ? corruptName
            : Path.Combine(directory, corruptName);
        File.Move(_path, corruptPath, overwrite: true);
        return corruptPath;
    }

    private bool TryLoadFile(string path, out Settings settings)
    {
        settings = new Settings();
        if (!File.Exists(path))
        {
            return false;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return false;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return false;
        }

        if (node is null)
        {
            return false;
        }

        node = _migrator.Migrate(node, _loadWarnings);
        Sanitizer.Sanitize(node, _loadWarnings);

        try
        {
            Settings? bound = node.Deserialize<Settings>(Settings.JsonOptions);
            if (bound is null)
            {
                return false;
            }

            settings = bound;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Replaces individual invalid values in the raw JSON with their defaults, collecting warnings.</summary>
    private static class Sanitizer
    {
        public static void Sanitize(JsonNode node, List<string> warnings)
        {
            if (node is not JsonObject root)
            {
                return;
            }

            EnumRule(root, "appearance", "theme", typeof(AppearanceTheme), warnings);
            EnumRule(root, "appearance", "backdrop", typeof(Backdrop), warnings);
            EnumRule(root, "appearance", "paletteSource", typeof(PaletteSource), warnings);
            EnumRule(root, "appearance", "reduceMotion", typeof(ReduceMotion), warnings);
            EnumRule(root, "monitors", "mode", typeof(MonitorMode), warnings);
            EnumRule(root, "diagnostics", "logLevel", typeof(LogLevel), warnings);
            EnumRule(root, "lyrics", "provider", typeof(LyricsProvider), warnings);

            NonNegativeIntRule(root, "drawer", "hoverOpenDelayMs", warnings);
            NonNegativeIntRule(root, "drawer", "dragThresholdPx", warnings);
            NonNegativeIntRule(root, "drawer", "velocityThresholdPxPerS", warnings);
            NonNegativeIntRule(root, "drawer", "autoHideMs", warnings);
            NonNegativeIntRule(root, "drawer", "width", warnings);
            NonNegativeIntRule(root, "drawer", "height", warnings);
            NonNegativeIntRule(root, "media", "timelineTickMs", warnings);
            NonNegativeIntRule(root, "media", "seekStepS", warnings);
            NonNegativeIntRule(root, "media", "volumeStepPercent", warnings);
            NonNegativeIntRule(root, "monitors", "handleOffsetPercent", warnings);
            NonNegativeIntRule(root, "privacy", "historyRetentionDays", warnings);
            NonNegativeIntRule(root, "visualiser", "bars", warnings);
            NonNegativeIntRule(root, "performance", "sampleIntervalMs", warnings);

            NumberRule(root, "drawer", "openThreshold", warnings);
            NumberRule(root, "appearance", "fontScale", warnings);
            NumberRule(root, "appearance", "blobDeform", warnings);

            StringRule(root, "drawer", "toggleShortcut", warnings);
            StringRule(root, "media", "preferredPlayer", warnings);
            StringRule(root, "appearance", "fixedAccent", warnings);
            StringRule(root, "weather", "location", warnings);
        }

        private static void NumberRule(JsonObject root, string section, string key, List<string> warnings)
        {
            if (root[section] is not JsonObject obj || !obj.TryGetPropertyValue(key, out JsonNode? value) || value is null)
            {
                return;
            }

            if (value is JsonValue jv && jv.TryGetValue(out double _))
            {
                return;
            }

            Drop(obj, section, key, warnings, "not a number");
        }

        private static void StringRule(JsonObject root, string section, string key, List<string> warnings)
        {
            if (root[section] is not JsonObject obj || !obj.TryGetPropertyValue(key, out JsonNode? value) || value is null)
            {
                return;
            }

            if (TryGetString(value) is null)
            {
                Drop(obj, section, key, warnings, "not a string");
            }
        }

        private static void EnumRule(JsonObject root, string section, string key, Type enumType, List<string> warnings)
        {
            if (root[section] is not JsonObject obj || !obj.TryGetPropertyValue(key, out JsonNode? value) || value is null)
            {
                return;
            }

            string? raw = TryGetString(value);
            if (raw is null)
            {
                Drop(obj, section, key, warnings, "not a string");
                return;
            }

            foreach (string name in Enum.GetNames(enumType))
            {
                if (string.Equals(ToCamel(name), raw, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            Drop(obj, section, key, warnings, $"unknown value '{raw}'");
        }

        private static void NonNegativeIntRule(JsonObject root, string section, string key, List<string> warnings)
        {
            if (root[section] is not JsonObject obj || !obj.TryGetPropertyValue(key, out JsonNode? value) || value is null)
            {
                return;
            }

            if (value is JsonValue jv && jv.TryGetValue(out int i))
            {
                if (i < 0)
                {
                    Drop(obj, section, key, warnings, $"negative value {i}");
                }

                return;
            }

            Drop(obj, section, key, warnings, "not an integer");
        }

        private static void Drop(JsonObject obj, string section, string key, List<string> warnings, string reason)
        {
            obj.Remove(key);
            warnings.Add($"{section}.{key}: {reason}; using default.");
        }

        private static string? TryGetString(JsonNode node)
            => node is JsonValue jv && jv.TryGetValue(out string? s) ? s : null;

        private static string ToCamel(string name)
            => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
    }
}
