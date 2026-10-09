using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Bulbs.Catalog;

namespace HolidayLights.Tests.Bulbs;

public sealed class BulbInternalsTests
{
    [Fact]
    public void Windows1252_IsABijectionWithTheUndefinedBytesAsC1Controls()
    {
        for (int b = 0; b < 256; b++)
        {
            Assert.Equal((byte)b, Windows1252.ToByte(Windows1252.ToChar((byte)b)));
        }

        Assert.Equal((char)0x20AC, Windows1252.ToChar(0x80));
        Assert.Equal((char)0x81, Windows1252.ToChar(0x81));
        Assert.Equal((char)0x2122, Windows1252.ToChar(0x99));
        Assert.Equal((char)0xFF, Windows1252.ToChar(0xFF));
        Assert.Equal((byte)'?', Windows1252.ToByte((char)0x4E2D));
        Assert.Equal("ab", Windows1252.DecodeCString([(byte)'a', (byte)'b', 0, (byte)'c']));
    }

    [Fact]
    public void LegacyNameComparer_SortsLike54()
    {
        string[] names = ["b", "B2", "a", "_x", "Z", "10", "a b", "ab", (char)0xE4 + "x"];

        string[] sorted = [.. names.Order(LegacyNameComparer.Instance)];

        // Bytes compared unsigned after folding A-Z to a-z: digits, '_' (0x5F), letters, then bytes above 0x7F; a prefix
        // sorts before the longer name.
        Assert.Equal(["10", "_x", "a", "a b", "ab", "b", "B2", "Z", (char)0xE4 + "x"], sorted);
    }

    [Fact]
    public void SearchText_FoldsCaseAndAccentsAndSplitsWords()
    {
        SearchText text = SearchText.For("Jack-O-Lanterns", "D" + (char)0xE9 + "j" + (char)0xE0 + " vu", "Ann", "File");

        Assert.Equal("jack-o-lanterns", text.Name);
        Assert.Equal(["jack", "o", "lanterns"], text.NameWords);
        Assert.Equal("deja vu\nann\nfile", text.Other);
        Assert.Equal("stjarna", SearchText.Fold("STJ" + (char)0xC4 + "RNA"));
    }

    [Fact]
    public void SearchQuery_RanksByTheWeakestWord()
    {
        SearchText snowman = SearchText.For("Frosty the Snowman", "A jolly soul", "Pat", "frosty");
        string[] categories = ["christmas", "winter"];

        Assert.Equal(SearchQuery.NamePrefix, Rank("frosty the", snowman, categories));
        Assert.Equal(SearchQuery.NamePrefix, Rank("fro", snowman, categories));
        Assert.Equal(SearchQuery.NameWord, Rank("snow", snowman, categories));
        Assert.Equal(SearchQuery.NameSubstring, Rank("owma", snowman, categories));
        Assert.Equal(SearchQuery.Category, Rank("wint", snowman, categories));
        Assert.Equal(SearchQuery.OtherField, Rank("jolly", snowman, categories));
        Assert.Equal(SearchQuery.OtherField, Rank("snow jolly", snowman, categories));
        Assert.Equal(SearchQuery.NoMatch, Rank("snow zebra", snowman, categories));
        Assert.Null(SearchQuery.Parse(" \t "));
    }

    [Fact]
    public void CategoryTally_MergesCaseAndPicksTheCommonSpelling()
    {
        var tally = new CategoryTally();
        tally.Add("a", ["Christmas", "christmas", ""]);
        tally.Add("b", ["Christmas"]);
        tally.Add("c", ["christmas", "Zoo"]);
        tally.Add("c", ["Zoo"]);

        Assert.Equal([new BulbCategoryCount("Christmas", 3), new BulbCategoryCount("Zoo", 1)], tally.ToCounts());
        Assert.Equal(["Christmas", "Zoo"], tally.ToNames());
    }

    [Fact]
    public void DecodedArtCache_EvictsTheLeastRecentlyUsed()
    {
        var cache = new DecodedArtCache(2 * 10 * 4);
        Rgba32Image[] Frames() => [new Rgba32Image(10, 1)];
        int decodes = 0;
        Rgba32Image[] Decode()
        {
            decodes++;
            return Frames();
        }

        Rgba32Image[] a = cache.GetOrAdd("a", Decode);
        cache.GetOrAdd("b", Decode);
        Assert.Same(a, cache.GetOrAdd("a", Decode));
        cache.GetOrAdd("c", Decode);
        Assert.Equal(3, decodes);
        Assert.Same(a, cache.GetOrAdd("a", Decode));
        cache.GetOrAdd("b", Decode);

        Assert.Equal(4, decodes);
        Assert.Equal(80, cache.SizeBytes);
        Assert.Same(cache.Add("b", Frames()), cache.GetOrAdd("b", Decode));
    }

    [Fact]
    public void DecodedArtCache_KeepsAnOversizedEntry()
    {
        var cache = new DecodedArtCache(4);

        Rgba32Image[] big = cache.GetOrAdd("big", () => [new Rgba32Image(5, 5)]);

        Assert.Same(big, cache.GetOrAdd("big", () => throw new InvalidOperationException()));
    }

    [Fact]
    public void LightBulbClassifier_NeedsIdenticalMasksAndAClearlyBrighterFrame()
    {
        Rgba32Image lit = Image([0xFFFFFF00, 0xFFFFFF00, 0, 0xFF101010]);
        Rgba32Image unlit = Image([0xFF808000, 0xFF808000, 0, 0xFF101010]);
        Rgba32Image otherMask = Image([0xFF808000, 0, 0, 0xFF101010]);
        Rgba32Image almostSame = Image([0xFFF0F000, 0xFFFFFF00, 0, 0xFF101010]);

        Assert.True(LightBulbClassifier.TryClassify(lit, unlit, out int litFrame));
        Assert.Equal(0, litFrame);
        Assert.True(LightBulbClassifier.TryClassify(unlit, lit, out litFrame));
        Assert.Equal(1, litFrame);
        Assert.False(LightBulbClassifier.TryClassify(lit, otherMask, out _));
        Assert.False(LightBulbClassifier.TryClassify(lit, almostSame, out _));
        Assert.False(LightBulbClassifier.TryClassify(Image([0, 0, 0, 0]), Image([0, 0, 0, 0]), out _));
        Assert.False(LightBulbClassifier.TryClassify(lit, new Rgba32Image(1, 1), out _));
    }

    [Theory]
    [InlineData("Sweet Hearts")]
    [InlineData("Autumn Leaves")]
    [InlineData("Cornucopias")]
    [InlineData("Dead Turkeys")]
    [InlineData("Chili Peppers")]
    [InlineData("Laundry")]
    [InlineData("Religious Icons")]
    public void LightBulbClassifier_RecognizesBuiltInPairsWhoseUnlitFrameIsTheLitFrameHalved(string name)
    {
        // For these seven the unlit frame is exactly the lit frame with every channel halved.
        IBulb bulb = BuiltInBulbs.Load().First(b => b.Name == name);

        Assert.True(LightBulbClassifier.TryClassify(bulb.GetCell(CellSlot.Top, 0, 0).Image, bulb.GetCell(CellSlot.Top, 0, 1).Image, out int lit));
        Assert.Equal(0, lit);
    }

    private static int Rank(string search, SearchText text, string[] categories) => SearchQuery.Parse(search)!.Rank(text, categories);

    private static Rgba32Image Image(uint[] pixels) => new(pixels.Length, 1, pixels);
}
