using System.Text.Json;
using System.Text.Json.Nodes;

namespace HolidayLights.Core.Settings;

/// <summary>One step of the <c>settings.json</c> format: rewrites a document of <see cref="FromVersion"/> into the next version.</summary>
/// <param name="FromVersion">The version the step reads.</param>
/// <param name="Description">What changed (logged when the step runs).</param>
/// <param name="Apply">Rewrites the document in place.</param>
internal sealed record SettingsMigration(int FromVersion, string Description, Action<JsonObject> Apply);

/// <summary>The outcome of <see cref="SettingsMigrator.Migrate"/>.</summary>
/// <param name="FileVersion">The version found in the file (1 when the file did not say).</param>
/// <param name="CurrentVersion">The version the document has now (unless it is newer).</param>
/// <param name="AppliedSteps">The descriptions of the steps that ran, oldest first.</param>
internal sealed record SettingsMigrationResult(int FileVersion, int CurrentVersion, IReadOnlyList<string> AppliedSteps)
{
    /// <summary>True when the file was written by a newer Holiday Lights (values this build does not know are dropped).</summary>
    public bool IsNewerThanCurrent => FileVersion > CurrentVersion;
}

/// <summary>
/// Brings a <c>settings.json</c> document to <see cref="AppSettings.CurrentVersion"/> (PRODUCT-SPEC Appendix C: "version
/// drives migrations"). Steps work on the JSON document before it is deserialized, so a renamed or restructured value is
/// carried over instead of being lost to its default. Version 1 is the first file format, so <see cref="Default"/> has no
/// steps yet; every later format adds its step here.
/// </summary>
internal sealed class SettingsMigrator
{
    /// <summary>The first file format; a file without a usable <c>version</c> is read as this version.</summary>
    public const int FirstVersion = 1;

    private const string VersionProperty = "version";

    private readonly IReadOnlyList<SettingsMigration> steps;
    private readonly int currentVersion;

    /// <summary>Creates a migrator.</summary>
    /// <param name="steps">One step per version from <see cref="FirstVersion"/> to <paramref name="currentVersion"/> - 1.</param>
    /// <param name="currentVersion">The version this build writes.</param>
    /// <exception cref="ArgumentException">A step is missing or duplicated.</exception>
    public SettingsMigrator(IReadOnlyList<SettingsMigration> steps, int currentVersion = AppSettings.CurrentVersion)
    {
        for (int version = FirstVersion; version < currentVersion; version++)
        {
            int count = steps.Count(s => s.FromVersion == version);
            if (count != 1)
            {
                throw new ArgumentException($"Expected exactly one migration from version {version}, found {count}.", nameof(steps));
            }
        }

        this.steps = steps;
        this.currentVersion = currentVersion;
    }

    /// <summary>The migrations of this build.</summary>
    public static SettingsMigrator Default { get; } = new([]);

    /// <summary>Migrates a document in place and stamps it with the current version.</summary>
    /// <param name="root">The <c>settings.json</c> document.</param>
    /// <returns>The version the file had and the steps that ran.</returns>
    public SettingsMigrationResult Migrate(JsonObject root)
    {
        int fileVersion = ReadVersion(root);
        var applied = new List<string>();
        for (int version = fileVersion; version < currentVersion; version++)
        {
            SettingsMigration step = steps.First(s => s.FromVersion == version);
            step.Apply(root);
            applied.Add(step.Description);
        }

        if (fileVersion <= currentVersion)
        {
            root[VersionProperty] = currentVersion;
        }

        return new SettingsMigrationResult(fileVersion, currentVersion, applied);
    }

    private static int ReadVersion(JsonObject root) =>
        root[VersionProperty] is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue(out int version)
            ? Math.Max(version, FirstVersion)
            : FirstVersion;
}
