using WinDots.Core.Settings;

namespace WinDots.Core.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public JsonSettingsStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "windots-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task MissingFileLoadsDefaults()
    {
        var store = new JsonSettingsStore(_path);
        await store.LoadAsync(CancellationToken.None);

        Assert.Equal(1, store.Current.SchemaVersion);
        Assert.Equal(720, store.Current.Drawer.Width);
        Assert.Equal("Win+Shift+M", store.Current.Drawer.ToggleShortcut);
        Assert.Null(store.LastLoadProblem);
    }

    [Fact]
    public async Task SaveThenLoadRoundTrips()
    {
        var store = new JsonSettingsStore(_path);
        var settings = new WinDots.Core.Settings.Settings { Drawer = new DrawerSettings { Width = 999 } };
        await store.SaveAsync(settings, CancellationToken.None);

        var reloaded = new JsonSettingsStore(_path);
        await reloaded.LoadAsync(CancellationToken.None);

        Assert.Equal(999, reloaded.Current.Drawer.Width);
    }

    [Fact]
    public async Task SaveLeavesNoTempFileAndWritesBackup()
    {
        var store = new JsonSettingsStore(_path);
        await store.SaveAsync(new WinDots.Core.Settings.Settings(), CancellationToken.None);
        await store.SaveAsync(new WinDots.Core.Settings.Settings { Drawer = new DrawerSettings { Height = 500 } }, CancellationToken.None);

        Assert.False(File.Exists(_path + ".tmp"), "temp file must not remain");
        Assert.True(File.Exists(_path));
        Assert.True(File.Exists(_path + ".bak"), "backup must exist after a save over an existing file");

        var changed = new List<int>();
        var s = new JsonSettingsStore(_path);
        s.Changed += (_, e) => changed.Add(e.Drawer.Height);
        await s.LoadAsync(CancellationToken.None);
        Assert.Contains(500, changed);
    }

    [Fact]
    public async Task CorruptFileIsQuarantinedAndBackupRestored()
    {
        // Good backup, corrupt main.
        var good = new WinDots.Core.Settings.Settings { Media = new MediaSettings { SeekStepS = 11 } };
        await File.WriteAllTextAsync(
            _path + ".bak",
            System.Text.Json.JsonSerializer.Serialize(good, WinDots.Core.Settings.Settings.JsonOptions));
        await File.WriteAllTextAsync(_path, "{ this is not valid json ");

        var store = new JsonSettingsStore(_path);
        await store.LoadAsync(CancellationToken.None);

        Assert.Equal(11, store.Current.Media.SeekStepS);
        Assert.NotNull(store.LastLoadProblem);
        var corrupt = Directory.GetFiles(_dir, "settings.corrupt-*.json");
        Assert.Single(corrupt);
        Assert.False(File.Exists(_path), "corrupt main file was moved aside");
    }

    [Fact]
    public async Task CorruptFileWithNoBackupLoadsDefaults()
    {
        await File.WriteAllTextAsync(_path, "not json at all");

        var store = new JsonSettingsStore(_path);
        await store.LoadAsync(CancellationToken.None);

        Assert.Equal(720, store.Current.Drawer.Width);
        Assert.Equal("Win+Shift+M", store.Current.Drawer.ToggleShortcut);
        Assert.Equal(5, store.Current.Media.SeekStepS);
        Assert.NotNull(store.LastLoadProblem);
        Assert.Single(Directory.GetFiles(_dir, "settings.corrupt-*.json"));
    }

    [Fact]
    public async Task NegativeIntFallsBackToDefaultWithWarning()
    {
        await File.WriteAllTextAsync(_path, """{ "schemaVersion": 1, "media": { "seekStepS": -7 } }""");

        var store = new JsonSettingsStore(_path);
        await store.LoadAsync(CancellationToken.None);

        Assert.Equal(5, store.Current.Media.SeekStepS); // default
        Assert.Contains(store.LoadWarnings, w => w.Contains("media.seekStepS", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnknownEnumFallsBackToDefaultWithWarning()
    {
        await File.WriteAllTextAsync(_path, """{ "schemaVersion": 1, "appearance": { "theme": "neon" } }""");

        var store = new JsonSettingsStore(_path);
        await store.LoadAsync(CancellationToken.None);

        Assert.Equal(AppearanceTheme.Auto, store.Current.Appearance.Theme); // default
        Assert.Contains(store.LoadWarnings, w => w.Contains("appearance.theme", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WrongTypedStringFieldFallsBackWithoutDiscardingWholeFile()
    {
        // A type-mismatched string value must revert only that one key, not quarantine the file.
        await File.WriteAllTextAsync(
            _path,
            """{ "schemaVersion": 1, "appearance": { "fixedAccent": 123 }, "drawer": { "width": 999 } }""");

        var store = new JsonSettingsStore(_path);
        await store.LoadAsync(CancellationToken.None);

        Assert.Equal("#8FD3C8", store.Current.Appearance.FixedAccent); // default
        Assert.Equal(999, store.Current.Drawer.Width);                 // preserved, not reset
        Assert.Null(store.LastLoadProblem);                            // file not quarantined
        Assert.Contains(store.LoadWarnings, w => w.Contains("appearance.fixedAccent", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(_dir, "settings.corrupt-*.json"));
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public async Task WrongTypedDoubleFieldFallsBackWithoutDiscardingWholeFile()
    {
        await File.WriteAllTextAsync(
            _path,
            """{ "schemaVersion": 1, "drawer": { "openThreshold": "high", "width": 999 } }""");

        var store = new JsonSettingsStore(_path);
        await store.LoadAsync(CancellationToken.None);

        Assert.Equal(0.35, store.Current.Drawer.OpenThreshold); // default
        Assert.Equal(999, store.Current.Drawer.Width);
        Assert.Null(store.LastLoadProblem);
        Assert.Contains(store.LoadWarnings, w => w.Contains("drawer.openThreshold", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnknownKeysSurviveThroughStore()
    {
        await File.WriteAllTextAsync(_path, """{ "schemaVersion": 1, "drawer": { "futureKnob": 7 } }""");

        var store = new JsonSettingsStore(_path);
        await store.LoadAsync(CancellationToken.None);

        Assert.True(store.Current.Drawer.Extra.ContainsKey("futureKnob"));
    }
}
