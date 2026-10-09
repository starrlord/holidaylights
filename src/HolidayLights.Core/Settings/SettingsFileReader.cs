using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HolidayLights.Core.Settings;

/// <summary>How reading <c>settings.json</c> ended.</summary>
internal enum SettingsReadStatus
{
    /// <summary>The file was read and interpreted.</summary>
    Read,

    /// <summary>The file was read, but its content cannot be interpreted: it is damaged.</summary>
    Damaged,

    /// <summary>
    /// The file could not be read at all (another program holds it, it is being replaced, access is denied). This says
    /// nothing about its content, so the file is never renamed or replaced because of it.
    /// </summary>
    Unavailable,
}

/// <summary>What reading <c>settings.json</c> produced.</summary>
/// <param name="Status">How reading ended.</param>
/// <param name="Settings">The sanitized settings, or null when the file was damaged or unavailable.</param>
/// <param name="Problem">Why the file was damaged or unavailable, else null.</param>
/// <param name="Migration">The migration outcome (null when not read).</param>
/// <param name="RemovedValues">JSON paths of values that could not be read and took their defaults.</param>
/// <param name="Content">The bytes that were read (kept for the copy of a damaged file), or null when unavailable.</param>
internal sealed record SettingsReadResult(
    SettingsReadStatus Status,
    AppSettings? Settings,
    string? Problem,
    SettingsMigrationResult? Migration,
    IReadOnlyList<string> RemovedValues,
    byte[]? Content);

/// <summary>
/// Reads <c>settings.json</c> (PRODUCT-SPEC Appendix C, 5.13): parse, migrate to the current version, deserialize with the
/// source-generated context (removing values that cannot be read), sanitize. Also handles the files that protect the
/// user's data: the damaged-file rename (or copy) and a one-time copy of a file written by a newer version.
/// </summary>
/// <remarks>
/// Only content that cannot be parsed counts as damaged. A file that cannot be opened (a sharing violation from a sync,
/// backup or antivirus tool, a file in the middle of being replaced, access denied) is retried while the caller's
/// patience lasts and then reported as <see cref="SettingsReadStatus.Unavailable"/>.
/// </remarks>
internal static class SettingsFileReader
{
    private const int MaxRetryDelayMilliseconds = 400;

    /// <summary>Reads and interprets the file.</summary>
    /// <param name="path">The settings file (it exists).</param>
    /// <param name="migrator">The migrations.</param>
    /// <param name="patience">How long to keep retrying while the file cannot be opened (zero: one attempt).</param>
    /// <returns>The result; <see cref="SettingsReadResult.Settings"/> is set only for <see cref="SettingsReadStatus.Read"/>.</returns>
    /// <exception cref="FileNotFoundException">The file disappeared while being read.</exception>
    /// <exception cref="DirectoryNotFoundException">The folder disappeared while being read.</exception>
    public static SettingsReadResult Read(string path, SettingsMigrator migrator, TimeSpan patience)
    {
        byte[] content;
        try
        {
            content = ReadBytes(path, patience);
        }
        catch (Exception e) when (IsUnavailable(e))
        {
            return new SettingsReadResult(SettingsReadStatus.Unavailable, null, e.Message, null, [], null);
        }

        if (!JsonSalvage.TryParseObject(Decode(content), out JsonObject? root, out string? problem))
        {
            return Damaged(problem, content);
        }

        try
        {
            SettingsMigrationResult migration = migrator.Migrate(root);
            var removed = new List<string>();
            AppSettings settings = JsonSalvage.Deserialize(root, HolidayLightsJsonContext.Default.AppSettings, removed);
            return new SettingsReadResult(SettingsReadStatus.Read, SettingsSanitizer.Sanitize(settings), null, migration, removed, content);
        }
        catch (JsonException e)
        {
            return Damaged(e.Message, content);
        }
    }

    /// <summary>
    /// True for a failure that says nothing about the file's content: another program holds or locks it, it is being
    /// replaced (delete pending), or access is denied. A missing file or folder is not such a failure.
    /// </summary>
    /// <param name="e">The exception.</param>
    /// <returns>True when the file is only unavailable.</returns>
    public static bool IsUnavailable(Exception e) =>
        e is UnauthorizedAccessException or IOException and not (FileNotFoundException or DirectoryNotFoundException);

    /// <summary>Renames a damaged file to <c>settings.damaged-&lt;date&gt;.json</c> (with " (2)" etc. when taken).</summary>
    /// <param name="path">The settings file.</param>
    /// <param name="today">The local date for the name.</param>
    /// <returns>The new path.</returns>
    /// <exception cref="IOException">The file could not be renamed.</exception>
    public static string RenameDamaged(string path, DateOnly today)
    {
        string target = FreeDamagedName(path, today);
        File.Move(path, target);
        return target;
    }

    /// <summary>
    /// Writes the bytes of a damaged file that could not be renamed (another program holds it without sharing delete) to
    /// <c>settings.damaged-&lt;date&gt;.json</c>, so that replacing the file later loses nothing.
    /// </summary>
    /// <param name="path">The settings file.</param>
    /// <param name="content">The bytes that were read.</param>
    /// <param name="today">The local date for the name.</param>
    /// <returns>The copy's path.</returns>
    /// <exception cref="IOException">The copy could not be written.</exception>
    public static string CopyDamaged(string path, byte[] content, DateOnly today)
    {
        string target = FreeDamagedName(path, today);
        using (var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(content);
        }

        return target;
    }

    /// <summary>Keeps a copy of a file written by a newer Holiday Lights before this version rewrites it (once per version).</summary>
    /// <param name="path">The settings file.</param>
    /// <param name="fileVersion">The version found in the file.</param>
    /// <returns>The copy's path, or null when a copy already existed.</returns>
    /// <exception cref="IOException">The copy could not be written.</exception>
    public static string? BackUpNewerVersion(string path, int fileVersion)
    {
        string copy = Path.Combine(Path.GetDirectoryName(path)!, $"settings.v{fileVersion}.json");
        if (File.Exists(copy))
        {
            return null;
        }

        File.Copy(path, copy);
        return copy;
    }

    private static string FreeDamagedName(string path, DateOnly today)
    {
        string folder = Path.GetDirectoryName(path)!;
        string stem = $"settings.damaged-{today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        string target = Path.Combine(folder, stem + ".json");
        for (int n = 2; File.Exists(target); n++)
        {
            target = Path.Combine(folder, $"{stem} ({n}).json");
        }

        return target;
    }

    /// <summary>
    /// Reads the bytes while no other program writes the file (<see cref="File.ReadAllBytes"/> shares reading only, so the
    /// content is never half-written), retrying with growing pauses while the file is unavailable and patience lasts.
    /// </summary>
    private static byte[] ReadBytes(string path, TimeSpan patience)
    {
        long start = Stopwatch.GetTimestamp();
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (Exception e) when (IsUnavailable(e) && Stopwatch.GetElapsedTime(start) < patience)
            {
                // 50, 100, 200, 400, 400, ... ms, never past the end of the patience.
                double pause = Math.Min(50 << Math.Min(attempt, 4), MaxRetryDelayMilliseconds);
                double remaining = (patience - Stopwatch.GetElapsedTime(start)).TotalMilliseconds;
                Thread.Sleep(TimeSpan.FromMilliseconds(Math.Clamp(remaining, 1, pause)));
            }
        }
    }

    /// <summary>Decodes like <see cref="File.ReadAllText(string)"/>: UTF-8 unless a byte order mark says otherwise.</summary>
    private static string Decode(byte[] content)
    {
        using var reader = new StreamReader(new MemoryStream(content, writable: false), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static SettingsReadResult Damaged(string? problem, byte[] content) =>
        new(SettingsReadStatus.Damaged, null, problem ?? "the file is unreadable", null, [], content);
}
