using System.IO;
using System.Windows.Threading;
using HolidayLights.Audio;

namespace HolidayLights.App.Shell;

/// <summary>
/// The server side of the instance pipe (CONTRACTS 6.4): one command per connection is performed on the UI thread and
/// answered with <c>{ "accepted": ... }</c>; a screen saver's <c>saver-started</c> starts a saver session and streams music
/// events back on the same connection until <c>saver-stopped</c> or the connection closes; <c>subscribe-music-events</c>
/// streams without a session. Connections are served on the thread pool.
/// </summary>
public sealed class InstanceServer : IDisposable
{
    private const string LogSource = "Shell.Instance";
    private static readonly TimeSpan StreamInterval = TimeSpan.FromMilliseconds(15);

    private readonly ISingleInstance instance;
    private readonly Dispatcher dispatcher;
    private readonly InstanceCommandRouter router;
    private readonly IScreenSaverSessions sessions;
    private readonly IMusicDirector music;
    private readonly IAppLog log;
    private readonly CancellationTokenSource lifetime = new();

    /// <summary>Creates the server (it listens after <see cref="Start"/>).</summary>
    /// <param name="instance">The claimed single instance.</param>
    /// <param name="router">Performs commands (UI thread).</param>
    /// <param name="sessions">Screen saver sessions.</param>
    /// <param name="music">The music engine (its events are streamed).</param>
    /// <param name="log">The log.</param>
    public InstanceServer(ISingleInstance instance, InstanceCommandRouter router, IScreenSaverSessions sessions, IMusicDirector music, IAppLog log)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(music);
        ArgumentNullException.ThrowIfNull(log);
        this.instance = instance;
        this.router = router;
        this.sessions = sessions;
        this.music = music;
        this.log = log;
        dispatcher = Dispatcher.CurrentDispatcher;
    }

    /// <summary>Starts accepting connections.</summary>
    public void Start() => instance.Listen(HandleAsync);

    /// <summary>Stops streaming (the single instance closes the pipe itself).</summary>
    public void Dispose()
    {
        lifetime.Cancel();
        lifetime.Dispose();
    }

    private async Task HandleAsync(IInstanceConnection connection)
    {
        CancellationToken token = lifetime.Token;
        await foreach (InstanceMessage message in connection.ReadAllAsync(token).ConfigureAwait(false))
        {
            if (message.Command is not { } command)
            {
                continue;
            }

            log.Info(LogSource, $"Received {command}.");
            switch (command.Kind)
            {
                case InstanceCommandKind.SaverStarted:
                    await StreamAsync(connection, saverSession: true, token).ConfigureAwait(false);
                    return;
                case InstanceCommandKind.SubscribeMusicEvents:
                    await StreamAsync(connection, saverSession: false, token).ConfigureAwait(false);
                    return;
                case InstanceCommandKind.SaverStopped:
                    // A stop on a connection of its own: the session started on another connection.
                    sessions.SaverStopped();
                    return;
                default:
                    await PerformAsync(connection, command, token).ConfigureAwait(false);
                    return;
            }
        }
    }

    private async Task PerformAsync(IInstanceConnection connection, InstanceCommand command, CancellationToken token)
    {
        CommandOutcome outcome;
        try
        {
            outcome = await dispatcher.InvokeAsync(() => router.Execute(command)).Task.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            log.Error(LogSource, $"The command {command} failed.", e);
            outcome = CommandOutcome.Refused;
        }

        await connection.SendAsync(new InstanceMessage { Accepted = outcome.Accepted }, token).ConfigureAwait(false);
        if (outcome.AfterReply is { } after)
        {
            await dispatcher.InvokeAsync(after).Task.Unwrap().ConfigureAwait(false);
        }
    }

    /// <summary>Streams music events until the other side says <c>saver-stopped</c> or closes the connection.</summary>
    private async Task StreamAsync(IInstanceConnection connection, bool saverSession, CancellationToken token)
    {
        if (saverSession)
        {
            sessions.SaverStarted();
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task listening = ListenForStopAsync(connection, stop);
        IMusicEventReader reader = music.Events.Subscribe();
        try
        {
            var buffer = new MusicEvent[256];
            while (!stop.IsCancellationRequested)
            {
                int count = reader.Read(buffer);
                for (int i = 0; i < count; i++)
                {
                    await connection.SendAsync(new InstanceMessage { Music = buffer[i] }, stop.Token).ConfigureAwait(false);
                }

                await Task.Delay(StreamInterval, stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // The saver ended or the program is closing.
        }
        finally
        {
            reader.Dispose();
            await stop.CancelAsync().ConfigureAwait(false);
            await listening.ConfigureAwait(false);
            if (saverSession)
            {
                sessions.SaverStopped();
            }
        }
    }

    private static async Task ListenForStopAsync(IInstanceConnection connection, CancellationTokenSource stop)
    {
        try
        {
            await foreach (InstanceMessage message in connection.ReadAllAsync(stop.Token).ConfigureAwait(false))
            {
                if (message.Command?.Kind == InstanceCommandKind.SaverStopped)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Streaming ended first.
        }

        await stop.CancelAsync().ConfigureAwait(false);
    }
}
