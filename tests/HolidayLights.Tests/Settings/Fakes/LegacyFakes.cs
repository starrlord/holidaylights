using HolidayLights.Core.Legacy;

namespace HolidayLights.Tests.Settings.Fakes;

/// <summary>Shortcut resolution from a table; recycling is recorded, never performed.</summary>
internal sealed class FakeShellOperations : IShellOperations
{
    /// <summary>Shortcut path to target.</summary>
    public Dictionary<string, string> Shortcuts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Files passed to <see cref="MoveToRecycleBin"/>.</summary>
    public List<string> Recycled { get; } = [];

    public void OpenFolder(string path) => throw new NotSupportedException();

    public void ShowInFolder(string filePath) => throw new NotSupportedException();

    public void OpenSettingsUri(string uri) => throw new NotSupportedException();

    public void OpenProjectHomePage() => throw new NotSupportedException();

    public void OpenControlPanel(string arguments) => throw new NotSupportedException();

    public bool MoveToRecycleBin(string path)
    {
        Recycled.Add(path);
        return true;
    }

    public string? ResolveShortcut(string shortcutPath) => Shortcuts.GetValueOrDefault(shortcutPath);
}

/// <summary>A registry source that returns a fixed snapshot (null = 5.4 absent).</summary>
internal sealed class FakeRegistrySource(LegacyRegistrySnapshot? snapshot) : ILegacyRegistrySource
{
    public int Reads { get; private set; }

    public LegacyRegistrySnapshot? Read()
    {
        Reads++;
        return snapshot;
    }
}

/// <summary>Bulb headers from a table keyed by file name; unknown files are damaged.</summary>
internal sealed class FakeBulbHeaders : ILegacyBulbHeaderReader
{
    public Dictionary<string, LegacyBulbHeader> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public LegacyBulbHeader? Read(string path) => Headers.GetValueOrDefault(Path.GetFileName(path));
}

/// <summary>A clock fixed at a local time of a fixed time zone.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone("Test", now.Offset, "Test", "Test");
}
