using HolidayLights.Audio.Events;
using HolidayLights.Audio.Midi;
using HolidayLights.Audio.Playback;
using HolidayLights.Audio.Scheduling;

namespace HolidayLights.Audio;

/// <summary>The Music Box engine (see <see cref="IMusicDirector"/>). Owner: audio.</summary>
/// <remarks>
/// Every command is queued to the director's music thread (above-normal priority), where the play-mode logic runs
/// (<see cref="DirectorCore"/>); MIDI songs play on the sequencer's own thread and audio files on NAudio's output
/// thread. <see cref="StateChanged"/> is raised on the music thread.
/// </remarks>
public sealed class MusicDirector : IMusicDirector
{
    /// <summary>Position updates while a song is loaded.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private readonly ISongLibrary songs;
    private readonly MusicEventHub events = new();
    private readonly MusicThread thread;
    private readonly DirectorCore core;
    private readonly List<TaskCompletionSource> stopWaiters = [];
    private MusicState state = MusicState.Initial;
    private TimeSpan? nextTick;
    private Task? shutdown;
    private int disposed;

    /// <summary>Creates the director with its music thread (above-normal priority); nothing plays until a policy allows it.</summary>
    /// <param name="songs">The song library.</param>
    /// <param name="log">The log.</param>
    public MusicDirector(ISongLibrary songs, IAppLog log)
        : this(songs, log, TimeProvider.System, new SongPlayerFactory(log), SharedMusicRandom.Instance, new WindowsAudioEnvironment())
    {
    }

    /// <summary>Creates the director with its collaborators (tests use fakes).</summary>
    /// <param name="songs">The song library.</param>
    /// <param name="log">The log.</param>
    /// <param name="time">The clock.</param>
    /// <param name="players">Opens songs.</param>
    /// <param name="random">Shuffle and gap randomness.</param>
    /// <param name="environment">Output device and mixer state.</param>
    internal MusicDirector(
        ISongLibrary songs, IAppLog log, TimeProvider time, ISongPlayerFactory players, IMusicRandom random, IAudioEnvironment environment)
    {
        this.songs = songs;
        thread = new MusicThread("Holiday Lights Music", log);
        core = new DirectorCore(time, players, random, random, environment, events, Run, log);
        thread.Start(DelayUntilWake, OnWake);
        songs.Changed += OnSongsChanged;
        Run(() => core.OnLibraryChanged(songs.Songs));
    }

    /// <inheritdoc />
    public event EventHandler? StateChanged;

    /// <inheritdoc />
    public MusicState State => Volatile.Read(ref state);

    /// <inheritdoc />
    public IMusicEventSource Events => events;

    /// <inheritdoc />
    public void ApplyPolicy(MusicPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Run(() => core.ApplyPolicy(policy));
    }

    /// <inheritdoc />
    public void PlayNow(string songId)
    {
        ArgumentException.ThrowIfNullOrEmpty(songId);
        Run(() => core.PlayNow(songId));
    }

    /// <inheritdoc />
    public void NextSong() => Run(core.NextSong);

    /// <inheritdoc />
    public void Previous() => Run(core.Previous);

    /// <inheritdoc />
    public void Pause() => Run(core.Pause);

    /// <inheritdoc />
    public void Resume() => Run(core.Resume);

    /// <inheritdoc />
    public void Seek(TimeSpan position) => Run(() => core.Seek(position));

    /// <inheritdoc />
    public void RetryNow() => Run(core.RetryNow);

    /// <inheritdoc />
    public IReadOnlyList<string> GetMidiOutputDevices() => MidiSequencer.GetDeviceNames();

    /// <inheritdoc />
    public Task StopAsync(TimeSpan fadeOut)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return Task.CompletedTask;
        }

        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Run(() =>
        {
            shutdown = core.BeginShutdown(fadeOut);
            stopWaiters.Add(stopped);
        });
        return stopped.Task;
    }

    /// <summary>Stops the music and the music thread.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        songs.Changed -= OnSongsChanged;
        thread.Invoke(() =>
        {
            core.Dispose();
            CompleteStopWaiters();
        });
        thread.Dispose();
    }

    private void OnSongsChanged(object? sender, EventArgs e) => Run(() => core.OnLibraryChanged(songs.Songs));

    /// <summary>Runs a command (or an engine callback) on the music thread, then publishes the new state.</summary>
    private void Run(Action command) => thread.Post(() =>
    {
        command();
        PublishState();
    });

    private TimeSpan? DelayUntilWake()
    {
        TimeSpan now = core.Now;
        TimeSpan? wake = core.NextWakeUp;
        if (nextTick is { } tick && (wake is null || tick < wake))
        {
            wake = tick;
        }

        return wake is { } due ? (due > now ? due - now : TimeSpan.Zero) : null;
    }

    /// <summary>
    /// The timer fired: the position tick, or a time the core asked for. The core is always woken: once its time has
    /// come it no longer reports it in <see cref="DirectorCore.NextWakeUp"/>, and a wake-up without anything due only
    /// re-evaluates, which changes nothing.
    /// </summary>
    private void OnWake()
    {
        TimeSpan now = core.Now;
        if (nextTick is { } tick && now >= tick)
        {
            core.Tick();
            nextTick = now + TickInterval;
        }

        core.OnWakeUp();
        PublishState();
    }

    /// <summary>Publishes the state when it changed, then completes <see cref="StopAsync"/> calls whose music has stopped.</summary>
    private void PublishState()
    {
        nextTick = core.HasSong ? nextTick ?? core.Now + TickInterval : null;
        MusicState next = core.GetState();
        if (next != Volatile.Read(ref state))
        {
            Volatile.Write(ref state, next);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        CompleteStopWaiters();
    }

    private void CompleteStopWaiters()
    {
        if (shutdown is not { IsCompleted: true })
        {
            return;
        }

        foreach (TaskCompletionSource waiter in stopWaiters)
        {
            waiter.TrySetResult();
        }

        stopWaiters.Clear();
    }
}
