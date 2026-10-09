using System.Text.Json;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Branding;

/// <summary>The credits of About and Help (PRODUCT-SPEC 6.5, 6.1.4): <c>credits.json</c> and the Credits topics.</summary>
public sealed class CreditsTests
{
    private static readonly Lazy<CreditsContent> Credits = new(HelpContentStore.LoadCredits);

    [Fact]
    public void FixedTextsAreTheSpecificationsWords()
    {
        CreditsContent credits = Credits.Value;

        Assert.Equal("holidaylights.credits/1", credits.Schema);
        Assert.Equal("Holiday Lights 5.4 for Windows, Copyright 1993-2003 Tiger Technologies. Holiday Lights was created by Tiger Technologies. Holiday Lights is a registered trademark of Tiger Technologies.", credits.OriginalProgram);
        Assert.Equal("The background pictures are licensed from ArtToday. The Dancing Demon, Gingerbread Man and Singing Tree animations are licensed from web-ready.com; the Angel, Santa and Skeleton animations from XOOM, Inc.", credits.ScreenSaverArt);
        Assert.Equal("All artwork and music are copyrighted by their authors and may not be used for other purposes without their permission.", credits.ArtworkNotice);
        Assert.Equal("Holiday Lights works completely offline and never collects information about you.", credits.PrivacyNotice);
    }

    [Fact]
    public void BuiltInArtistsCreditEveryBuiltInBulb()
    {
        using JsonDocument table = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "assets", "builtin", "table.json")));
        string[] builtIn = [.. table.RootElement.GetProperty("bulbs").EnumerateArray().Select(b => b.GetProperty("name").GetString()!)];
        IReadOnlyList<ArtistCredit> artists = Credits.Value.BuiltInArtists;

        Assert.Equal(49, builtIn.Length);
        Assert.Equal(["Joe Lachoff", "Robert L. Mathews", "Mark Pettus, Mighty Toad Software", "Mary Lee Seward", "Gail Allinson", "Apple Computer, Inc."], artists.Select(a => a.Name));
        Assert.All(artists, a => Assert.Equal(a.Bulbs.Count, a.BulbCount));
        Assert.Equal(builtIn.Order(StringComparer.Ordinal), artists.SelectMany(a => a.Bulbs).Distinct().Order(StringComparer.Ordinal));

        // Old Glory Bulbs was drawn by two artists; every other bulb by one. Each artist's bulbs are in the 5.4 table order.
        Assert.Equal(50, artists.Sum(a => a.BulbCount));
        Assert.Equal(["Joe Lachoff", "Robert L. Mathews"], artists.Where(a => a.Bulbs.Contains("Old Glory Bulbs")).Select(a => a.Name));
        Assert.All(artists, a => Assert.Equal(builtIn.Where(a.Bulbs.Contains), a.Bulbs));
        Assert.Equal(
            ["Jack-O-Lanterns", "Zombie Tombstones", "The Grim Reaper", "Autumn Leaves", "Cornucopias", "Live Turkeys", "Dead Turkeys"],
            artists.Single(a => a.Name == "Mark Pettus, Mighty Toad Software").Bulbs);
        Assert.Equal(["Spring Flowers", "Ghosts"], artists.Single(a => a.Name == "Mary Lee Seward").Bulbs);
        Assert.Equal(["North Pole Express", "Flight Lights"], artists.Single(a => a.Name == "Gail Allinson").Bulbs);
        Assert.Equal("based on art included with Mac OS, Copyright 1983-1997 Apple Computer, Inc.", artists.Single(a => a.Bulbs.Contains("Party Hats")).Note);
    }

    [Fact]
    public void AddOnArtistsAreComputedFromTheBundledBulbFiles()
    {
        IReadOnlyList<(string Name, int Count)> expected = AddOnArtistCredits.FromFolder(Path.Combine(TestPaths.ContentFolder, "Bulbs"));
        IReadOnlyList<ArtistCredit> actual = Credits.Value.AddOnArtists;

        Assert.Equal(expected.Select(a => (a.Name, a.Count)), actual.Select(a => (a.Name, a.BulbCount)));
        Assert.Equal(1501, actual.Sum(a => a.BulbCount));
        Assert.InRange(actual.Count, 360, 420);
        Assert.All(actual, a => Assert.Empty(a.Bulbs));
    }

    [Fact]
    public void ArtCopyrightTopicEndsWithTheAddOnArtists()
    {
        IReadOnlyList<(string Name, int Count)> artists = [.. Credits.Value.AddOnArtists.Select(a => (a.Name, a.BulbCount))];
        string topic = HelpContentStore.ReadTopicMarkdown(HelpTopics.ArtCopyright).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.EndsWith("\n\n" + AddOnArtistCredits.MarkdownSection(artists), topic, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Created by Pat Butler ...\r\nAlaNanny@aol.com", "Pat Butler")]
    [InlineData("Created with pride & prayers by Pat Butler ...", "Pat Butler")]
    [InlineData("Special Request created with pride & prayers by Pat Butler ...", "Pat Butler")]
    [InlineData("Created by Dustin MacDonald.", "Dustin MacDonald")]
    [InlineData("By: Tyler Canning\r\n       tcanning@rmci.net", "Tyler Canning")]
    [InlineData("Copyight: Mark Dillon December 21st 2000\r\ntoocooldood@aol.com", "Mark Dillon")]
    [InlineData("Copyright 09/Dec/2000 Mark Dillon. \r\ntoocooldood@aol.com", "Mark Dillon")]
    [InlineData("Mark Dillon at: markus-d@ntlworld.com", "Mark Dillon")]
    [InlineData("Lee Hericks \\ LeWHe@juno.com", "Lee Hericks")]
    [InlineData("Lee Hericks / LeWHe@juno.com", "Lee Hericks")]
    [InlineData("Vaughn Drinen (DBZ@Habiki.Com)", "Vaughn Drinen")]
    [InlineData("Chris Allen, Crissy9898@aol.com\r\n", "Chris Allen")]
    [InlineData("diva_sarah@hotmail.com", "diva_sarah")]
    [InlineData("D. StotzCDSIV@aol.com\r\nCDSIV@aol.com", "D. Stotz")]
    [InlineData("CYNTHIA  H. KENT", "CYNTHIA H. KENT")]
    [InlineData("Neo-Link,\r\ntassadar@softhome.net", "Neo-Link")]
    [InlineData("Joe Lachoff, for Tiger Technologies\r\nsupport@tigertech.com", "Joe Lachoff, for Tiger Technologies")]
    public void AddOnArtistRulesFollowTheSpecification(string authorField, string artist) =>
        Assert.Equal(artist, AddOnArtistCredits.Artist(authorField));

    [Fact]
    public void SpellingsOfOneArtistMerge()
    {
        Assert.Equal(AddOnArtistCredits.MergeKey("G.P. Barsby"), AddOnArtistCredits.MergeKey("G.P.Barsby"));
        Assert.Equal(AddOnArtistCredits.MergeKey("J. Perkins"), AddOnArtistCredits.MergeKey("JPerkins"));
        Assert.Equal(AddOnArtistCredits.MergeKey("teresa godeaux"), AddOnArtistCredits.MergeKey("Teresa Godeaux"));
        Assert.NotEqual(AddOnArtistCredits.MergeKey("Lisa Harshman"), AddOnArtistCredits.MergeKey("Lisa M. Harshman"));
    }

    [Fact]
    public void EastAsianArtistNamesAreReadInTheirCodePage()
    {
        string bulbs = Path.Combine(TestPaths.ContentFolder, "Bulbs");

        Assert.StartsWith("桜井 達也", AddOnArtistCredits.ReadAuthorField(Path.Combine(bulbs, "Doru.bul")), StringComparison.Ordinal);
        Assert.StartsWith("김의수", AddOnArtistCredits.ReadAuthorField(Path.Combine(bulbs, "YesMan.bul")), StringComparison.Ordinal);
        Assert.Contains("© Poison's Icons", AddOnArtistCredits.ReadAuthorField(Path.Combine(bulbs, "EagleBorder.bul")), StringComparison.Ordinal);
    }

    [Fact]
    public void MusicCreditsNameEveryArrangerOfTheBundledSongs()
    {
        IReadOnlyList<MusicCredit> music = Credits.Value.Music;
        string[] bundled = [.. Directory.EnumerateFiles(Path.Combine(TestPaths.ContentFolder, "Music"), "*.mid").Select(Path.GetFileNameWithoutExtension)!];

        Assert.Equal(46, bundled.Length);
        Assert.Equal(
            ["Dean Burris, 1999", "Michael Kosacki, 1994", "George Larson, 1993", "Bill Basham and Diversified Software Research, 1996", "Thomas Thurston, 1999", "Night Gallery Halloween Web site, 1995"],
            music.Select(m => m.Arranger));
        string[] credited = [.. music.SelectMany(m => m.Songs)];
        Assert.Equal(credited.Length, credited.Distinct().Count());
        Assert.All(credited, song => Assert.Contains(song, bundled));

        // Holiday Lights 5.4 named no arranger for these two songs (PRODUCT-SPEC 6.1.4).
        Assert.Equal(["Joy of Man's Desire (Reggae)", "Star Spangled Banner"], bundled.Except(credited).Order(StringComparer.Ordinal));
        Assert.Equal(24, music.Single(m => m.Arranger.StartsWith("George Larson", StringComparison.Ordinal)).Songs.Count);
    }

    [Fact]
    public void MusicTopicMatchesTheMusicCredits()
    {
        string topic = HelpContentStore.ReadTopicMarkdown(HelpTopics.MusicCopyright);

        Assert.All(Credits.Value.Music, m =>
        {
            string row = topic.Split('\n').Single(l => l.StartsWith($"| {m.Arranger} |", StringComparison.Ordinal));
            Assert.All(m.Songs, song => Assert.Contains(song, row, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void SoftwareCreditsCarryTheirMitLicenses()
    {
        IReadOnlyList<SoftwareCredit> software = Credits.Value.Software;
        string topic = HelpContentStore.ReadTopicMarkdown(HelpTopics.SoftwareCredits);

        Assert.Equal(["MMPX pixel-art magnification", "Vortice.Windows", "NAudio", "H.NotifyIcon", "VirtualizingWrapPanel", ".NET and WPF"], software.Select(s => s.Name));
        Assert.All(software, s =>
        {
            Assert.Equal("MIT", s.License);
            Assert.StartsWith($"MIT License\n\n{s.Copyright}\n\nPermission is hereby granted, free of charge,", s.LicenseText, StringComparison.Ordinal);
            Assert.EndsWith("OTHER DEALINGS IN THE SOFTWARE.", s.LicenseText, StringComparison.Ordinal);
            Assert.Contains($"| {s.Name} | {s.Copyright} |", topic, StringComparison.Ordinal);
        });
    }
}
