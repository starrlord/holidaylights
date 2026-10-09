using System.Collections.Concurrent;
using HolidayLights.Platform.Instance;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Platform;

/// <summary>Uses private mutex and pipe names, so a running Holiday Lights is never disturbed.</summary>
public sealed class SingleInstanceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void TryClaim_OnlyTheFirstInstanceWins()
    {
        (string mutex, string pipe) = PrivateNames();
        using var first = new SingleInstance(new RecordingLog(), mutex, pipe);
        using var second = new SingleInstance(new RecordingLog(), mutex, pipe);

        Assert.True(first.TryClaim());
        Assert.True(first.TryClaim());
        Assert.False(second.TryClaim());

        first.Dispose();
        Assert.True(second.TryClaim());
    }

    [Fact]
    public void Listen_RequiresTheClaim()
    {
        (string mutex, string pipe) = PrivateNames();
        using var instance = new SingleInstance(new RecordingLog(), mutex, pipe);

        Assert.Throws<InvalidOperationException>(() => instance.Listen(_ => Task.CompletedTask));
    }

    [Fact]
    public void RealNames_FollowTheContract()
    {
        Assert.Equal(@"Local\HolidayLights6.Instance", InstanceNames.Mutex);
        Assert.StartsWith("HolidayLights6.", InstanceNames.PipePrefix, StringComparison.Ordinal);
    }

    /// <summary>Review r1 #9: a data root isolates the session, so a test launch never forwards to another instance.</summary>
    [Fact]
    public void ADataRoot_GetsItsOwnMutexAndPipe()
    {
        string root = Path.Combine(Path.GetTempPath(), "hl-instance-" + Guid.NewGuid().ToString("N"));
        string other = root + "-other";

        Assert.Equal(InstanceNames.Mutex, InstanceNames.MutexFor(null));
        Assert.Equal(InstanceNames.PipePrefix, InstanceNames.PipePrefixFor(null));
        Assert.NotEqual(InstanceNames.Mutex, InstanceNames.MutexFor(root));
        Assert.StartsWith(InstanceNames.Mutex + ".", InstanceNames.MutexFor(root), StringComparison.Ordinal);
        Assert.StartsWith(InstanceNames.PipePrefix, InstanceNames.PipePrefixFor(root), StringComparison.Ordinal);
        Assert.NotEqual(InstanceNames.PipePrefix, InstanceNames.PipePrefixFor(root));

        // The same folder however it is written, a different folder never.
        Assert.Equal(InstanceNames.MutexFor(root), InstanceNames.MutexFor(root.ToUpperInvariant() + Path.DirectorySeparatorChar));
        Assert.NotEqual(InstanceNames.MutexFor(root), InstanceNames.MutexFor(other));

        // Two isolated sessions both become the first instance; a second launch on the same root does not.
        using var a = new SingleInstance(new RecordingLog(), root);
        using var b = new SingleInstance(new RecordingLog(), other);
        using var a2 = new SingleInstance(new RecordingLog(), root);
        Assert.True(a.TryClaim());
        Assert.True(b.TryClaim());
        Assert.False(a2.TryClaim());
    }

    [Fact]
    public async Task ACommand_IsDeliveredAndAcknowledged()
    {
        (string mutex, string pipe) = PrivateNames();
        using var server = new SingleInstance(new RecordingLog(), mutex, pipe);
        Assert.True(server.TryClaim());
        var received = new TaskCompletionSource<InstanceCommand>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Listen(async connection =>
        {
            await foreach (InstanceMessage message in connection.ReadAllAsync())
            {
                received.TrySetResult(message.Command!);
                await connection.SendAsync(new InstanceMessage { Accepted = true });
                return;
            }
        });

        using var client = new SingleInstance(new RecordingLog(), mutex, pipe);
        Assert.False(client.TryClaim());
        await using IInstanceConnection? connection = await client.ConnectAsync(Timeout);
        Assert.NotNull(connection);
        await connection.SendAsync(new InstanceMessage { Command = InstanceCommand.Open([@"C:\Bulbs\Snow Family.bul", @"D:\a b\c.gif"]) });

        InstanceCommand command = await received.Task.WaitAsync(Timeout);
        Assert.Equal(InstanceCommandKind.Open, command.Kind);
        Assert.Equal([@"C:\Bulbs\Snow Family.bul", @"D:\a b\c.gif"], command.Arguments);

        InstanceMessage reply = await FirstAsync(connection);
        Assert.True(reply.Accepted);
    }

    [Fact]
    public async Task TheAcknowledgement_SurvivesTheServerClosingAtOnce()
    {
        (string mutex, string pipe) = PrivateNames();
        using var server = new SingleInstance(new RecordingLog(), mutex, pipe);
        Assert.True(server.TryClaim());
        server.Listen(connection => connection.SendAsync(new InstanceMessage { Accepted = true }).AsTask());

        using var client = new SingleInstance(new RecordingLog(), mutex, pipe);
        await using IInstanceConnection? connection = await client.ConnectAsync(Timeout);
        Assert.NotNull(connection);

        InstanceMessage reply = await FirstAsync(connection);
        Assert.True(reply.Accepted);
        Assert.Empty(await RestAsync(connection));
    }

    [Fact]
    public async Task MusicEvents_StreamWhileOtherCommandsAreServed()
    {
        (string mutex, string pipe) = PrivateNames();
        using var server = new SingleInstance(new RecordingLog(), mutex, pipe);
        Assert.True(server.TryClaim());
        var stopStreaming = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commands = new ConcurrentQueue<InstanceCommandKind>();
        server.Listen(async connection =>
        {
            await foreach (InstanceMessage message in connection.ReadAllAsync())
            {
                commands.Enqueue(message.Command!.Kind);
                if (message.Command.Kind == InstanceCommandKind.SaverStarted)
                {
                    for (int note = 60; note < 63; note++)
                    {
                        await connection.SendAsync(new InstanceMessage { Music = new MusicEvent(MusicEventKind.NoteOn, 0, 0, (byte)note, 100) });
                    }

                    await stopStreaming.Task;
                }
                else
                {
                    await connection.SendAsync(new InstanceMessage { Accepted = true });
                }

                return;
            }
        });

        using var saver = new SingleInstance(new RecordingLog(), mutex, pipe);
        await using IInstanceConnection? stream = await saver.ConnectAsync(Timeout);
        Assert.NotNull(stream);
        await stream.SendAsync(new InstanceMessage { Command = InstanceCommand.Simple(InstanceCommandKind.SaverStarted) });
        var notes = new List<int>();
        await foreach (InstanceMessage message in stream.ReadAllAsync().WithCancellation(new CancellationTokenSource(Timeout).Token))
        {
            notes.Add(message.Music!.Value.Note);
            if (notes.Count == 3)
            {
                break;
            }
        }

        // The saver's connection is still open: another launch is served all the same.
        using var launcher = new SingleInstance(new RecordingLog(), mutex, pipe);
        await using IInstanceConnection? launch = await launcher.ConnectAsync(Timeout);
        Assert.NotNull(launch);
        await launch.SendAsync(new InstanceMessage { Command = InstanceCommand.Simple(InstanceCommandKind.ToggleLights) });
        Assert.True((await FirstAsync(launch)).Accepted);

        stopStreaming.SetResult();
        Assert.Equal([60, 61, 62], notes);
        Assert.Contains(InstanceCommandKind.ToggleLights, commands);
    }

    [Fact]
    public async Task ConnectAsync_WithoutAnInstanceReturnsNull()
    {
        (string mutex, string pipe) = PrivateNames();
        using var client = new SingleInstance(new RecordingLog(), mutex, pipe);

        Assert.Null(await client.ConnectAsync(TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task Dispose_ClosesOpenConnections()
    {
        (string mutex, string pipe) = PrivateNames();
        var server = new SingleInstance(new RecordingLog(), mutex, pipe);
        Assert.True(server.TryClaim());
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Listen(async connection =>
        {
            connected.SetResult();
            await foreach (InstanceMessage _ in connection.ReadAllAsync())
            {
            }
        });

        using var client = new SingleInstance(new RecordingLog(), mutex, pipe);
        await using IInstanceConnection? connection = await client.ConnectAsync(Timeout);
        Assert.NotNull(connection);
        await connected.Task.WaitAsync(Timeout);

        server.Dispose();

        Assert.Empty(await RestAsync(connection).WaitAsync(Timeout));
        Assert.Null(await client.ConnectAsync(TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task AFailingHandler_IsLoggedAndTheServerKeepsListening()
    {
        (string mutex, string pipe) = PrivateNames();
        var log = new RecordingLog();
        using var server = new SingleInstance(log, mutex, pipe);
        Assert.True(server.TryClaim());
        int calls = 0;
        server.Listen(async connection =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new InvalidOperationException("boom");
            }

            await connection.SendAsync(new InstanceMessage { Accepted = true });
        });

        using var client = new SingleInstance(new RecordingLog(), mutex, pipe);
        await using (IInstanceConnection? first = await client.ConnectAsync(Timeout))
        {
            Assert.NotNull(first);
            Assert.Empty(await RestAsync(first));
        }

        await using IInstanceConnection? second = await client.ConnectAsync(Timeout);
        Assert.NotNull(second);
        Assert.True((await FirstAsync(second)).Accepted);
        Assert.Contains(log.Entries, e => e.Level == AppLogLevel.Error && e.Exception is InvalidOperationException);
    }

    private static (string Mutex, string Pipe) PrivateNames()
    {
        string id = Guid.NewGuid().ToString("N");
        return ($@"Local\HolidayLights6.Test.{id}", $"HolidayLights6.Test.{id}");
    }

    private static async Task<InstanceMessage> FirstAsync(IInstanceConnection connection)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        await foreach (InstanceMessage message in connection.ReadAllAsync(timeout.Token))
        {
            return message;
        }

        throw new InvalidOperationException("The connection closed without a message.");
    }

    private static async Task<List<InstanceMessage>> RestAsync(IInstanceConnection connection)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var messages = new List<InstanceMessage>();
        await foreach (InstanceMessage message in connection.ReadAllAsync(timeout.Token))
        {
            messages.Add(message);
        }

        return messages;
    }
}
