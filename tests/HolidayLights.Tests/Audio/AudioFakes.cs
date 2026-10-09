using System.Collections.Concurrent;
using HolidayLights.Audio.Midi;
using HolidayLights.Audio.Playback;
using HolidayLights.Audio.Scheduling;
using HolidayLights.Audio.Timing;

namespace HolidayLights.Tests.Audio;

/// <summary>A <see cref="TimeProvider"/> that only moves when told (timestamps are <see cref="TimeSpan"/> ticks).</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private static readonly DateTimeOffset Start = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private long ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref ticks);

    public override DateTimeOffset GetUtcNow() => Start + TimeSpan.FromTicks(GetTimestamp());

    public void Advance(TimeSpan by) => Interlocked.Add(ref ticks, by.Ticks);
}

/// <summary>Returns scripted numbers (then 0) and records the bounds it was asked for.</summary>
internal sealed class ScriptedRandom(params int[] values) : IMusicRandom
{
    private readonly Queue<int> queue = new(values);

    public List<int> Requests { get; } = [];

    public int Next(int maxExclusive)
    {
        Requests.Add(maxExclusive);
        return queue.Count > 0 ? queue.Dequeue() % maxExclusive : 0;
    }
}

/// <summary>An output device and mixer that tests switch on and off.</summary>
internal sealed class FakeAudioEnvironment : IAudioEnvironment
{
    public bool HasDevice { get; set; } = true;

    public bool? Muted { get; set; }

    public int MixerChecks { get; private set; }

    public bool HasOutputDevice() => HasDevice;

    public bool? IsMutedInMixer()
    {
        MixerChecks++;
        return Muted;
    }
}

/// <summary>A song player that records what it was told; tests end it with <see cref="Finish"/> or <see cref="Fail"/>.</summary>
internal sealed class FakeSongPlayer(SongInfo song, SongPlaybackContext context) : ISongPlayer
{
    public event EventHandler<SongEndedEventArgs>? Ended;

    public SongInfo Song { get; } = song;

    public SongPlaybackContext Context { get; } = context;

    public ConcurrentQueue<string> Calls { get; } = new();

    public double Volume { get; private set; }

    public TimeSpan FadeIn { get; private set; }

    public TimeSpan? FadeOutDuration { get; private set; }

    public bool IsPaused { get; private set; }

    public bool IsStopped { get; private set; }

    public bool IsDisposed { get; private set; }

    public TimeSpan? SeekedTo { get; private set; }

    public TimeSpan Position { get; set; }

    public bool CanSeek { get; set; }

    public TimeSpan OutputLatency { get; set; }

    public Exception? FailOnStart { get; set; }

    public void Start(double volume, TimeSpan fadeIn)
    {
        Calls.Enqueue("Start");
        if (FailOnStart is { } failure)
        {
            throw failure;
        }

        Volume = volume;
        FadeIn = fadeIn;
    }

    public void Pause()
    {
        Calls.Enqueue("Pause");
        IsPaused = true;
    }

    public void Resume()
    {
        Calls.Enqueue("Resume");
        IsPaused = false;
    }

    public void Seek(TimeSpan position)
    {
        Calls.Enqueue("Seek");
        SeekedTo = position;
    }

    public void SetVolume(double volume)
    {
        Calls.Enqueue("SetVolume");
        Volume = volume;
    }

    public void FadeOut(TimeSpan duration)
    {
        Calls.Enqueue("FadeOut");
        FadeOutDuration = duration;
    }

    public void Synchronize() => Calls.Enqueue("Synchronize");

    public void Stop()
    {
        Calls.Enqueue("Stop");
        IsStopped = true;
    }

    public void Dispose() => IsDisposed = true;

    public void Finish() => Ended?.Invoke(this, new SongEndedEventArgs(null));

    public void Fail(Exception error) => Ended?.Invoke(this, new SongEndedEventArgs(error));
}

/// <summary>Opens <see cref="FakeSongPlayer"/>s; <see cref="OpenError"/> makes chosen songs fail to open.</summary>
internal sealed class FakeSongPlayerFactory : ISongPlayerFactory
{
    private readonly Lock gate = new();

    public List<FakeSongPlayer> Opened { get; } = [];

    public Func<SongInfo, Exception?>? OpenError { get; set; }

    public Action<FakeSongPlayer>? Configure { get; set; }

    public FakeSongPlayer Last
    {
        get
        {
            lock (gate)
            {
                return Opened[^1];
            }
        }
    }

    public int Count
    {
        get
        {
            lock (gate)
            {
                return Opened.Count;
            }
        }
    }

    public ISongPlayer Open(SongInfo song, SongPlaybackContext context)
    {
        if (OpenError?.Invoke(song) is { } error)
        {
            throw error;
        }

        var player = new FakeSongPlayer(song, context);
        Configure?.Invoke(player);
        lock (gate)
        {
            Opened.Add(player);
        }

        return player;
    }
}

/// <summary>Records every MIDI message with the clock time it was sent.</summary>
internal sealed class RecordingMidiOutput(Func<long> clock) : IMidiOutput
{
    private readonly Lock gate = new();
    private readonly List<SentMessage> messages = [];

    public bool IsDisposed { get; private set; }

    public Exception? FailWith { get; set; }

    public IReadOnlyList<SentMessage> Messages
    {
        get
        {
            lock (gate)
            {
                return [.. messages];
            }
        }
    }

    public void SendShort(byte status, byte data1, byte data2)
    {
        if (FailWith is { } failure)
        {
            throw failure;
        }

        lock (gate)
        {
            messages.Add(new SentMessage(clock(), status, data1, data2, null));
        }
    }

    public void SendSysEx(ReadOnlySpan<byte> message)
    {
        if (FailWith is { } failure)
        {
            throw failure;
        }

        byte[] copy = message.ToArray();
        lock (gate)
        {
            messages.Add(new SentMessage(clock(), copy[0], 0, 0, copy));
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            messages.Clear();
        }
    }

    public void Dispose() => IsDisposed = true;
}

/// <summary>One recorded MIDI message.</summary>
internal readonly record struct SentMessage(long Time, byte Status, byte Data1, byte Data2, byte[]? SysEx);

/// <summary>
/// A sequencer clock in 100 ns ticks that jumps straight to every requested time, so a song plays in milliseconds and every
/// message carries its exact scheduled time. Times after <c>limit</c> never come: the sequencer then waits for commands.
/// </summary>
internal sealed class JumpingMusicClock(long limit = long.MaxValue - 1) : IMusicClock
{
    private long now;

    public long Frequency => TimeSpan.TicksPerSecond;

    public long Now => Interlocked.Read(ref now);

    public bool WaitUntil(long timestamp, WaitHandle wake)
    {
        if (timestamp > limit)
        {
            wake.WaitOne();
            return false;
        }

        if (wake.WaitOne(0))
        {
            return false;
        }

        if (timestamp > Now)
        {
            Interlocked.Exchange(ref now, timestamp);
        }

        return true;
    }
}

/// <summary>A sequencer clock in 100 ns ticks that only moves when the test sets it; waits poll it every millisecond.</summary>
internal sealed class ManualMusicClock : IMusicClock
{
    private long now;

    public long Frequency => TimeSpan.TicksPerSecond;

    public long Now => Interlocked.Read(ref now);

    public void Set(TimeSpan time) => Interlocked.Exchange(ref now, time.Ticks);

    public bool WaitUntil(long timestamp, WaitHandle wake)
    {
        while (Now < timestamp)
        {
            if (wake.WaitOne(1))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Resolves shortcuts from a table (no COM in tests).</summary>
internal sealed class FakeShellOperations : IShellOperations
{
    public Dictionary<string, string> Shortcuts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void OpenFolder(string path)
    {
    }

    public void ShowInFolder(string filePath)
    {
    }

    public void OpenSettingsUri(string uri)
    {
    }

    public void OpenProjectHomePage()
    {
    }

    public void OpenControlPanel(string arguments)
    {
    }

    public bool MoveToRecycleBin(string path) => false;

    public string? ResolveShortcut(string shortcutPath) => Shortcuts.GetValueOrDefault(shortcutPath);
}

/// <summary>Builders for song lists.</summary>
internal static class TestSongs
{
    public static SongInfo Bundled(string title, SongKind kind = SongKind.Midi, bool playable = true) => new()
    {
        Id = MediaIds.Bundled(title + ".mid"),
        Title = title,
        FilePath = Path.Combine("C:\\Music", title + ".mid"),
        Origin = MediaOrigin.Bundled,
        Kind = kind,
        IsPlayable = playable,
        SortKey = title.ToUpperInvariant(),
    };

    public static IReadOnlyList<SongInfo> Numbered(int count) =>
        Enumerable.Range(0, count).Select(i => Bundled($"Song {i:D2}")).ToList();
}
