using System.Diagnostics;
using System.Text.Json;
using HolidayLights.Audio.Library;
using HolidayLights.Tests.Shared;
using NAudio.Wave;
using static HolidayLights.Tests.Audio.SmfBuilder;

namespace HolidayLights.Tests.Audio;

/// <summary>The Music Box library: bundled songs with their credits and chips, My Music, shortcuts, adding and removing.</summary>
public sealed class MusicLibraryTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly TempDataRoot root = new();
    private readonly InMemorySettingsStore settings = new();
    private readonly FakeShellOperations shell = new();
    private readonly TestHoldingFolder holding;
    private readonly string myMusic;
    private readonly string sources;
    private MusicLibrary? library;

    public MusicLibraryTests()
    {
        holding = new TestHoldingFolder(root.Paths);
        myMusic = DataPaths.EnsureFolder(root.Paths.MyMusicFolder);
        sources = DataPaths.EnsureFolder(Path.Combine(root.Root, "Sources"));
    }

    [Fact]
    public void Bundled_songs_are_listed_in_the_5_4_order_with_credits_and_chips()
    {
        MusicLibrary songs = StartLibrary();

        Assert.Equal(46, songs.Songs.Count);
        Assert.Equal(
            ["A Very Merry Christmas", "Almost Time for Christmas", "Angels We Have Heard On High", "Auld Lang Syne (Swing)", "Auld Lang Syne", "Bicycle Built for Two"],
            songs.Songs.Take(6).Select(s => s.Title));
        Assert.All(songs.Songs, s => Assert.Equal((MediaOrigin.Bundled, SongKind.Midi, MediaIds.Bundled(s.Title + ".mid")), (s.Origin, s.Kind, s.Id)));
        var chips = songs.Songs.SelectMany(s => s.Categories).GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(31, chips[SongCategory.Christmas]);
        Assert.Equal(3, chips[SongCategory.Chanukah]);
        Assert.Equal(5, chips[SongCategory.Halloween]);
        Assert.Equal(2, chips[SongCategory.NewYear]);
        Assert.Equal(2, chips[SongCategory.Patriotic]);
        Assert.Equal(4, chips[SongCategory.FolkAndClassics]);
        Assert.False(chips.ContainsKey(SongCategory.MySongs));
        Assert.Equal("Dean Burris, 1999", Song(songs, "Jingle Bells (Reggae)").Arranger);
        Assert.Equal("George Larson, 1993", Song(songs, "God Rest Ye Merry Gentlemen (Reggae)").Arranger);
        Assert.Equal("Night Gallery Halloween Web site, 1995", Song(songs, "Halloween - Scary").Arranger);
        Assert.Null(Song(songs, "Star Spangled Banner").Arranger);
        Assert.Null(Song(songs, "Joy of Man's Desire (Reggae)").Arranger);
        Assert.Equal([SongCategory.Christmas, SongCategory.FolkAndClassics], Song(songs, "Greensleeves").Categories);
    }

    [Fact]
    public void The_christmas_chip_is_exactly_the_songs_of_the_5_4_christmas_1_theme()
    {
        using JsonDocument themes = GoldenData.ReadJson("default-themes.json");
        IEnumerable<string> christmas1 = themes.RootElement.GetProperty("themes").GetProperty("Christmas 1")
            .GetProperty("Enabled Music").GetProperty("decoded").EnumerateArray().Select(e => e.GetString()!);

        MusicLibrary songs = StartLibrary();

        Assert.Equal(
            christmas1.Select(MediaIds.Bundled).Order(StringComparer.Ordinal),
            songs.Songs.Where(s => s.Categories.Contains(SongCategory.Christmas)).Select(s => s.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Lengths_are_read_in_the_background()
    {
        MusicLibrary songs = StartLibrary();

        WaitFor(() => songs.Songs.All(s => s.Length is not null), "song lengths");

        Assert.Equal(TimeSpan.FromSeconds(44), Song(songs, "Jingle Bells").Length);
        Assert.Equal(TimeSpan.FromMicroseconds(52_116_666), Song(songs, "Chanukah Song").Length);
        Assert.All(songs.Songs, s => Assert.True(s.IsPlayable));
    }

    [Fact]
    public void My_music_lists_your_songs_and_skips_other_and_hidden_files()
    {
        WriteSong(Path.Combine(myMusic, "Zebra Dance.mid"));
        File.WriteAllText(Path.Combine(myMusic, "notes.txt"), "not music");
        string hidden = Path.Combine(myMusic, "Secret.mid");
        WriteSong(hidden);
        File.SetAttributes(hidden, FileAttributes.Hidden);

        MusicLibrary songs = StartLibrary();

        SongInfo mine = Assert.Single(songs.Songs, s => s.Origin == MediaOrigin.User);
        Assert.Equal((MediaIds.User("Zebra Dance.mid"), "Zebra Dance", SongKind.Midi), (mine.Id, mine.Title, mine.Kind));
        Assert.Equal(SongCredits.MySongs, mine.Categories);
        Assert.Equal(mine, songs.Songs[^1]);
    }

    [Fact]
    public void Names_sort_upper_cased_so_brackets_follow_the_letters()
    {
        foreach (string name in new[] { "[Bracket].mid", "zulu.mid", "_Under.mid", "apple.mid" })
        {
            WriteSong(Path.Combine(myMusic, name));
        }

        MusicLibrary songs = StartLibrary();

        Assert.Equal(["apple", "zulu", "[Bracket]", "_Under"], songs.Songs.Where(s => s.Origin == MediaOrigin.User).Select(s => s.Title));
    }

    [Fact]
    public void Shortcuts_play_with_the_engine_of_their_target()
    {
        string target = Path.Combine(sources, "Far Away.mid");
        WriteSong(target);
        string shortcut = Path.Combine(myMusic, "Far Away.mid - Shortcut.lnk");
        File.WriteAllBytes(shortcut, [0x4C, 0, 0, 0]);
        shell.Shortcuts[shortcut] = target;
        File.WriteAllBytes(Path.Combine(myMusic, "Broken.lnk"), [0x4C, 0, 0, 0]);
        string textShortcut = Path.Combine(myMusic, "Text.lnk");
        File.WriteAllBytes(textShortcut, [0x4C, 0, 0, 0]);
        shell.Shortcuts[textShortcut] = Path.Combine(sources, "readme.txt");

        MusicLibrary songs = StartLibrary();

        SongInfo song = Assert.Single(songs.Songs, s => s.Origin == MediaOrigin.User);
        Assert.Equal((MediaIds.User("Far Away.mid - Shortcut.lnk"), "Far Away.mid - Shortcut", SongKind.Midi, target), (song.Id, song.Title, song.Kind, song.ShortcutTarget));
        WaitFor(() => songs.Songs.Single(s => s.Origin == MediaOrigin.User).Length is not null, "the shortcut's length");
    }

    [Fact]
    public void Songs_copied_in_by_hand_appear_at_once()
    {
        MusicLibrary songs = StartLibrary();
        using var changed = new SemaphoreSlim(0);
        songs.Changed += (_, _) => changed.Release();

        WriteSong(Path.Combine(myMusic, "Dropped In.mid"));

        WaitFor(() => songs.TryGetSong(MediaIds.User("Dropped In.mid"), out _), "the watcher");
        Assert.True(changed.CurrentCount > 0);
    }

    [Fact]
    public void Adding_songs_copies_new_ones_skips_same_content_and_renames_clashes()
    {
        string sameAsBundled = Path.Combine(sources, "My Jingle.mid");
        File.Copy(Path.Combine(root.Paths.BundledMusicFolder, "Jingle Bells.mid"), sameAsBundled);
        string newSong = Path.Combine(sources, "New Song.mid");
        WriteSong(newSong, note: 60);
        string otherFolder = DataPaths.EnsureFolder(Path.Combine(sources, "Other"));
        string clash = Path.Combine(otherFolder, "New Song.mid");
        WriteSong(clash, note: 72);
        string text = Path.Combine(sources, "readme.txt");
        File.WriteAllText(text, "hello");
        MusicLibrary songs = StartLibrary();

        IReadOnlyList<MediaImportResult> results = songs.AddFiles([sameAsBundled, newSong, clash, text, Path.Combine(sources, "ghost.mid"), newSong]);

        Assert.Equal(
            [
                (MediaImportOutcome.AlreadyPresent, MediaIds.Bundled("Jingle Bells.mid")),
                (MediaImportOutcome.Added, MediaIds.User("New Song.mid")),
                (MediaImportOutcome.Renamed, MediaIds.User("New Song (2).mid")),
                (MediaImportOutcome.Unsupported, null),
                (MediaImportOutcome.Failed, null),
                (MediaImportOutcome.AlreadyPresent, MediaIds.User("New Song.mid")),
            ],
            results.Select(r => (r.Outcome, r.Id)));
        Assert.False(string.IsNullOrEmpty(results[4].Error));
        Assert.True(songs.TryGetSong(MediaIds.User("New Song (2).mid"), out SongInfo? renamed));
        Assert.Equal(Path.Combine(myMusic, "New Song (2).mid"), renamed.FilePath);
        Assert.True(File.Exists(newSong));
    }

    [Fact]
    public void A_re_added_song_starts_checked()
    {
        settings.Update(
            s => s with { Current = s.Current with { Music = s.Current.Music with { DisabledSongs = [MediaIds.User("New Song.mid"), MediaIds.Bundled("Dreidle.mid")] } } },
            SettingsChange.Internal);
        string newSong = Path.Combine(sources, "New Song.mid");
        WriteSong(newSong);
        MusicLibrary songs = StartLibrary();

        songs.AddFiles([newSong]);

        Assert.Equal([MediaIds.Bundled("Dreidle.mid")], settings.Current.Current.Music.DisabledSongs);
    }

    [Fact]
    public void Removing_your_song_holds_the_file_and_restoring_brings_it_back()
    {
        string path = Path.Combine(myMusic, "Mine.mid");
        WriteSong(path);
        MusicLibrary songs = StartLibrary();
        string id = MediaIds.User("Mine.mid");

        HeldItem? held = songs.Remove(id);

        Assert.NotNull(held);
        Assert.False(File.Exists(path));
        Assert.False(songs.TryGetSong(id, out _));
        songs.Restore(held);
        Assert.True(File.Exists(path));
        Assert.True(songs.TryGetSong(id, out _));
    }

    [Fact]
    public void Removing_a_bundled_song_hides_it_until_restored()
    {
        MusicLibrary songs = StartLibrary();
        string id = MediaIds.Bundled("Dreidle.mid");

        Assert.Null(songs.Remove(id));

        Assert.Equal([id], settings.Current.Songs.Hidden);
        Assert.Equal("Remove Dreidle", settings.History[^1].Description);
        Assert.DoesNotContain(songs.Songs, s => s.Id == id);
        Assert.Contains(songs.HiddenSongs, s => s.Id == id);
        Assert.True(songs.TryGetSong(id, out _));
        Assert.True(File.Exists(Path.Combine(root.Paths.BundledMusicFolder, "Dreidle.mid")));

        songs.RestoreHiddenSongs();

        Assert.Empty(settings.Current.Songs.Hidden);
        Assert.Contains(songs.Songs, s => s.Id == id);
        Assert.Empty(songs.HiddenSongs);
    }

    [Fact]
    public void Hidden_songs_follow_settings_changes_such_as_undo()
    {
        MusicLibrary songs = StartLibrary();
        WaitFor(() => songs.Songs.All(s => s.Length is not null), "song lengths");
        int changes = 0;
        songs.Changed += (_, _) => changes++;

        settings.Update(s => s with { Songs = new HiddenItems { Hidden = [MediaIds.Bundled("Clementine.mid")] } }, SettingsChange.Edit("Remove Clementine"));

        Assert.Equal(45, songs.Songs.Count);
        Assert.Equal(MediaIds.Bundled("Clementine.mid"), Assert.Single(songs.HiddenSongs).Id);
        Assert.True(changes >= 1);
    }

    [Fact]
    public void Files_windows_cannot_decode_stay_listed_but_are_not_playable()
    {
        File.WriteAllText(Path.Combine(myMusic, "Broken.mid"), "not a midi file");
        File.WriteAllText(Path.Combine(myMusic, "Broken.mp3"), "not an mp3 file");

        MusicLibrary songs = StartLibrary();

        WaitFor(() => songs.Songs.Where(s => s.Origin == MediaOrigin.User).All(s => !s.IsPlayable), "the probe");
        Assert.Equal(2, songs.Songs.Count(s => s.Origin == MediaOrigin.User));
    }

    [Fact]
    public void Audio_files_get_their_type_and_length()
    {
        WriteWave(Path.Combine(myMusic, "Chimes.wav"), TimeSpan.FromSeconds(1.5));
        WriteSunAu(Path.Combine(myMusic, "Bells.au"), sampleRate: 8000, seconds: 2);

        MusicLibrary songs = StartLibrary();

        WaitFor(() => songs.Songs.Where(s => s.Origin == MediaOrigin.User).All(s => s.Length is not null), "the probe");
        SongInfo wave = Song(songs, "Chimes");
        SongInfo au = Song(songs, "Bells");
        Assert.Equal((SongKind.Wav, TimeSpan.FromSeconds(1.5), true), (wave.Kind, wave.Length, wave.IsPlayable));
        Assert.Equal((SongKind.Au, TimeSpan.FromSeconds(2), true), (au.Kind, au.Length, au.IsPlayable));
    }

    public void Dispose()
    {
        library?.Dispose();
        root.Dispose();
    }

    internal static void WriteWave(string path, TimeSpan length)
    {
        var format = new WaveFormat(8000, 16, 1);
        using var writer = new WaveFileWriter(path, format);
        writer.Write(new byte[(int)(format.AverageBytesPerSecond * length.TotalSeconds)]);
    }

    internal static void WriteSunAu(string path, int sampleRate, int seconds)
    {
        using var file = File.Create(path);
        foreach (uint value in new uint[] { 0x2E736E64, 24, (uint)(sampleRate * seconds * 2), 3, (uint)sampleRate, 1 })
        {
            file.Write([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
        }

        file.Write(new byte[sampleRate * seconds * 2]);
    }

    private static void WriteSong(string path, byte note = 60) =>
        File.WriteAllBytes(path, Smf(0, 96, Track(0x00, 0x90, note, 100, 0x60, 0x80, note, 0, EndOfTrack)));

    private static SongInfo Song(MusicLibrary songs, string title) => songs.Songs.Single(s => s.Title == title);

    private static void WaitFor(Func<bool> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < Timeout, $"Timed out waiting for {what}.");
            Thread.Sleep(20);
        }
    }

    private MusicLibrary StartLibrary()
    {
        library = new MusicLibrary(root.Paths, settings, holding, shell, NullAppLog.Instance);
        library.Start();
        return library;
    }
}
