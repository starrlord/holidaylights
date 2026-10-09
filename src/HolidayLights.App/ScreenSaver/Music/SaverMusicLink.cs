using System.IO;
using HolidayLights.Audio;
using HolidayLights.Audio.Events;

namespace HolidayLights.App.ScreenSaver.Music;

/// <summary>
/// The music of a <c>/s</c> saver process (PRODUCT-SPEC 6.2.1). When Holiday Lights runs, the saver connects to its pipe and
/// sends <c>saver-started</c>: the app rests its lights, plays per "Play the Chosen Songs" and streams its music events back
/// for "Dance to the Music"; <c>saver-stopped</c> ends the session. Otherwise the saver process plays the music itself
/// (switch and mode permitting) and stops it when it ends (5.4).
/// </summary>
internal sealed class SaverMusicLink : IAsyncDisposable
{
    private const string LogSource = "ScreenSaver.Music";

    /// <summary>How long the saver waits for a running Holiday Lights to answer.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    /// <summary>How long local music fades out when the saver ends (the Exit fade).</summary>
    public static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(500);

    private readonly IMusicDirector? localMusic;
    private readonly ISingleInstance? instance;
    private readonly IInstanceConnection? connection;
    private readonly CancellationTokenSource? reading;
    private readonly Task? readLoop;
    private readonly IAppLog log;

    private SaverMusicLink(IMusicEventSource events, IMusicDirector? localMusic, ISingleInstance? instance, IInstanceConnection? connection, IAppLog log)
    {
        Events = events;
        this.localMusic = localMusic;
        this.instance = instance;
        this.connection = connection;
        this.log = log;
        if (connection is not null && events is MusicEventHub hub)
        {
            reading = new CancellationTokenSource();
            readLoop = ReadEventsAsync(connection, hub, reading.Token);
        }
    }

    /// <summary>Music events for "Dance to the Music": streamed from the running app, or the local player's.</summary>
    public IMusicEventSource Events { get; }

    /// <summary>True when the running app plays the music.</summary>
    public bool IsConnected => connection is not null;

    /// <summary>Connects to a running Holiday Lights, or starts the local music.</summary>
    /// <param name="connectToRunningApp">Returns the pipe client when Holiday Lights runs, else null (no wait).</param>
    /// <param name="music">The saver process's own music engine.</param>
    /// <param name="settings">The settings (music switch, mode, songs, volume).</param>
    /// <param name="remoteSession">True in a Remote Desktop session (music stops).</param>
    /// <param name="log">The log.</param>
    /// <returns>The link.</returns>
    public static async Task<SaverMusicLink> StartAsync(
        Func<ISingleInstance?> connectToRunningApp, IMusicDirector music, AppSettings settings, bool remoteSession, IAppLog log)
    {
        ArgumentNullException.ThrowIfNull(connectToRunningApp);
        ArgumentNullException.ThrowIfNull(music);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(log);
        if (connectToRunningApp() is { } instance)
        {
            try
            {
                if (await instance.ConnectAsync(ConnectTimeout).ConfigureAwait(false) is { } connection)
                {
                    await connection.SendAsync(Message(InstanceCommandKind.SaverStarted)).ConfigureAwait(false);
                    log.Info(LogSource, "Holiday Lights is running: it plays the music while the saver shows.");
                    return new SaverMusicLink(new MusicEventHub(), null, instance, connection, log);
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
                log.Warn(LogSource, "Holiday Lights didn't answer; the saver plays the music itself.", ex);
            }

            instance.Dispose();
        }

        music.ApplyPolicy(Policy(settings, remoteSession));
        return new SaverMusicLink(music.Events, music, null, null, log);
    }

    /// <summary>The policy of a saver that plays its own music: the settings, with the saver showing.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="remoteSession">True in a Remote Desktop session.</param>
    /// <returns>The policy.</returns>
    public static MusicPolicy Policy(AppSettings settings, bool remoteSession)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new MusicPolicy
        {
            Enabled = settings.Music.Enabled,
            Mode = settings.Current.Music.Mode,
            DisabledSongs = new HashSet<string>(settings.Current.Music.DisabledSongs, MediaIds.Comparer),
            Volume = settings.Music.Volume,
            Muted = settings.Music.Muted,
            MidiDevice = settings.Music.MidiDevice,
            SyncOffsetMs = settings.Music.SyncOffsetMs,
            SaverRunning = true,
            Stopped = remoteSession,
        };
    }

    /// <summary>Ends the music side of the saver: <c>saver-stopped</c> to the app, or the local music fades out.</summary>
    /// <returns>A task that completes when done.</returns>
    public async ValueTask DisposeAsync()
    {
        if (connection is not null)
        {
            try
            {
                await connection.SendAsync(Message(InstanceCommandKind.SaverStopped)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
                // The app closes the session when the connection closes anyway.
                log.Warn(LogSource, "Couldn't tell Holiday Lights that the saver stopped.", ex);
            }

            if (reading is not null)
            {
                await reading.CancelAsync().ConfigureAwait(false);
            }

            await connection.DisposeAsync().ConfigureAwait(false);
            if (readLoop is not null)
            {
                await readLoop.ConfigureAwait(false);
            }

            reading?.Dispose();
            instance?.Dispose();
        }

        if (localMusic is not null)
        {
            await localMusic.StopAsync(FadeOut).ConfigureAwait(false);
        }
    }

    private static InstanceMessage Message(InstanceCommandKind kind) => new() { Command = InstanceCommand.Simple(kind) };

    private async Task ReadEventsAsync(IInstanceConnection pipe, MusicEventHub hub, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (InstanceMessage message in pipe.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (message.Music is { } musicEvent)
                {
                    hub.Publish(musicEvent);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            log.Info(LogSource, "The music event stream ended.");
        }
    }
}
