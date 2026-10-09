namespace HolidayLights.Audio.Scheduling;

/// <summary>The songs that played, newest last, for "Previous" (PRODUCT-SPEC 3.4.2).</summary>
internal sealed class SongHistory
{
    private const int Capacity = 50;

    private readonly List<string> songIds = [];
    private int cursor = -1;

    /// <summary>The song "Previous" restarts when nothing plays: the one played last, or the one stepped back to.</summary>
    public string? Current => cursor >= 0 ? songIds[cursor] : null;

    /// <summary>Records a song that started; songs after a step back are forgotten.</summary>
    /// <param name="songId">The song.</param>
    public void Add(string songId)
    {
        songIds.RemoveRange(cursor + 1, songIds.Count - cursor - 1);
        songIds.Add(songId);
        if (songIds.Count > Capacity)
        {
            songIds.RemoveAt(0);
        }

        cursor = songIds.Count - 1;
    }

    /// <summary>Moves to the song before the current one.</summary>
    /// <param name="exists">True for songs that are still listed (removed ones are skipped).</param>
    /// <returns>The earlier song, or null when there is none.</returns>
    public string? StepBack(Func<string, bool> exists)
    {
        for (int i = cursor - 1; i >= 0; i--)
        {
            if (exists(songIds[i]))
            {
                cursor = i;
                return songIds[i];
            }
        }

        return null;
    }
}
