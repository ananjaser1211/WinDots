using System.Text.Json.Nodes;
using WinDots.Core.Settings;

namespace WinDots.Core.Tests.Settings;

public class SettingsMigratorTests
{
    [Fact]
    public void CurrentVersionNoOpLeavesNodeUnchanged()
    {
        var node = JsonNode.Parse("""{ "schemaVersion": 1, "drawer": { "width": 720 } }""")!;
        var warnings = new List<string>();

        var result = new SettingsMigrator().Migrate(node, warnings);

        Assert.Equal(1, result["schemaVersion"]!.GetValue<int>());
        Assert.Equal(720, result["drawer"]!["width"]!.GetValue<int>());
        Assert.Empty(warnings);
    }

    [Fact]
    public void StepsApplyInAscendingVersionOrder()
    {
        var order = new List<int>();
        var steps = new[]
        {
            new MigrationStep(2, _ => order.Add(2)),
            new MigrationStep(1, _ => order.Add(1)),
        };
        var migrator = new SettingsMigrator(steps, target: 3);
        var node = JsonNode.Parse("""{ "schemaVersion": 1 }""")!;

        var result = migrator.Migrate(node, new List<string>());

        Assert.Equal(new[] { 1, 2 }, order);
        Assert.Equal(3, result["schemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public void StepsBelowSourceVersionAreSkipped()
    {
        var applied = new List<int>();
        var steps = new[]
        {
            new MigrationStep(1, _ => applied.Add(1)),
            new MigrationStep(2, _ => applied.Add(2)),
        };
        var migrator = new SettingsMigrator(steps, target: 3);
        var node = JsonNode.Parse("""{ "schemaVersion": 2 }""")!;

        migrator.Migrate(node, new List<string>());

        Assert.Equal(new[] { 2 }, applied);
    }

    [Fact]
    public void FutureVersionLoadsAsIsWithWarning()
    {
        var migrator = new SettingsMigrator();
        var node = JsonNode.Parse("""{ "schemaVersion": 99, "drawer": { "width": 720 } }""")!;
        var warnings = new List<string>();

        var result = migrator.Migrate(node, warnings);

        Assert.Equal(99, result["schemaVersion"]!.GetValue<int>());
        Assert.Single(warnings);
    }

    [Fact]
    public void NoOpSampleStepDoesNothing()
    {
        var node = (JsonObject)JsonNode.Parse("""{ "schemaVersion": 1, "drawer": { "width": 5 } }""")!;
        SettingsMigrator.NoOpSample.Apply(node);
        Assert.Equal(5, node["drawer"]!["width"]!.GetValue<int>());
    }
}
