using System.Text.Json.Nodes;

namespace WinDots.Core.Settings;

/// <summary>One ordered migration: transforms a settings object at <see cref="FromVersion"/> to the next version.</summary>
public sealed record MigrationStep(int FromVersion, Action<JsonObject> Apply);

/// <summary>
/// Applies ordered <see cref="MigrationStep"/>s to a settings <see cref="JsonNode"/> before it is bound.
/// <see cref="CurrentSchemaVersion"/> is 1, so the default step set is a no-op; unknown future versions load
/// as-is with a warning. See _docs/06-settings-schema.md ("Migration and recovery").
/// </summary>
public sealed class SettingsMigrator
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>A sample step demonstrating the framework; it does nothing and is exercised only by tests.</summary>
    public static readonly MigrationStep NoOpSample = new(1, static _ => { });

    private readonly IReadOnlyList<MigrationStep> _steps;
    private readonly int _target;

    public SettingsMigrator()
        : this(Array.Empty<MigrationStep>(), CurrentSchemaVersion)
    {
    }

    public SettingsMigrator(IEnumerable<MigrationStep> steps, int target)
    {
        ArgumentNullException.ThrowIfNull(steps);
        _steps = steps.OrderBy(s => s.FromVersion).ToArray();
        _target = target;
    }

    /// <summary>
    /// Migrates <paramref name="root"/> in place to the target version, collecting warnings. Returns the same
    /// node. A missing or invalid <c>schemaVersion</c> is treated as the current version.
    /// </summary>
    public JsonNode Migrate(JsonNode root, List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(warnings);

        if (root is not JsonObject obj)
        {
            return root;
        }

        int version = ReadVersion(obj, warnings);

        if (version > _target)
        {
            warnings.Add(
                $"Settings schemaVersion {version} is newer than supported version {_target}; loading as-is.");
            return obj;
        }

        foreach (MigrationStep step in _steps)
        {
            if (step.FromVersion >= version && step.FromVersion < _target)
            {
                step.Apply(obj);
            }
        }

        if (version < _target)
        {
            obj["schemaVersion"] = _target;
        }

        return obj;
    }

    private static int ReadVersion(JsonObject obj, List<string> warnings)
    {
        if (obj.TryGetPropertyValue("schemaVersion", out JsonNode? node) && node is not null)
        {
            try
            {
                return node.GetValue<int>();
            }
            catch (FormatException)
            {
                warnings.Add("Settings schemaVersion was not an integer; assuming current version.");
            }
            catch (InvalidOperationException)
            {
                warnings.Add("Settings schemaVersion was not an integer; assuming current version.");
            }
        }

        return CurrentSchemaVersion;
    }
}
