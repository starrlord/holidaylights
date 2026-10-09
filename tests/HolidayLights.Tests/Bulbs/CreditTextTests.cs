using HolidayLights.Core.Bulbs;

namespace HolidayLights.Tests.Bulbs;

/// <summary>Review r1 #14 (PO decision 4): Shift-JIS and CP949 credits read as written; every other bulb keeps Windows-1252.</summary>
public sealed class CreditTextTests
{
    // The bytes of Doru.bul and YesMan.bul (author field at 0x10C, copyright at 0xBC), as BulFile reads them.
    private static readonly string DoruAuthor = Windows1252.Decode([0x8D, 0xF7, 0x88, 0xE4, 0x20, 0x92, 0x42, 0x96, 0xE7, 0x0D, 0x0A, .. "sakutatsu@can.bekkoame.or.jp"u8]);
    private static readonly string DoruCopyright = Windows1252.Decode([.. "Copyright 1998 "u8, 0x8D, 0xF7, 0x88, 0xE4, 0x20, 0x92, 0x42, 0x96, 0xE7]);
    private static readonly string YesManAuthor = Windows1252.Decode([0xB1, 0xE8, 0xC0, 0xC7, 0xBC, 0xF6, 0x0D, 0x0A, .. "eskim@kde.co.kr"u8]);
    private static readonly string YesManCopyright = Windows1252.Decode([.. "Copyright 1998 "u8, 0xB1, 0xE8, 0xC0, 0xC7, 0xBC, 0xF6]);

    [Fact]
    public void JapaneseAndKoreanCredits_AreReadInTheirCodePages()
    {
        Assert.Equal(("桜井 達也\r\nsakutatsu@can.bekkoame.or.jp", "Copyright 1998 桜井 達也"), CreditText.Decode(DoruAuthor, DoruCopyright));
        Assert.Equal(("김의수\r\neskim@kde.co.kr", "Copyright 1998 김의수"), CreditText.Decode(YesManAuthor, YesManCopyright));
    }

    [Fact]
    public void WesternCredits_StayWindows1252()
    {
        // "©" is a valid one-byte Shift-JIS character: without a double-byte character nothing changes, even with a .jp address.
        Assert.Equal(("Leigh McVey\r\nGrArts@hotmail.com", "©2001 GrArts"), CreditText.Decode("Leigh McVey\r\nGrArts@hotmail.com", "©2001 GrArts"));
        Assert.Equal(("Ann\r\nann@example.jp", "Copyright © 2001 Ann"), CreditText.Decode("Ann\r\nann@example.jp", "Copyright © 2001 Ann"));
        Assert.Equal((DoruAuthor.Replace(".jp", ".com", StringComparison.Ordinal), DoruCopyright), CreditText.Decode(DoruAuthor.Replace(".jp", ".com", StringComparison.Ordinal), DoruCopyright));
    }

    [Fact]
    public void DecodedText_IsLeftAsItIs()
    {
        (string author, string copyright) = CreditText.Decode(DoruAuthor, DoruCopyright);
        Assert.Equal((author, copyright), CreditText.Decode(author, copyright));
    }

    [Fact]
    public void TheCatalog_ShowsAndSearchesTheArtistsAsTheyWroteTheirNames()
    {
        using var harness = new CatalogHarness(["Doru.bul", "YesMan.bul", "Arrow.bul"]);
        BulbCatalog catalog = harness.Start();

        Assert.True(catalog.TryGetInfo("addon:Doru", out BulbInfo? doru));
        Assert.StartsWith("桜井 達也", doru.Author, StringComparison.Ordinal);
        Assert.Equal("Copyright 1998 桜井 達也", doru.Copyright);
        Assert.True(catalog.TryGetInfo("addon:YesMan", out BulbInfo? yesMan));
        Assert.StartsWith("김의수", yesMan.Author, StringComparison.Ordinal);

        Assert.Equal(["addon:Doru"], catalog.Query(new BulbQuery { SearchText = "桜井" }).Select(b => b.Id));
        Assert.Equal(["addon:YesMan"], catalog.Query(new BulbQuery { SearchText = "김의수" }).Select(b => b.Id));
        Assert.Equal(["addon:Doru"], catalog.Query(new BulbQuery { SearchText = "sakutatsu" }).Select(b => b.Id));
    }
}
