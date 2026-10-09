using HolidayLights.Audio.Events;

namespace HolidayLights.Tests.Audio;

/// <summary>The lock-free per-subscriber event queues of the music engine.</summary>
public sealed class MusicEventHubTests
{
    [Fact]
    public void Each_subscriber_reads_every_event_in_order()
    {
        var hub = new MusicEventHub();
        using IMusicEventReader first = hub.Subscribe();
        using IMusicEventReader second = hub.Subscribe();

        for (byte note = 60; note < 63; note++)
        {
            hub.Publish(Note(note));
        }

        Assert.Equal([60, 61, 62], ReadAll(first).Select(e => (int)e.Note));
        Assert.Equal([60, 61, 62], ReadAll(second).Select(e => (int)e.Note));
        Assert.Empty(ReadAll(first));
    }

    [Fact]
    public void Events_before_subscribing_are_not_seen()
    {
        var hub = new MusicEventHub();
        hub.Publish(Note(1));
        using IMusicEventReader reader = hub.Subscribe();
        hub.Publish(Note(2));

        Assert.Equal([2], ReadAll(reader).Select(e => (int)e.Note));
    }

    [Fact]
    public void A_full_queue_drops_the_oldest_events()
    {
        var hub = new MusicEventHub();
        using IMusicEventReader reader = hub.Subscribe(capacity: 16);

        for (int i = 0; i < 40; i++)
        {
            hub.Publish(Note((byte)i));
        }

        Assert.Equal(Enumerable.Range(24, 16), ReadAll(reader).Select(e => (int)e.Note));
    }

    [Fact]
    public void A_small_buffer_reads_in_several_calls()
    {
        var hub = new MusicEventHub();
        using IMusicEventReader reader = hub.Subscribe();
        for (int i = 0; i < 5; i++)
        {
            hub.Publish(Note((byte)i));
        }

        var buffer = new MusicEvent[2];
        Assert.Equal(2, reader.Read(buffer));
        Assert.Equal(2, reader.Read(buffer));
        Assert.Equal(1, reader.Read(buffer));
        Assert.Equal(4, buffer[0].Note);
        Assert.Equal(0, reader.Read(buffer));
    }

    [Fact]
    public void A_disposed_reader_stops_receiving()
    {
        var hub = new MusicEventHub();
        IMusicEventReader reader = hub.Subscribe();
        reader.Dispose();

        hub.Publish(Note(1));

        Assert.Empty(ReadAll(reader));
    }

    [Fact]
    public async Task Concurrent_producers_and_a_reader_never_see_torn_or_reordered_events()
    {
        var hub = new MusicEventHub();
        using IMusicEventReader reader = hub.Subscribe(capacity: 256);
        const int PerProducer = 200_000;
        const int Producers = 2;
        long[] lastSeen = [-1, -1];
        using var stop = new CancellationTokenSource();

        // Dedicated threads that start together: thread-pool work items could run one after the other (or not at all
        // until production ends) when the whole test suite saturates the pool.
        using var start = new Barrier(Producers + 1);
        Task consumer = Task.Factory.StartNew(
            () =>
            {
                var buffer = new MusicEvent[64];
                start.SignalAndWait();
                while (!stop.IsCancellationRequested)
                {
                    Check(reader.Read(buffer));
                }

                // Production has ended: drain what is left, so the newest events are always seen.
                int count;
                while ((count = reader.Read(buffer)) > 0)
                {
                    Check(count);
                }

                void Check(int count)
                {
                    for (int i = 0; i < count; i++)
                    {
                        MusicEvent e = buffer[i];
                        int producer = e.Channel;
                        Assert.Equal(Checksum(e.Timestamp), (e.Note, e.Velocity));
                        Assert.True(e.Timestamp > lastSeen[producer], "events of one producer arrived out of order");
                        lastSeen[producer] = e.Timestamp;
                    }
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Task[] producers = Enumerable.Range(0, Producers).Select(producer => Task.Factory.StartNew(
            () =>
            {
                start.SignalAndWait();
                for (long n = 0; n < PerProducer; n++)
                {
                    (byte note, byte velocity) = Checksum(n);
                    hub.Publish(new MusicEvent(MusicEventKind.NoteOn, n, (byte)producer, note, velocity));
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)).ToArray();

        await Task.WhenAll(producers).WaitAsync(TimeSpan.FromSeconds(30));
        stop.Cancel();
        await consumer.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(PerProducer - 1, lastSeen.Max());
    }

    private static (byte Note, byte Velocity) Checksum(long n) => ((byte)(n % 128), (byte)(n * 7 % 127 + 1));

    private static MusicEvent Note(byte note) => new(MusicEventKind.NoteOn, note, 0, note, 100);

    private static List<MusicEvent> ReadAll(IMusicEventReader reader)
    {
        var all = new List<MusicEvent>();
        var buffer = new MusicEvent[8];
        int count;
        while ((count = reader.Read(buffer)) > 0)
        {
            all.AddRange(buffer.AsSpan(0, count));
        }

        return all;
    }
}
