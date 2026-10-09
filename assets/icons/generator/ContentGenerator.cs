using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using HolidayLights.Tests.Branding;

namespace HolidayLights.Branding;

/// <summary>
/// Writes the generated parts of the help content: the add-on bulb artists (in <c>credits.json</c> and at the end of the
/// Art Copyright Information topic) and <c>docs/USER-GUIDE.md</c>, the Help topics as one document.
/// </summary>
internal static class ContentGenerator
{
    /// <summary>The heading that starts the generated part of the Art Copyright Information topic.</summary>
    public const string ArtistsHeading = "## Add-On Bulb Artists";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Computes the add-on artists from the bundled bulb files and writes them into the credits and the topic.</summary>
    /// <param name="paths">Repository locations.</param>
    public static void Credits(RepoPaths paths)
    {
        IReadOnlyList<(string Name, int Count)> artists = AddOnArtistCredits.FromFolder(Path.Combine(paths.Root, "content", "Bulbs"));

        string creditsPath = Path.Combine(paths.HelpContent, "credits.json");
        JsonNode credits = JsonNode.Parse(File.ReadAllText(creditsPath)) ?? throw new InvalidOperationException("credits.json is empty.");
        credits["addOnArtists"] = new JsonArray([.. artists.Select(a => (JsonNode)new JsonObject { ["name"] = a.Name, ["bulbCount"] = a.Count })]);
        WriteText(creditsPath, credits.ToJsonString(JsonOptions) + "\n");

        string topicPath = Path.Combine(paths.HelpContent, "topics", "art-copyright.md");
        string topic = File.ReadAllText(topicPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        int start = topic.IndexOf(ArtistsHeading, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"art-copyright.md has no \"{ArtistsHeading}\" section.");
        }

        WriteText(topicPath, topic[..start] + AddOnArtistCredits.MarkdownSection(artists));
        Console.WriteLine($"Credits written: {artists.Count} add-on artists, {artists.Sum(a => a.Count)} bulbs.");
    }

    /// <summary>Writes <c>docs/USER-GUIDE.md</c> from the Help contents and topics.</summary>
    /// <param name="paths">Repository locations.</param>
    public static void UserGuide(RepoPaths paths)
    {
        string contents = File.ReadAllText(Path.Combine(paths.HelpContent, "contents.json"));
        string guide = UserGuideComposer.Compose(contents, id => File.ReadAllText(Path.Combine(paths.HelpContent, "topics", id + ".md")));
        string path = Path.Combine(paths.Root, UserGuideComposer.GuidePath);
        WriteText(path, guide);
        Console.WriteLine($"User guide written to {path}.");
    }

    private static void WriteText(string path, string text) => File.WriteAllText(path, text, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}
