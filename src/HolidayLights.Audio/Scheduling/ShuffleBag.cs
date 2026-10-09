namespace HolidayLights.Audio.Scheduling;

/// <summary>
/// The 5.4 shuffle bag (golden <c>shuffle.json</c>): every eligible
/// (checked and playable) song plays once per round in random order; a new round contains every eligible song except
/// the one picked last, unless it is the only one, so no song plays twice in a row. A rescan creates fresh records (every
/// song unplayed) and keeps the last pick, exactly as 5.4 did.
/// </summary>
/// <remarks>Songs are identified by id instead of 5.4's list index, so the last pick stays the same song across a rescan.</remarks>
internal sealed class ShuffleBag
{
    private readonly IMusicRandom random;
    private string[] songIds = [];
    private bool[] unplayed = [];

    /// <summary>Creates an empty bag.</summary>
    /// <param name="random">One draw per non-empty pick: 5.4 used <c>rand() % remaining</c>.</param>
    public ShuffleBag(IMusicRandom random) => this.random = random;

    /// <summary>The song picked last (kept across rescans, as in 5.4), or null.</summary>
    public string? LastPicked { get; private set; }

    /// <summary>Fresh records for a new song list (5.4 <c>SongList_Rescan</c>): every song unplayed; the last pick is kept.</summary>
    /// <param name="ids">Song ids in list order (the 5.4 order).</param>
    public void Rescan(IReadOnlyList<string> ids)
    {
        songIds = [.. ids];
        unplayed = new bool[songIds.Length];
        Array.Fill(unplayed, true);
    }

    /// <summary>Picks the next song.</summary>
    /// <param name="isEligible">True for checked, playable songs.</param>
    /// <returns>The song id, or null when no song is eligible (nothing is drawn then).</returns>
    public string? Pick(Func<string, bool> isEligible)
    {
        var eligibleAt = new bool[songIds.Length];
        int eligible = 0;
        int remaining = 0;
        for (int i = 0; i < songIds.Length; i++)
        {
            if (isEligible(songIds[i]))
            {
                eligibleAt[i] = true;
                eligible++;
                if (unplayed[i])
                {
                    remaining++;
                }
            }
        }

        if (eligible == 0)
        {
            return null;
        }

        if (remaining == 0)
        {
            for (int i = 0; i < songIds.Length; i++)
            {
                if (eligibleAt[i] && (eligible == 1 || !MediaIds.Comparer.Equals(songIds[i], LastPicked)))
                {
                    unplayed[i] = true;
                    remaining++;
                }
            }
        }

        int r = random.Next(remaining);
        for (int i = 0; i < songIds.Length; i++)
        {
            if (eligibleAt[i] && unplayed[i] && r-- == 0)
            {
                unplayed[i] = false;
                LastPicked = songIds[i];
                return songIds[i];
            }
        }

        throw new InvalidOperationException("The shuffle bag lost count of its songs.");
    }
}
