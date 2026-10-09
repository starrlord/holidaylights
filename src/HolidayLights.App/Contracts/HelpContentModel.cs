using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HolidayLights.App.Contracts;

/// <summary>One topic entry of the Help contents.</summary>
/// <param name="Id">A <see cref="HelpTopics"/> id; the Markdown is <c>HelpContent/topics/&lt;id&gt;.md</c>.</param>
/// <param name="Title">The title shown in the Contents tree.</param>
public sealed record HelpTopicEntry(string Id, string Title);

/// <summary>One book of the Help contents (5.4 books kept, the payment book removed, "Credits" added).</summary>
/// <param name="Title">The book title, e.g. "Getting Started".</param>
/// <param name="Topics">The topics in order.</param>
public sealed record HelpBook(string Title, IReadOnlyList<HelpTopicEntry> Topics);

/// <summary>The Help contents (<c>HelpContent/contents.json</c>, schema <c>holidaylights.help/1</c>).</summary>
/// <param name="Schema">The schema identifier.</param>
/// <param name="Books">The books in order.</param>
public sealed record HelpContents(string Schema, IReadOnlyList<HelpBook> Books);

/// <summary>An artist and the bulbs they drew (About and Help "Credits").</summary>
public sealed record ArtistCredit
{
    /// <summary>The artist (normalized per PRODUCT-SPEC 6.5 for add-on artists).</summary>
    public string Name { get; set; } = "";

    /// <summary>Bulb names (built-in artists); empty for add-on artists.</summary>
    public IReadOnlyList<string> Bulbs { get; set; } = [];

    /// <summary>Number of bulbs.</summary>
    public int BulbCount { get; set; }

    /// <summary>Extra text, e.g. "based on art included with Mac OS, Copyright 1983-1997 Apple Computer, Inc.", or null.</summary>
    public string? Note { get; set; }
}

/// <summary>A music arranger and their songs (PRODUCT-SPEC 6.1.4).</summary>
/// <param name="Arranger">E.g. "Dean Burris, 1999".</param>
/// <param name="Songs">Song titles.</param>
public sealed record MusicCredit(string Arranger, IReadOnlyList<string> Songs);

/// <summary>A software component and its licence (PRODUCT-SPEC 6.5 item 6).</summary>
/// <param name="Name">E.g. "NAudio".</param>
/// <param name="Copyright">The copyright line.</param>
/// <param name="License">The licence name, e.g. "MIT".</param>
/// <param name="LicenseText">The full licence text ("View License").</param>
public sealed record SoftwareCredit(string Name, string Copyright, string License, string LicenseText);

/// <summary>
/// The credits of About and Help (<c>HelpContent/credits.json</c>, schema <c>holidaylights.credits/1</c>; PRODUCT-SPEC 6.5).
/// Setters exist for source-generated JSON (missing values keep their defaults); treat instances as immutable.
/// </summary>
public sealed record CreditsContent
{
    /// <summary>The schema identifier.</summary>
    public string Schema { get; set; } = "holidaylights.credits/1";

    /// <summary>Item 1: the original program text.</summary>
    public string OriginalProgram { get; set; } = "";

    /// <summary>Item 2: built-in bulb artists with their bulbs.</summary>
    public IReadOnlyList<ArtistCredit> BuiltInArtists { get; set; } = [];

    /// <summary>Item 3: every distinct add-on bulb author (about 410), A-Z with bulb counts.</summary>
    public IReadOnlyList<ArtistCredit> AddOnArtists { get; set; } = [];

    /// <summary>Item 4: the arrangers with their songs.</summary>
    public IReadOnlyList<MusicCredit> Music { get; set; } = [];

    /// <summary>Item 5: screen saver art.</summary>
    public string ScreenSaverArt { get; set; } = "";

    /// <summary>Item 6: software and licences.</summary>
    public IReadOnlyList<SoftwareCredit> Software { get; set; } = [];

    /// <summary>Item 7: "All artwork and music are copyrighted by their authors ...".</summary>
    public string ArtworkNotice { get; set; } = "";

    /// <summary>Item 7: "Holiday Lights works completely offline and never collects information about you."</summary>
    public string PrivacyNotice { get; set; } = "";
}

/// <summary>Source-generated JSON metadata of the help and credits files.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(HelpContents))]
[JsonSerializable(typeof(CreditsContent))]
public sealed partial class HelpContentJsonContext : JsonSerializerContext;

/// <summary>
/// Reads the help content embedded in the App assembly (logical names <c>help/&lt;path&gt;</c>, from the
/// <c>HelpContent</c> folder owned by branding-docs).
/// </summary>
public static class HelpContentStore
{
    private const string Prefix = "help/";

    private static readonly Assembly Assembly = typeof(HelpContentStore).Assembly;

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Names = new(() =>
        Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
            .ToDictionary(n => n[Prefix.Length..].Replace('\\', '/'), n => n, StringComparer.OrdinalIgnoreCase));

    /// <summary>True when a help file exists (e.g. <c>topics/welcome.md</c>).</summary>
    /// <param name="path">Path relative to <c>HelpContent</c>, with forward slashes.</param>
    /// <returns>True when embedded.</returns>
    public static bool Exists(string path) => Names.Value.ContainsKey(path);

    /// <summary>Opens a help file (images, Markdown, JSON).</summary>
    /// <param name="path">Path relative to <c>HelpContent</c>, with forward slashes.</param>
    /// <returns>A read-only stream; dispose it.</returns>
    /// <exception cref="FileNotFoundException">The file is not embedded.</exception>
    public static Stream Open(string path) =>
        Names.Value.TryGetValue(path, out string? resource) && Assembly.GetManifestResourceStream(resource) is { } stream
            ? stream
            : throw new FileNotFoundException($"Help file '{path}' not found.", path);

    /// <summary>Reads <c>contents.json</c>.</summary>
    /// <returns>The contents.</returns>
    public static HelpContents LoadContents()
    {
        using Stream stream = Open("contents.json");
        return JsonSerializer.Deserialize(stream, HelpContentJsonContext.Default.HelpContents)
            ?? throw new InvalidDataException("contents.json is empty.");
    }

    /// <summary>Reads the Markdown of a topic.</summary>
    /// <param name="topicId">A <see cref="HelpTopics"/> id.</param>
    /// <returns>The Markdown text.</returns>
    public static string ReadTopicMarkdown(string topicId)
    {
        using var reader = new StreamReader(Open($"topics/{topicId}.md"));
        return reader.ReadToEnd();
    }

    /// <summary>Reads <c>credits.json</c>.</summary>
    /// <returns>The credits.</returns>
    public static CreditsContent LoadCredits()
    {
        using Stream stream = Open("credits.json");
        return JsonSerializer.Deserialize(stream, HelpContentJsonContext.Default.CreditsContent)
            ?? throw new InvalidDataException("credits.json is empty.");
    }
}
