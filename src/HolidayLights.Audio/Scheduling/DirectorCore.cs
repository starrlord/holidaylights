using HolidayLights.Audio.Events;
using HolidayLights.Audio.Midi;
using HolidayLights.Audio.Playback;

namespace HolidayLights.Audio.Scheduling;

/// <summary>Who asked for the song that plays.</summary>
internal enum SongOrigin
{
    /// <summary>Chosen by the play mode and the shuffle bag: stops when the switch, the mode or its check box no longer allow it.</summary>
    Automatic,

    /// <summary>Asked for by the user ("Play Now", "Previous", "Next Song" while the mode plays nothing): ignores the mode and the check boxes; stops when the switch is turned off.</summary>
    Requested,
}

/// <summary>
/// The decisions of the Music Box (PRODUCT-SPEC 6.1): which song plays when, the 5.4 play modes
/// and shuffle bag, the 1 s and 60-180 s gaps, and the 6.0 rules around them (the switch, the held first song, pause rules,
/// failures, a busy synthesizer, a missing output device, Previous/Next, the exit fade).
/// </summary>
/// <remarks>
/// Not thread-safe and free of threads: the owner calls every member on one thread and calls <see cref="OnWakeUp"/> at
/// <see cref="NextWakeUp"/>. Engine callbacks come back through the <c>post</c> delegate. Time comes from a
/// <see cref="TimeProvider"/>, so tests run it on a fake clock.
/// </remarks>
internal sealed class DirectorCore : IDisposable
{
    private const string LogSource = "Audio.Director";
    private const int MaxFailuresInARow = 3;
    private const int IntermittentRandomMilliseconds = 120_000;
    private const int MixerCheckTicks = 5;

    /// <summary>"Always": 1 s between songs.</summary>
    private static readonly TimeSpan AlwaysGap = TimeSpan.FromSeconds(1);

    /// <summary>"Intermittently": 60,000 + random(0..119,999) ms between songs.</summary>
    private static readonly TimeSpan IntermittentMinimumGap = TimeSpan.FromSeconds(60);

    /// <summary>A song that fails is followed by the next one after 1 s (5.4 stopped all music).</summary>
    private static readonly TimeSpan FailureGap = TimeSpan.FromSeconds(1);

    /// <summary>"Holiday Lights tries again every 30 seconds" when another app holds the synthesizer.</summary>
    private static readonly TimeSpan SynthesizerRetry = TimeSpan.FromSeconds(30);

    /// <summary>How often a missing output device is looked for again.</summary>
    private static readonly TimeSpan OutputDeviceRetry = TimeSpan.FromSeconds(5);

    /// <summary>The first song after program start fades in over 3 s.</summary>
    private static readonly TimeSpan FirstSongFadeIn = TimeSpan.FromSeconds(3);

    /// <summary>"Previous" pressed again within 3 s plays the song before.</summary>
    private static readonly TimeSpan PreviousWindow = TimeSpan.FromSeconds(3);

    private readonly TimeProvider time;
    private readonly long epoch;
    private readonly ISongPlayerFactory players;
    private readonly IMusicRandom gapRandom;
    private readonly IAudioEnvironment environment;
    private readonly MusicEventHub events;
    private readonly Action<Action> post;
    private readonly IAppLog log;
    private readonly ShuffleBag bag;
    private readonly SongHistory history = new();

    private MusicPolicy policy = new();
    private HashSet<string> disabledSongs = new(MediaIds.Comparer);
    private IReadOnlyList<SongInfo> songs = [];
    private Dictionary<string, SongInfo> songsById = new(MediaIds.Comparer);
    private Playing? current;
    private Gap? gap;
    private PendingStart? waitingForSynthesizer;
    private TimeSpan? outputDeviceRetryAt;
    private int failuresInARow;
    private bool stoppedAfterFailures;
    private bool userPaused;
    private bool anySongStarted;
    private TimeSpan? previousPressedAt;
    private bool? mutedInMixer;
    private int ticksSinceMixerCheck;
    private Shutdown? shutdown;

    /// <summary>Creates the core; nothing plays until a policy allows it.</summary>
    /// <param name="time">The clock.</param>
    /// <param name="players">Opens songs.</param>
    /// <param name="shuffleRandom">The shuffle bag's draws (5.4 <c>rand()</c>).</param>
    /// <param name="gapRandom">The "Intermittently" gaps (5.4 used <c>GetTickCount()</c>, not <c>rand()</c>, so they never disturb the shuffle).</param>
    /// <param name="environment">Output device and mixer state.</param>
    /// <param name="events">Where song state events go.</param>
    /// <param name="post">Runs engine callbacks on the owner's thread.</param>
    /// <param name="log">The log.</param>
    public DirectorCore(
        TimeProvider time,
        ISongPlayerFactory players,
        IMusicRandom shuffleRandom,
        IMusicRandom gapRandom,
        IAudioEnvironment environment,
        MusicEventHub events,
        Action<Action> post,
        IAppLog log)
    {
        this.time = time;
        this.players = players;
        this.gapRandom = gapRandom;
        this.environment = environment;
        this.events = events;
        this.post = post;
        this.log = log;
        epoch = time.GetTimestamp();
        bag = new ShuffleBag(shuffleRandom);
    }

    /// <summary>Time since the core was created (its monotonic clock).</summary>
    public TimeSpan Now => time.GetElapsedTime(epoch);

    /// <summary>True while a song is loaded (playing or paused).</summary>
    public bool HasSong => current is not null;

    /// <summary>
    /// When <see cref="OnWakeUp"/> must run next (on the <see cref="Now"/> clock), or null. A time stays reported, even
    /// once it has passed, until <see cref="OnWakeUp"/> handles it; a gap that something else holds up (a pause, the held
    /// first song) is not reported, because only that change can end it.
    /// </summary>
    public TimeSpan? NextWakeUp
    {
        get
        {
            if (shutdown is { } closing)
            {
                return closing.Done.Task.IsCompleted ? null : closing.CompletesAt;
            }

            TimeSpan? next = Earliest(waitingForSynthesizer?.RetryAt, outputDeviceRetryAt);
            if (gap is { } pending && current is null && MusicAllowed && !StartHeldUp)
            {
                next = Earliest(next, GapDue(pending));
            }

            return next;
        }
    }

    private bool MusicAllowed => policy.Enabled && !policy.Stopped && ModeAllows(policy.Mode, policy.SaverRunning);

    private double Volume => policy.Muted ? 0 : Math.Clamp(policy.Volume, 0, 100) / 100.0;

    /// <summary>Applies a new policy and re-evaluates as 5.4 did (a still-allowed song keeps playing; a gap is honoured).</summary>
    /// <param name="newPolicy">The policy.</param>
    public void ApplyPolicy(MusicPolicy newPolicy)
    {
        MusicPolicy old = policy;
        var newDisabled = new HashSet<string>(newPolicy.DisabledSongs, MediaIds.Comparer);
        bool checkedSongsChanged = !newDisabled.SetEquals(disabledSongs);
        policy = newPolicy;
        disabledSongs = newDisabled;
        if (checkedSongsChanged)
        {
            ClearFailures();
        }

        if (current is { } playing && (old.Volume != newPolicy.Volume || old.Muted != newPolicy.Muted))
        {
            Control(playing, p => p.SetVolume(Volume));
        }

        if (old.Enabled && !newPolicy.Enabled)
        {
            // "Off means no music at all", even a song started (or waiting to start) with Play Now. A user pause ends
            // with it, so turning the music on again plays (review r1 #35).
            StopCurrent();
            ClearWaiting(includeRequested: true);
            userPaused = false;
        }

        if (shutdown is null && !policy.Stopped && !string.Equals(old.MidiDevice, newPolicy.MidiDevice, StringComparison.Ordinal))
        {
            MoveToMidiDevice();
        }

        Evaluate();
    }

    /// <summary>Takes a new song list (bundled + My Music); a changed list is a rescan, which starts a new shuffle bag.</summary>
    /// <param name="list">The listed songs in 5.4 order.</param>
    public void OnLibraryChanged(IReadOnlyList<SongInfo> list)
    {
        bool sameSongs = list.Count == songs.Count && list.Select(s => s.Id).SequenceEqual(songs.Select(s => s.Id), MediaIds.Comparer);
        songs = list;
        songsById = new Dictionary<string, SongInfo>(list.Count, MediaIds.Comparer);
        foreach (SongInfo song in list)
        {
            songsById.TryAdd(song.Id, song);
        }

        if (!sameSongs)
        {
            bag.Rescan(list.Select(s => s.Id).ToList());
            ClearFailures();
        }

        if (current is { } playing && songsById.TryGetValue(playing.Song.Id, out SongInfo? refreshed))
        {
            playing.Song = refreshed;
        }

        Evaluate();
    }

    /// <summary>"Play Now": plays a listed song at once, ignoring the mode and the check boxes, without touching the bag.</summary>
    /// <param name="songId">The song.</param>
    public void PlayNow(string songId)
    {
        if (!AcceptsCommands())
        {
            return;
        }

        if (!songsById.TryGetValue(songId, out SongInfo? song))
        {
            log.Warn(LogSource, "Play Now asked for a song that is not listed.");
            return;
        }

        BeginRequest();
        StopCurrent();
        StartSong(song, SongOrigin.Requested, TimeSpan.Zero, addToHistory: true);
    }

    /// <summary>"Next Song": another random checked song now (during a gap: at once).</summary>
    public void NextSong()
    {
        if (!AcceptsCommands())
        {
            return;
        }

        BeginRequest();
        StopCurrent();
        if (bag.Pick(IsEligible) is { } id)
        {
            StartSong(songsById[id], MusicAllowed ? SongOrigin.Automatic : SongOrigin.Requested, TimeSpan.Zero, addToHistory: true);
        }
    }

    /// <summary>"Previous": restarts the current song; pressed again within 3 s, plays the song that played before it.</summary>
    public void Previous()
    {
        if (!AcceptsCommands())
        {
            return;
        }

        TimeSpan now = Now;
        bool pressedAgain = previousPressedAt is { } pressed && now - pressed <= PreviousWindow;
        previousPressedAt = now;
        SongInfo target;
        SongOrigin origin;
        if (current is { } playing)
        {
            if (pressedAgain && history.StepBack(songsById.ContainsKey) is { } earlier)
            {
                (target, origin) = (songsById[earlier], SongOrigin.Requested);
            }
            else
            {
                (target, origin) = (playing.Song, playing.Origin);
            }
        }
        else if (history.Current is { } last && songsById.TryGetValue(last, out SongInfo? lastSong))
        {
            (target, origin) = (lastSong, SongOrigin.Requested);
        }
        else
        {
            return;
        }

        BeginRequest();
        StopCurrent();
        StartSong(target, origin, TimeSpan.Zero, addToHistory: false);
    }

    /// <summary>"Pause": pauses mid-song; no new song starts until <see cref="Resume"/>.</summary>
    public void Pause()
    {
        userPaused = true;
        if (current is { } playing)
        {
            ApplyPause(playing);
        }
    }

    /// <summary>"Resume": continues the paused song, or lets the next one start when it is due.</summary>
    public void Resume()
    {
        userPaused = false;
        Evaluate();
    }

    /// <summary>Seeks the current audio file (MIDI songs ignore it).</summary>
    /// <param name="position">The position.</param>
    public void Seek(TimeSpan position)
    {
        if (current is { } playing && playing.Player.CanSeek)
        {
            Control(playing, p => p.Seek(position));
        }
    }

    /// <summary>"Try Now" / "Try Again": retries the synthesizer, the output device or stopped music at once.</summary>
    public void RetryNow()
    {
        if (shutdown is not null)
        {
            return;
        }

        ClearFailures();
        outputDeviceRetryAt = null;
        RetryPendingStart();
    }

    /// <summary>The owner's timer: a gap ended, a retry is due, or the exit fade finished.</summary>
    public void OnWakeUp()
    {
        TimeSpan now = Now;
        if (shutdown is { } closing)
        {
            if (now >= closing.CompletesAt)
            {
                CompleteShutdown();
            }

            return;
        }

        if (outputDeviceRetryAt is { } deviceRetry && now >= deviceRetry)
        {
            outputDeviceRetryAt = null;
        }

        if (waitingForSynthesizer is { } pending && now >= pending.RetryAt)
        {
            RetryPendingStart();
            return;
        }

        Evaluate();
    }

    /// <summary>Called about once a second while a song is loaded: keeps event timestamps aligned and watches the mixer.</summary>
    public void Tick()
    {
        if (current is not { } playing)
        {
            return;
        }

        Control(playing, p => p.Synchronize());
        if (ticksSinceMixerCheck-- <= 0)
        {
            ticksSinceMixerCheck = MixerCheckTicks - 1;
            mutedInMixer = environment.IsMutedInMixer();
        }
    }

    /// <summary>The state for the Music Box, Home and the tray.</summary>
    /// <returns>A snapshot.</returns>
    public MusicState GetState()
    {
        if (current is { } playing)
        {
            return new MusicState
            {
                Status = policy.PausedByRules ? MusicStatus.PausedByRules : userPaused ? MusicStatus.Paused : MusicStatus.Playing,
                CurrentSong = playing.Song,
                Position = PositionOf(playing),
                CanSeek = playing.Player.CanSeek,
                MutedInMixer = mutedInMixer,
            };
        }

        MusicStatus status = IdleStatus();
        DateTimeOffset? nextSongAt = status == MusicStatus.BetweenSongs && gap is { } pending
            ? pending.EndedAtUtc + GapLength(pending)
            : null;
        return new MusicState { Status = status, NextSongAt = nextSongAt, MutedInMixer = mutedInMixer };
    }

    /// <summary>Fades the music out and stops; afterwards nothing plays again.</summary>
    /// <param name="fadeOut">The fade (Exit: 500 ms).</param>
    /// <returns>A task that completes when the music has stopped (after <see cref="OnWakeUp"/> at <see cref="NextWakeUp"/>).</returns>
    public Task BeginShutdown(TimeSpan fadeOut)
    {
        if (shutdown is { } existing)
        {
            return existing.Done.Task;
        }

        gap = null;
        waitingForSynthesizer = null;
        outputDeviceRetryAt = null;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TimeSpan completesAt = Now;
        if (current is { Paused: false } playing && fadeOut > TimeSpan.Zero)
        {
            Control(playing, p => p.FadeOut(fadeOut));
            completesAt += fadeOut + playing.Player.OutputLatency;
        }

        shutdown = new Shutdown(completesAt, done);
        if (completesAt <= Now)
        {
            CompleteShutdown();
        }

        return done.Task;
    }

    /// <summary>Stops the music at once.</summary>
    public void Dispose()
    {
        StopCurrent();
        shutdown?.Done.TrySetResult();
    }

    private static bool ModeAllows(PlayMode mode, bool saverRunning) => mode switch
    {
        PlayMode.SaverOn => saverRunning,
        PlayMode.SaverOff => !saverRunning,
        PlayMode.Always or PlayMode.Intermittently => true,
        _ => false,
    };

    private static TimeSpan? Earliest(TimeSpan? a, TimeSpan? b) => a is null ? b : b is null ? a : (a < b ? a : b);

    /// <summary>The 5.4 <c>Music_Update</c> step, with the 6.0 fixes: a pending gap is honoured, and requested songs keep playing.</summary>
    private void Evaluate()
    {
        if (shutdown is not null)
        {
            return;
        }

        if (policy.Stopped)
        {
            if (current is not null)
            {
                // The user's pause belonged to the song that stops here (review r1 #35).
                userPaused = false;
            }

            StopCurrent();
            ClearWaiting(includeRequested: true);
            return;
        }

        if (current is { } playing)
        {
            if (KeepsPlaying(playing))
            {
                ApplyPause(playing);
                return;
            }

            // Unchecked, no longer allowed by the mode, or removed: stop it and choose again at once. A user pause ends
            // with its song, so the next song is not held back by a pause nobody can see any more (review r1 #35).
            StopCurrent();
            userPaused = false;
        }

        if (!MusicAllowed)
        {
            ClearWaiting(includeRequested: false);
            return;
        }

        if (StartHeldUp || (gap is { } pending && Now < GapDue(pending)))
        {
            return;
        }

        if (bag.Pick(IsEligible) is { } id)
        {
            StartSong(songsById[id], SongOrigin.Automatic, anySongStarted ? TimeSpan.Zero : FirstSongFadeIn, addToHistory: true);
        }
        else
        {
            // No checked song: nothing to wait for; checking one starts it at once.
            gap = null;
        }
    }

    private bool KeepsPlaying(Playing playing) =>
        songsById.ContainsKey(playing.Song.Id)
        && (playing.Origin == SongOrigin.Requested || (MusicAllowed && IsEligible(playing.Song.Id)));

    /// <summary>Something other than the gap keeps the next song from starting by itself.</summary>
    private bool StartHeldUp =>
        (policy.HoldFirstSong && !anySongStarted)
        || userPaused
        || policy.PausedByRules
        || stoppedAfterFailures
        || waitingForSynthesizer is not null
        || outputDeviceRetryAt is not null;

    private bool IsEligible(string songId) =>
        !disabledSongs.Contains(songId) && songsById.TryGetValue(songId, out SongInfo? song) && song.IsPlayable;

    private bool AcceptsCommands() => shutdown is null && !policy.Stopped;

    private void BeginRequest()
    {
        userPaused = false;
        ClearFailures();
    }

    private void ClearFailures()
    {
        failuresInARow = 0;
        stoppedAfterFailures = false;
    }

    private void ClearWaiting(bool includeRequested)
    {
        gap = null;
        outputDeviceRetryAt = null;
        if (includeRequested || waitingForSynthesizer?.Origin == SongOrigin.Automatic)
        {
            waitingForSynthesizer = null;
        }
    }

    /// <summary>
    /// "MIDI Output" changed, which applies at once (PRODUCT-SPEC 2.3): a MIDI song moves to the new device (MIDI cannot
    /// seek, so it starts over there, keeping who asked for it and a pause), and a song waiting for a busy synthesizer
    /// tries the new device now. A busy or failing device is handled as at any start.
    /// </summary>
    private void MoveToMidiDevice()
    {
        if (current is { } playing)
        {
            if (playing.Song.Kind == SongKind.Midi && KeepsPlaying(playing))
            {
                StopCurrent();
                StartSong(playing.Song, playing.Origin, TimeSpan.Zero, addToHistory: false);
            }
        }
        else if (waitingForSynthesizer is not null)
        {
            RetryPendingStart();
        }
    }

    private void RetryPendingStart()
    {
        PendingStart? pending = waitingForSynthesizer;
        waitingForSynthesizer = null;
        if (pending is { Origin: SongOrigin.Requested })
        {
            StartSong(pending.Song, SongOrigin.Requested, TimeSpan.Zero, addToHistory: true);
        }
        else
        {
            Evaluate();
        }
    }

    private void StartSong(SongInfo song, SongOrigin origin, TimeSpan fadeIn, bool addToHistory)
    {
        gap = null;
        waitingForSynthesizer = null;
        if (!environment.HasOutputDevice())
        {
            outputDeviceRetryAt = Now + OutputDeviceRetry;
            log.Info(LogSource, "No speakers or headphones are available; music waits for an output device.");
            return;
        }

        outputDeviceRetryAt = null;
        ISongPlayer player;
        try
        {
            player = players.Open(song, new SongPlaybackContext(policy.MidiDevice, TimeSpan.FromMilliseconds(policy.SyncOffsetMs), events));
        }
        catch (MidiOutputException ex) when (ex.IsDeviceBusy)
        {
            waitingForSynthesizer = new PendingStart(song, origin, Now + SynthesizerRetry);
            log.Info(LogSource, "Another app is using the MIDI synthesizer; trying again in 30 seconds.");
            return;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            RecordFailure(song, ex);
            return;
        }

        player.Ended += (_, e) => post(() => OnPlayerEnded(player, e.Error));
        try
        {
            player.Start(Volume, fadeIn);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            DisposeQuietly(player);
            RecordFailure(song, ex);
            return;
        }

        var playing = new Playing(player, song, origin);
        current = playing;
        anySongStarted = true;
        ticksSinceMixerCheck = 0;
        if (addToHistory)
        {
            history.Add(song.Id);
        }

        Publish(MusicEventKind.SongStarted);
        log.Info(LogSource, $"Playing {Describe(song)}.");
        ApplyPause(playing);
    }

    private void OnPlayerEnded(ISongPlayer player, Exception? error)
    {
        if (current is not { } playing || !ReferenceEquals(playing.Player, player))
        {
            return;
        }

        current = null;
        DisposeQuietly(player);
        Publish(MusicEventKind.SongStopped);
        if (error is null)
        {
            failuresInARow = 0;
            gap = new Gap(Now, time.GetUtcNow(), AfterFailure: false);
        }
        else
        {
            RecordFailure(playing.Song, error);
        }

        Evaluate();
    }

    private void RecordFailure(SongInfo song, Exception error)
    {
        log.Warn(LogSource, $"{Describe(song)} couldn't be played.", error);
        if (!environment.HasOutputDevice())
        {
            outputDeviceRetryAt = Now + OutputDeviceRetry;
            return;
        }

        if (++failuresInARow >= MaxFailuresInARow)
        {
            stoppedAfterFailures = true;
            gap = null;
            log.Warn(LogSource, "Music stopped because several songs couldn't be played.");
            return;
        }

        gap = new Gap(Now, time.GetUtcNow(), AfterFailure: true);
    }

    private void ApplyPause(Playing playing)
    {
        bool pause = userPaused || policy.PausedByRules;
        if (pause == playing.Paused)
        {
            return;
        }

        if (Control(playing, pause ? p => p.Pause() : p => p.Resume()))
        {
            playing.Paused = pause;
            Publish(pause ? MusicEventKind.Paused : MusicEventKind.Resumed);
        }
    }

    /// <summary>Runs a command on the current player; a failure ends the song as a failed one.</summary>
    private bool Control(Playing playing, Action<ISongPlayer> command)
    {
        try
        {
            command(playing.Player);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (ReferenceEquals(current, playing))
            {
                current = null;
                DisposeQuietly(playing.Player);
                Publish(MusicEventKind.SongStopped);
                RecordFailure(playing.Song, ex);
            }

            return false;
        }
    }

    private void StopCurrent()
    {
        if (current is not { } playing)
        {
            return;
        }

        current = null;
        try
        {
            playing.Player.Stop();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Warn(LogSource, "Stopping a song failed.", ex);
        }

        DisposeQuietly(playing.Player);
        Publish(MusicEventKind.SongStopped);
    }

    private void CompleteShutdown()
    {
        StopCurrent();
        shutdown?.Done.TrySetResult();
    }

    private void DisposeQuietly(ISongPlayer player)
    {
        try
        {
            player.Dispose();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Warn(LogSource, "Closing a song failed.", ex);
        }
    }

    private MusicStatus IdleStatus()
    {
        if (policy.Stopped)
        {
            return MusicStatus.Stopped;
        }

        if (waitingForSynthesizer is not null)
        {
            return MusicStatus.WaitingForSynthesizer;
        }

        if (outputDeviceRetryAt is not null)
        {
            return MusicStatus.NoOutputDevice;
        }

        if (!policy.Enabled)
        {
            return MusicStatus.Off;
        }

        MusicStatus? modeStatus = policy.Mode switch
        {
            PlayMode.Never => MusicStatus.NeverMode,
            PlayMode.SaverOn when !policy.SaverRunning => MusicStatus.WaitingForScreenSaver,
            PlayMode.SaverOff when policy.SaverRunning => MusicStatus.WaitingForScreenSaverToEnd,
            _ => null,
        };
        if (modeStatus is { } blocked)
        {
            return blocked;
        }

        if (!songs.Any(s => IsEligible(s.Id)))
        {
            return MusicStatus.NoSongs;
        }

        if (stoppedAfterFailures)
        {
            return MusicStatus.StoppedAfterFailures;
        }

        if (policy.HoldFirstSong && !anySongStarted)
        {
            return MusicStatus.Held;
        }

        return policy.PausedByRules ? MusicStatus.PausedByRules : userPaused ? MusicStatus.Paused : MusicStatus.BetweenSongs;
    }

    private TimeSpan GapDue(Gap pending) => pending.EndedAt + GapLength(pending);

    /// <summary>The gap follows the current mode, measured from the end of the last song.</summary>
    private TimeSpan GapLength(Gap pending) =>
        pending.AfterFailure ? FailureGap
        : policy.Mode == PlayMode.Intermittently ? pending.IntermittentDelay ??= IntermittentMinimumGap + TimeSpan.FromMilliseconds(gapRandom.Next(IntermittentRandomMilliseconds))
        : AlwaysGap;

    private TimeSpan PositionOf(Playing playing)
    {
        try
        {
            return playing.Player.Position;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return TimeSpan.Zero;
        }
    }

    private void Publish(MusicEventKind kind) => events.Publish(new MusicEvent(kind, time.GetTimestamp(), 0, 0, 0));

    /// <summary>Song names of My Music are personal; the log only names bundled songs.</summary>
    private static string Describe(SongInfo song) =>
        song.Origin == MediaOrigin.Bundled ? $"\"{song.Title}\"" : $"a song from My Music ({song.Kind})";

    /// <summary>The song that plays or is paused.</summary>
    private sealed class Playing(ISongPlayer player, SongInfo song, SongOrigin origin)
    {
        public ISongPlayer Player { get; } = player;

        public SongInfo Song { get; set; } = song;

        public SongOrigin Origin { get; } = origin;

        public bool Paused { get; set; }
    }

    /// <summary>The pause after a song: its length follows the mode when it is asked; the random part is drawn once.</summary>
    private sealed record Gap(TimeSpan EndedAt, DateTimeOffset EndedAtUtc, bool AfterFailure)
    {
        public TimeSpan? IntermittentDelay { get; set; }
    }

    /// <summary>A song waiting for the synthesizer.</summary>
    private sealed record PendingStart(SongInfo Song, SongOrigin Origin, TimeSpan RetryAt);

    /// <summary>The exit fade.</summary>
    private sealed record Shutdown(TimeSpan CompletesAt, TaskCompletionSource Done);
}
