namespace HolidayLights.Audio.Events;

/// <summary>
/// A bounded ring of music events for one consumer. The producer side is serialized by <see cref="MusicEventHub"/>; the
/// consumer side is wait-free: <see cref="Read"/> never blocks and never takes a lock. When the producer laps the
/// consumer, the oldest events are overwritten and skipped (per-slot sequence numbers detect it, as in a seqlock).
/// </summary>
internal sealed class MusicEventQueue : IMusicEventReader
{
    private const long Writing = -1;

    private readonly MusicEventHub hub;
    private readonly MusicEvent[] events;
    private readonly long[] sequences;
    private readonly long mask;
    private long head;
    private long tail;

    /// <summary>Creates a queue.</summary>
    /// <param name="hub">The hub to leave on dispose.</param>
    /// <param name="capacity">Requested capacity; rounded up to a power of two (at least 16).</param>
    public MusicEventQueue(MusicEventHub hub, int capacity)
    {
        this.hub = hub;
        int size = (int)Math.Min(1 << 30, System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(16, capacity)));
        events = new MusicEvent[size];
        sequences = new long[size];
        Array.Fill(sequences, Writing);
        mask = size - 1;
    }

    /// <summary>Appends an event (producer side; the hub serializes producers).</summary>
    /// <param name="musicEvent">The event.</param>
    public void Write(in MusicEvent musicEvent)
    {
        long index = head;
        long slot = index & mask;
        Volatile.Write(ref sequences[slot], Writing);
        Interlocked.MemoryBarrier();
        events[slot] = musicEvent;
        Volatile.Write(ref sequences[slot], index);
        Volatile.Write(ref head, index + 1);
    }

    /// <inheritdoc />
    public int Read(Span<MusicEvent> destination)
    {
        long available = Volatile.Read(ref head);
        long next = Math.Max(tail, available - events.Length);
        int count = 0;
        while (next < available && count < destination.Length)
        {
            long slot = next & mask;
            if (Volatile.Read(ref sequences[slot]) != next)
            {
                next = SkipOverwritten(next);
                continue;
            }

            MusicEvent candidate = events[slot];
            Interlocked.MemoryBarrier();
            if (Volatile.Read(ref sequences[slot]) != next)
            {
                next = SkipOverwritten(next);
                continue;
            }

            destination[count++] = candidate;
            next++;
        }

        tail = next;
        return count;
    }

    /// <summary>Unsubscribes.</summary>
    public void Dispose() => hub.Unsubscribe(this);

    /// <summary>The producer lapped this slot: continue with the oldest event that is still in the ring.</summary>
    private long SkipOverwritten(long next) => Math.Max(next + 1, Volatile.Read(ref head) - events.Length);
}
