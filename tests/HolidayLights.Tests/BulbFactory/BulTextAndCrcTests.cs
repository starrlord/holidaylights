using HolidayLights.Core.Bulbs.Writing;

namespace HolidayLights.Tests.BulbFactory;

public sealed class BulTextAndCrcTests
{
    [Fact]
    public void Crc_IsCrc32Bzip2()
    {
        Assert.Equal(0xFC891918u, Crc32Bzip2.Compute("123456789"u8));
        Assert.Equal(0u, Crc32Bzip2.Compute([]));
    }

    [Fact]
    public void Crc_MatchesTheBitwiseReferenceOnRandomData()
    {
        var random = new Random(54);
        for (int length = 0; length < 300; length += 7)
        {
            byte[] data = new byte[length];
            random.NextBytes(data);
            Assert.Equal(WritingTestData.ReferenceCrc(data), Crc32Bzip2.Compute(data));
        }
    }

    [Fact]
    public void Text_IsWindows1252()
    {
        byte[] bytes = BulText.Encode("Stjärna € ©", out bool replaced);

        Assert.False(replaced);
        Assert.Equal(new byte[] { (byte)'S', (byte)'t', (byte)'j', 0xE4, (byte)'r', (byte)'n', (byte)'a', (byte)' ', 0x80, (byte)' ', 0xA9 }, bytes);
    }

    [Fact]
    public void Text_ReplacesWhatTheCodePageCannotHold()
    {
        Assert.Equal("a?b?c?"u8.ToArray(), BulText.Encode("a中b\U0001F384c\0", out bool replaced));
        Assert.True(replaced);
        Assert.False(BulText.CanStore("Snow ☃"));
        Assert.False(BulText.CanStore("Tab\0"));
        Assert.True(BulText.CanStore("Who? Me?"));
        Assert.Equal("?"u8.ToArray(), BulText.Encode("?", out bool question));
        Assert.False(question);
    }

    [Fact]
    public void Fields_AreCutTo79Characters()
    {
        string text = new string('x', 78) + "\U0001F384" + "yz";

        byte[] bytes = BulText.EncodeField(text, out bool replaced);

        Assert.Equal(79, bytes.Length);
        Assert.Equal((byte)'?', bytes[^1]);
        Assert.True(replaced);
        Assert.False(BulText.EncodeField(new string('x', 79) + "中", out bool cutOff).Contains((byte)'?'));
        Assert.False(cutOff);
    }

    [Theory]
    [InlineData("Christmas", true)]
    [InlineData("4th", true)]
    [InlineData("Été", true)]
    [InlineData("  ", false)]
    [InlineData("--||--", false)]
    [InlineData("", false)]
    public void NewCategory_NeedsALetterOrDigit(string name, bool usable) => Assert.Equal(usable, BulCategories.IsUsableName(name));

    [Theory]
    [InlineData("  Snow | Ice  ", "Snow   Ice")]
    [InlineData("|Holly|", "Holly")]
    [InlineData("A very long category name that goes on and on", "A very long category name that goes on")]
    public void NewCategory_ReplacesBarsTrimsAndCuts(string typed, string expected) => Assert.Equal(expected, BulCategories.Normalize(typed));

    [Fact]
    public void Categories_JoinWithoutEmptyInternalOrRepeatedNames()
    {
        string text = BulCategories.Join(["Winter", " ", "_0", "christmas", "Christmas", "_1", "Snow|Ice", "WINTER"], out IReadOnlyList<string> omitted);

        Assert.Equal("Winter|christmas|Snow Ice", text);
        Assert.Empty(omitted);
    }

    [Fact]
    public void Categories_StopBeforeTheRecordWouldBeTooLongFor54()
    {
        // 24 characters each: ten names and nine bars make 249 characters, an eleventh would make 274.
        string[] names = [.. Enumerable.Range(0, 12).Select(i => $"Category number {i:00} xxxxx")];

        string text = BulCategories.Join(names, out IReadOnlyList<string> omitted);

        Assert.Equal(249, text.Length);
        Assert.Equal(names[..10], text.Split('|'));
        Assert.Equal(names[10..], omitted);
        Assert.False(BulCategories.FitInFile(names));
        Assert.True(BulCategories.FitInFile(names[..10]));
    }
}
