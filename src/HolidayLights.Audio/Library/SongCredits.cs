namespace HolidayLights.Audio.Library;

/// <summary>The credit and category chips of a bundled song.</summary>
/// <param name="Arranger">"Arranged by" text ("Dean Burris, 1999"), or null when 5.4 named nobody.</param>
/// <param name="Categories">The chips the song belongs to.</param>
internal sealed record SongCredit(string? Arranger, IReadOnlyList<SongCategory> Categories);

/// <summary>
/// The 5.4 music credits (help topic 31) as the Arranger column and the category chips of the 46 bundled songs
/// (PRODUCT-SPEC 6.1.4). Chip counts: Christmas 31 (the songs of the 5.4 "Christmas 1" theme), Chanukah 3, Halloween 5,
/// New Year 2, Patriotic 2, Folk &amp; Classics 4 (Greensleeves is in two chips).
/// </summary>
internal static class SongCredits
{
    private const string DeanBurris = "Dean Burris, 1999";
    private const string MichaelKosacki = "Michael Kosacki, 1994";
    private const string GeorgeLarson = "George Larson, 1993";
    private const string BillBasham = "Bill Basham and Diversified Software Research, 1996";
    private const string ThomasThurston = "Thomas Thurston, 1999";
    private const string NightGallery = "Night Gallery Halloween Web site, 1995";

    /// <summary>The chip of every song in My Music.</summary>
    public static readonly IReadOnlyList<SongCategory> MySongs = [SongCategory.MySongs];

    private static readonly IReadOnlyList<SongCategory> Christmas = [SongCategory.Christmas];
    private static readonly IReadOnlyList<SongCategory> Chanukah = [SongCategory.Chanukah];
    private static readonly IReadOnlyList<SongCategory> Halloween = [SongCategory.Halloween];
    private static readonly IReadOnlyList<SongCategory> NewYear = [SongCategory.NewYear];
    private static readonly IReadOnlyList<SongCategory> Patriotic = [SongCategory.Patriotic];
    private static readonly IReadOnlyList<SongCategory> FolkAndClassics = [SongCategory.FolkAndClassics];
    private static readonly IReadOnlyList<SongCategory> ChristmasAndFolk = [SongCategory.Christmas, SongCategory.FolkAndClassics];

    /// <summary>Credits by song title (the file name without extension).</summary>
    private static readonly Dictionary<string, SongCredit> ByTitle = Build();

    /// <summary>Finds the credit of a bundled song.</summary>
    /// <param name="title">The song title (file name without extension).</param>
    /// <param name="credit">The credit.</param>
    /// <returns>True for the 46 bundled songs.</returns>
    public static bool TryGet(string title, out SongCredit credit) => ByTitle.TryGetValue(title, out credit!);

    private static Dictionary<string, SongCredit> Build()
    {
        var credits = new Dictionary<string, SongCredit>(StringComparer.OrdinalIgnoreCase);
        void Add(string arranger, IReadOnlyList<SongCategory> categories, params string[] titles)
        {
            foreach (string title in titles)
            {
                credits.Add(title, new SongCredit(arranger.Length == 0 ? null : arranger, categories));
            }
        }

        Add(DeanBurris, Christmas, "Deck the Halls (Reggae)", "Jingle Bells (Reggae)", "O Christmas Tree (Swing)", "We Wish You a Merry Christmas (Reggae)");
        Add(DeanBurris, NewYear, "Auld Lang Syne (Swing)");
        Add(DeanBurris, Chanukah, "Hava Nagilah");
        Add(MichaelKosacki, NewYear, "Auld Lang Syne");
        Add(MichaelKosacki, Chanukah, "Chanukah Song", "Dreidle");
        Add(MichaelKosacki, ChristmasAndFolk, "Greensleeves");
        Add(MichaelKosacki, Christmas, "Joy of Man's Desire");
        Add(
            GeorgeLarson,
            Christmas,
            "A Very Merry Christmas",
            "Almost Time for Christmas",
            "Angels We Have Heard On High",
            "Deck the Halls",
            "First Noel",
            "God Rest Ye Merry Gentlemen",
            "God Rest Ye Merry Gentlemen (Reggae)",
            "Good Christian Men",
            "Good King Wenceslaus",
            "Hark the Herald Angels Sing",
            "Here We Come A-Caroling",
            "In a Manger",
            "It Came Upon A Midnight",
            "Jingle Bells",
            "Jolly Old St. Nick",
            "Joy to the World",
            "O Christmas Tree",
            "O Come, All Ye Faithful",
            "O Little Town of Bethlehem",
            "Silent Night",
            "Twelve Days of Christmas",
            "We Three Kings",
            "We Wish You a Merry Christmas",
            "What Child Is This");
        Add(string.Empty, Christmas, "Joy of Man's Desire (Reggae)");
        Add(BillBasham, FolkAndClassics, "Bicycle Built for Two", "Clementine");
        Add(BillBasham, Patriotic, "Yankee Doodle Dandy");
        Add(ThomasThurston, FolkAndClassics, "Danny Boy");
        Add(
            NightGallery,
            Halloween,
            "Halloween - Eerie",
            "Halloween - Funeral March",
            "Halloween - Haunted House",
            "Halloween - Marionette",
            "Halloween - Scary");
        Add(string.Empty, Patriotic, "Star Spangled Banner");
        return credits;
    }
}
