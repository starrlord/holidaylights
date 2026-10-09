using System.Diagnostics;
using HolidayLights.Audio;

namespace HolidayLights.Tests.Audio;

/// <summary>The public Music Box engine: its music thread, state reporting, library changes and exit.</summary>
public sealed class MusicDirectorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void A_policy_starts_music_and_the_next_song_follows_the_gap()
    {
        var players = new FakeSongPlayerFactory();
        using MusicDirector director = Create(new StubLibrary(TestSongs.Numbered(3)), players);
        int changes = 0;
        director.StateChanged += (_, _) => Interlocked.Increment(ref changes);

        director.ApplyPolicy(new MusicPolicy { Enabled = true });

        WaitFor(() => director.State.Status == MusicStatus.Playing, "the first song");
        WaitFor(() => Volatile.Read(ref changes) > 0, "StateChanged");
        Assert.Equal(players.Last.Song, director.State.CurrentSong);
        players.Last.Finish();
        WaitFor(() => players.Count == 2, "the second song after the 1 s gap");
        Assert.True(players.Opened[0].IsDisposed);
    }

    [Fact]
    public void The_position_is_reported_while_a_song_plays()
    {
        var players = new FakeSongPlayerFactory();
        using MusicDirector director = Create(new StubLibrary(TestSongs.Numbered(1)), players);
        director.ApplyPolicy(new MusicPolicy { Enabled = true });
        WaitFor(() => director.State.Status == MusicStatus.Playing, "the song");

        players.Last.Position = TimeSpan.FromSeconds(12);

        WaitFor(() => director.State.Position == TimeSpan.FromSeconds(12), "a position update");
        Assert.Contains("Synchronize", players.Last.Calls);
    }

    [Fact]
    public void Library_changes_reach_the_director()
    {
        var library = new StubLibrary([]);
        var players = new FakeSongPlayerFactory();
        using MusicDirector director = Create(library, players);
        director.ApplyPolicy(new MusicPolicy { Enabled = true });
        WaitFor(() => director.State.Status == MusicStatus.NoSongs, "the empty list");

        library.Replace(TestSongs.Numbered(2));

        WaitFor(() => director.State.Status == MusicStatus.Playing, "a song from the new list");
    }

    [Fact]
    public async Task Stop_async_fades_out_and_completes()
    {
        var players = new FakeSongPlayerFactory();
        using MusicDirector director = Create(new StubLibrary(TestSongs.Numbered(2)), players);
        director.ApplyPolicy(new MusicPolicy { Enabled = true });
        WaitFor(() => director.State.Status == MusicStatus.Playing, "the song");

        await director.StopAsync(TimeSpan.FromMilliseconds(100)).WaitAsync(Timeout);

        Assert.Equal(TimeSpan.FromMilliseconds(100), players.Last.FadeOutDuration);
        Assert.True(players.Last.IsStopped);
    }

    [Fact]
    public void Dispose_stops_the_music_and_can_be_repeated()
    {
        var players = new FakeSongPlayerFactory();
        MusicDirector director = Create(new StubLibrary(TestSongs.Numbered(2)), players);
        director.ApplyPolicy(new MusicPolicy { Enabled = true });
        WaitFor(() => director.State.Status == MusicStatus.Playing, "the song");

        director.Dispose();
        director.Dispose();

        Assert.True(players.Last.IsStopped);
        Assert.True(players.Last.IsDisposed);
    }

    [Fact]
    public void Midi_output_devices_are_listed()
    {
        using MusicDirector director = Create(new StubLibrary([]), new FakeSongPlayerFactory());

        IReadOnlyList<string> devices = director.GetMidiOutputDevices();

        Assert.All(devices, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    private static MusicDirector Create(StubLibrary library, FakeSongPlayerFactory players) =>
        new(library, NullAppLog.Instance, TimeProvider.System, players, new MsvcRandom(1), new FakeAudioEnvironment());

    private static void WaitFor(Func<bool> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < Timeout, $"Timed out waiting for {what}.");
            Thread.Sleep(10);
        }
    }

    /// <summary>A library whose list the test replaces.</summary>
    private sealed class StubLibrary(IReadOnlyList<SongInfo> songs) : ISongLibrary
    {
        public event EventHandler? Changed;

        public IReadOnlyList<SongInfo> Songs { get; private set; } = songs;

        public IReadOnlyList<SongInfo> HiddenSongs => [];

        public void Replace(IReadOnlyList<SongInfo> list)
        {
            Songs = list;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Start()
        {
        }

        public bool TryGetSong(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SongInfo? song)
        {
            song = Songs.FirstOrDefault(s => MediaIds.Comparer.Equals(s.Id, id));
            return song is not null;
        }

        public IReadOnlyList<MediaImportResult> AddFiles(IEnumerable<string> paths) => [];

        public HeldItem? Remove(string id) => null;

        public void Restore(HeldItem item)
        {
        }

        public void RestoreHiddenSongs()
        {
        }
    }
}
