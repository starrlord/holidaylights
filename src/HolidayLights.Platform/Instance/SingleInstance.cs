using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using HolidayLights.Platform.Native;

namespace HolidayLights.Platform.Instance;

/// <summary>
/// The <c>Local\HolidayLights6.Instance</c> mutex and the per-user pipe <c>HolidayLights6.&lt;user SID&gt;</c>
/// (<c>PipeOptions.CurrentUserOnly</c>), line-based <see cref="InstanceMessage"/> JSON (see <see cref="ISingleInstance"/>).
/// Owner: platform.
/// </summary>
/// <remarks>
/// <para>The mutex is only a marker: whoever creates it is the first instance, and it disappears with the last handle, so
/// a crashed instance never blocks the next start (no ownership, no abandoned-mutex states, no thread affinity).</para>
/// <para>The server keeps one listening pipe instance at all times and serves every connection on the thread pool, so a
/// screen saver's long-lived music stream never blocks other commands. The same user may be signed in to two sessions
/// (Remote Desktop): both ends check that the other process runs in the same session, and the client keeps trying until
/// its own session's instance answers.</para>
/// </remarks>
public sealed class SingleInstance : ISingleInstance
{
    private const string LogSource = "Platform.Instance";
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);

    private readonly IAppLog log;
    private readonly string mutexName;
    private readonly string pipeName;
    private readonly uint sessionId;
    private readonly CancellationTokenSource shutdown = new();
    private readonly ConcurrentDictionary<PipeConnection, byte> connections = new();
    private readonly Lock gate = new();
    private Mutex? mutex;
    private Task? acceptLoop;

    /// <summary>Creates the object (nothing is claimed or opened yet) for the data root in <see cref="DataPaths.DataRootVariable"/>, if any.</summary>
    /// <param name="log">The log.</param>
    public SingleInstance(IAppLog log)
        : this(log, DataRootFromEnvironment())
    {
    }

    /// <summary>
    /// Creates the object (nothing is claimed or opened yet). A data root gets its own mutex and pipe
    /// (<see cref="InstanceNames.MutexFor"/>), so an isolated session never forwards to, or is reached by, another session.
    /// </summary>
    /// <param name="log">The log.</param>
    /// <param name="dataRoot">The data root (<see cref="DataPaths.DataRoot"/>), or null for the normal locations.</param>
    public SingleInstance(IAppLog log, string? dataRoot)
        : this(log, InstanceNames.MutexFor(dataRoot), InstanceNames.PipePrefixFor(dataRoot) + CurrentUserSid())
    {
    }

    /// <summary>The data root named by <see cref="DataPaths.DataRootVariable"/>, or null.</summary>
    /// <returns>The full data root, or null.</returns>
    public static string? DataRootFromEnvironment()
    {
        string? root = Environment.GetEnvironmentVariable(DataPaths.DataRootVariable);
        return string.IsNullOrWhiteSpace(root) ? null : DataPaths.ForDataRoot(root).DataRoot;
    }

    /// <summary>Creates the object with private names (tests run beside a real instance).</summary>
    /// <param name="log">The log.</param>
    /// <param name="mutexName">The mutex name.</param>
    /// <param name="pipeName">The pipe name.</param>
    internal SingleInstance(IAppLog log, string mutexName, string pipeName)
    {
        this.log = log;
        this.mutexName = mutexName;
        this.pipeName = pipeName;
        using Process self = Process.GetCurrentProcess();
        sessionId = (uint)self.SessionId;
    }

    /// <inheritdoc />
    public bool TryClaim()
    {
        lock (gate)
        {
            if (mutex is not null)
            {
                return true;
            }

            try
            {
                var candidate = new Mutex(initiallyOwned: false, mutexName, out bool createdNew);
                if (createdNew)
                {
                    mutex = candidate;
                    return true;
                }

                // Not first: close our handle at once so the marker vanishes with the running instance.
                candidate.Dispose();
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                // Created by an elevated instance whose object we may not open: it is running.
                return false;
            }
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">This process is not the first instance, or it already listens.</exception>
    public void Listen(Func<IInstanceConnection, Task> onConnection)
    {
        ArgumentNullException.ThrowIfNull(onConnection);
        lock (gate)
        {
            if (mutex is null)
            {
                throw new InvalidOperationException("Only the first instance listens; call TryClaim first.");
            }

            if (acceptLoop is not null)
            {
                throw new InvalidOperationException("The instance pipe is already listening.");
            }

            CancellationToken token = shutdown.Token;
            acceptLoop = Task.Run(() => AcceptLoopAsync(onConnection, token), token);
        }
    }

    /// <inheritdoc />
    /// <remarks>A zero or negative timeout tries once.</remarks>
    public async Task<IInstanceConnection?> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        double budget = Math.Clamp(timeout.TotalMilliseconds, 0, int.MaxValue);
        long start = Stopwatch.GetTimestamp();
        while (true)
        {
            int remaining = (int)Math.Max(0, budget - Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await client.ConnectAsync(remaining, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                await client.DisposeAsync().ConfigureAwait(false);
                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // UnauthorizedAccessException: a pipe of that name exists but belongs to another user.
                await client.DisposeAsync().ConfigureAwait(false);
                log.Warn(LogSource, "The running instance did not accept the connection.", e);
                return null;
            }

            if (NativeMethods.GetNamedPipeServerSessionId(client.SafePipeHandle, out uint serverSession) && serverSession == sessionId)
            {
                return new PipeConnection(client);
            }

            // The same user's instance in another session answered; ours may be listening on another pipe instance.
            await client.DisposeAsync().ConfigureAwait(false);
            if (remaining == 0)
            {
                return null;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Stops listening, closes open connections and releases the mutex.</summary>
    public void Dispose()
    {
        Task? loop;
        lock (gate)
        {
            shutdown.Cancel();
            loop = acceptLoop;
        }

        foreach (PipeConnection connection in connections.Keys)
        {
            connection.Abort();
        }

        try
        {
            loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // The loop ends with a cancellation; nothing else is expected and nothing is left to clean up.
        }

        lock (gate)
        {
            mutex?.Dispose();
            mutex = null;
        }
    }

    private static string CurrentUserSid()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? Environment.UserName;
    }

    private async Task AcceptLoopAsync(Func<IInstanceConnection, Task> onConnection, CancellationToken cancellationToken)
    {
        TimeSpan retryDelay = TimeSpan.FromMilliseconds(250);
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(
                    pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The previous instance may still be closing its pipe: retry with back-off.
                log.Warn(LogSource, $"Cannot open the instance pipe; retrying in {retryDelay.TotalMilliseconds:0} ms.", e);
                if (!await DelayAsync(retryDelay, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, MaxRetryDelay.Ticks));
                continue;
            }

            retryDelay = TimeSpan.FromMilliseconds(250);
            try
            {
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                return;
            }
            catch (IOException e)
            {
                // A client that vanished between connecting and our accept.
                log.Write(AppLogLevel.Debug, LogSource, "A client left before it was served.", e);
                await server.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            _ = Task.Run(() => ServeAsync(server, onConnection), CancellationToken.None);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream server, Func<IInstanceConnection, Task> onConnection)
    {
        var connection = new PipeConnection(server);
        connections.TryAdd(connection, 0);
        try
        {
            if (shutdown.IsCancellationRequested)
            {
                // Accepted while Dispose was closing the connections.
                return;
            }

            if (!NativeMethods.GetNamedPipeClientSessionId(server.SafePipeHandle, out uint clientSession) || clientSession != sessionId)
            {
                log.Warn(LogSource, "Refused an instance connection from another Windows session.");
                return;
            }

            await onConnection(connection).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            log.Error(LogSource, "An instance connection failed.", e);
        }
        finally
        {
            connections.TryRemove(connection, out _);
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
