namespace HolidayLights.Audio.Events;

/// <summary>
/// Fans music events out to subscribers, each with its own lock-free single-producer single-consumer queue (see
/// <see cref="IMusicEventSource"/>). Owner: audio.
/// </summary>
/// <remarks>
/// Readers (the Lights thread, the pipe stream to a screen saver) never block. The engine publishes from its music
/// threads (MIDI sequencer, audio output, director); a short lock makes them one producer per queue.
/// </remarks>
public sealed class MusicEventHub : IMusicEventSource
{
    private readonly Lock gate = new();
    private MusicEventQueue[] queues = [];

    /// <inheritdoc />
    public IMusicEventReader Subscribe(int capacity = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        var queue = new MusicEventQueue(this, capacity);
        lock (gate)
        {
            queues = [.. queues, queue];
        }

        return queue;
    }

    /// <summary>Publishes one event to every subscriber (called by the music thread only).</summary>
    /// <param name="musicEvent">The event (timestamp already includes the latency offset).</param>
    public void Publish(MusicEvent musicEvent)
    {
        lock (gate)
        {
            foreach (MusicEventQueue queue in queues)
            {
                queue.Write(musicEvent);
            }
        }
    }

    /// <summary>Removes a queue (its reader was disposed).</summary>
    /// <param name="queue">The queue.</param>
    internal void Unsubscribe(MusicEventQueue queue)
    {
        lock (gate)
        {
            queues = Array.FindAll(queues, q => !ReferenceEquals(q, queue));
        }
    }
}
