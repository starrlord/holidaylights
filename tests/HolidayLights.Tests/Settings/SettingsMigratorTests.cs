using System.Text.Json.Nodes;
using HolidayLights.Core.Settings;

namespace HolidayLights.Tests.Settings;

public sealed class SettingsMigratorTests
{
    [Fact]
    public void Migrate_RunsEveryStepInOrder_AndStampsTheVersion()
    {
        var steps = new List<string>();
        var migrator = new SettingsMigrator(
            [
                new SettingsMigration(2, "two", _ => steps.Add("2->3")),
                new SettingsMigration(1, "one", _ => steps.Add("1->2")),
            ],
            currentVersion: 3);
        var document = JsonNode.Parse("""{ "version": 1 }""")!.AsObject();

        SettingsMigrationResult result = migrator.Migrate(document);

        Assert.Equal(["1->2", "2->3"], steps);
        Assert.Equal(["one", "two"], result.AppliedSteps);
        Assert.Equal(1, result.FileVersion);
        Assert.Equal(3, (int)document["version"]!);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "version": "x" }""")]
    [InlineData("""{ "version": -4 }""")]
    public void Migrate_AFileWithoutAUsableVersion_IsTheFirstVersion(string json)
    {
        var migrator = new SettingsMigrator([new SettingsMigration(1, "one", _ => { })], currentVersion: 2);

        SettingsMigrationResult result = migrator.Migrate(JsonNode.Parse(json)!.AsObject());

        Assert.Equal(1, result.FileVersion);
        Assert.Equal(["one"], result.AppliedSteps);
    }

    [Fact]
    public void Migrate_ANewerFile_IsLeftAlone()
    {
        var document = JsonNode.Parse("""{ "version": 9 }""")!.AsObject();

        SettingsMigrationResult result = SettingsMigrator.Default.Migrate(document);

        Assert.True(result.IsNewerThanCurrent);
        Assert.Empty(result.AppliedSteps);
        Assert.Equal(9, (int)document["version"]!);
    }

    [Fact]
    public void Constructor_RejectsAMissingStep() =>
        Assert.Throws<ArgumentException>(() => new SettingsMigrator([new SettingsMigration(2, "two", _ => { })], currentVersion: 3));

    [Fact]
    public void Default_IsForTheCurrentVersion()
    {
        var document = JsonNode.Parse("{}")!.AsObject();

        SettingsMigrationResult result = SettingsMigrator.Default.Migrate(document);

        Assert.False(result.IsNewerThanCurrent);
        Assert.Equal(AppSettings.CurrentVersion, (int)document["version"]!);
    }
}
