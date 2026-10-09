namespace HolidayLights.Core.Legacy;

/// <summary>A song or picture file of a 5.4 content folder.</summary>
/// <param name="FilePath">The file (possibly a <c>.lnk</c> shortcut).</param>
/// <param name="TargetPath">The file a shortcut points to (the file itself when it is no shortcut); null when unresolvable.</param>
/// <param name="BundledId">The bundled song or picture with the same name and content, or null when the file is not bundled.</param>
internal sealed record LegacyMediaFile(string FilePath, string? TargetPath, string? BundledId)
{
    /// <summary>The file name 5.4 stored in its lists ("Jingle Bells.mid", "Family.lnk").</summary>
    public string FileName => Path.GetFileName(FilePath);
}

/// <summary>Everything <see cref="LegacyImporter.Analyze"/> found, kept for <see cref="LegacyImporter.Import"/>.</summary>
internal sealed class LegacyAnalysis
{
    /// <summary>The registry.</summary>
    public required LegacyRegistrySnapshot Snapshot { get; init; }

    /// <summary>The main key.</summary>
    public required LegacyValueSet Main { get; init; }

    /// <summary>The add-on bulb files with their 5.4 ids.</summary>
    public required LegacyAddOnFiles AddOns { get; init; }

    /// <summary>Add-on files whose bulb content is already installed (bundled or in My Bulbs), by file path.</summary>
    public required IReadOnlyDictionary<string, string> ContentMatches { get; init; }

    /// <summary>The songs 5.4 listed.</summary>
    public required IReadOnlyList<LegacyMediaFile> Songs { get; init; }

    /// <summary>The pictures 5.4 listed.</summary>
    public required IReadOnlyList<LegacyMediaFile> Pictures { get; init; }

    /// <summary>True when 5.4 was at its factory defaults (PRODUCT-SPEC 2.5.2).</summary>
    public required bool IsFactoryDefault { get; init; }
}
