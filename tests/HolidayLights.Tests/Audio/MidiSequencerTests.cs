using System.Collections.Concurrent;
using HolidayLights.Audio.Midi;
using HolidayLights.Tests.Shared;
using static HolidayLights.Tests.Audio.SmfBuilder;

namespace HolidayLights.Tests.Audio;

/// <summary>
/// The threaded sequencer against a fake clock that jumps to every requested time: a shipped song is sent with every
/// message at its exact scheduled time (100 ns ticks), with volume scaling, fades, events and failures.
/// </summary>
public sealed class MidiSequencerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>Four seconds, no notes: program at 0 s, pan at 2 s, End-of-Track at 4 s (1 tick = 10 ms).</summary>
    private static readonly MidiFile QuietSong = MidiFile.Parse(Smf(0, 100, Track(
        0x00, 0xFF, 0x51, 0x03, 0x0F, 0x42, 0x40, 0x00, 0xC0, 0, 0x81, 0x48, 0xB0, 10, 64, 0x81, 0x48, 0xFF, 0x2F, 0x00)));

    [Fact]
    public void A_shipped_song_is_sent_at_its_exact_times()
    {
        MidiFile song = MidiFile.Load(Path.Combine(TestPaths.ContentFolder, "Music", "Jingle Bells (Reggae).mid"));
        var clock = new JumpingMusicClock();
        var output = new RecordingMidiOutput(() => clock.Now);
        var events = new ConcurrentQueue<MusicEvent>();
        int finishedCount = 0;
        using var finished = new ManualResetEventSlim();
        using var sequencer = new MidiSequencer(output, clock, NullAppLog.Instance);
        sequencer.EventSent += (_, e) => events.Enqueue(e);
        sequencer.Finished += (_, _) =>
        {
            Interlocked.Increment(ref finishedCount);
            finished.Set();
        };

        sequencer.Play(song);

        Assert.True(finished.Wait(Timeout));
        long start = MidiPlayback.ResetSettleTime.Ticks;
        SentMessage[] messages = [.. output.Messages];
        Assert.Equal(0, messages[0].Time);
        Assert.Equal(MidiPlayback.GmSystemOn, messages[0].SysEx);
        Assert.All(messages[1..17], m => Assert.Equal((start, 7, 100), (m.Time, (int)m.Data1, (int)m.Data2)));
        SentMessage[] body = messages[17..^48];
        Assert.Equal(song.Events.Count, body.Length);
        for (int i = 0; i < body.Length; i++)
        {
            MidiEvent expected = song.Events[i];
            Assert.Equal((start + expected.TimeMicroseconds * 10, expected.Status, expected.Data1, expected.Data2), (body[i].Time, body[i].Status, body[i].Data1, body[i].Data2));
        }

        Assert.All(messages[^48..], m => Assert.Equal(start + song.DurationMicroseconds * 10, m.Time));
        MusicEvent[] noteOns = events.Where(e => e.Kind == MusicEventKind.NoteOn).ToArray();
        Assert.Equal(song.Events.Count(e => (e.Status & 0xF0) == 0x90 && e.Data2 > 0), noteOns.Length);
        Assert.Equal(song.BeatMicroseconds.Select(t => start + t * 10), events.Where(e => e.Kind == MusicEventKind.Beat).Select(e => e.Timestamp));
        Assert.Equal(1, Volatile.Read(ref finishedCount));
        Assert.Equal(TimeSpan.FromMicroseconds(song.DurationMicroseconds), sequencer.Position);
    }

    [Fact]
    public void Volume_scales_cc7_at_once()
    {
        var clock = new JumpingMusicClock(limit: TimeSpan.TicksPerSecond);
        var output = new RecordingMidiOutput(() => clock.Now);
        using var sequencer = new MidiSequencer(output, clock, NullAppLog.Instance) { Volume = 0.6 };

        sequencer.Play(QuietSong);

        WaitFor(() => output.Messages.Any(m => m.Status == 0xBF && m.Data1 == 7));
        Assert.Equal(Enumerable.Repeat(60, 16), output.Messages.Skip(1).Take(16).Select(m => (int)m.Data2));
        output.Clear();
        sequencer.Volume = 0.3;
        Assert.Equal(16, output.Messages.Count(m => m.Data1 == 7 && m.Data2 == 30));
    }

    [Fact]
    public void A_fade_in_raises_cc7_step_by_step_to_full_volume()
    {
        var clock = new JumpingMusicClock();
        var output = new RecordingMidiOutput(() => clock.Now);
        using var finished = new ManualResetEventSlim();
        using var sequencer = new MidiSequencer(output, clock, NullAppLog.Instance);
        sequencer.Finished += (_, _) => finished.Set();

        sequencer.FadeTo(0, TimeSpan.Zero);
        sequencer.FadeTo(1, TimeSpan.FromSeconds(1));
        sequencer.Play(QuietSong);

        Assert.True(finished.Wait(Timeout));
        var channel0Volume = output.Messages.Where(m => m.Status == 0xB0 && m.Data1 == 7).Select(m => (m.Time, (int)m.Data2)).ToArray();
        Assert.Equal((MidiPlayback.ResetSettleTime.Ticks, 5), channel0Volume[0]);
        Assert.Equal((TimeSpan.TicksPerSecond, 100), channel0Volume[^1]);
        Assert.True(channel0Volume.Length > 30, $"only {channel0Volume.Length} steps");
        Assert.True(channel0Volume.Zip(channel0Volume.Skip(1)).All(p => p.First.Item2 < p.Second.Item2 && p.Second.Time - p.First.Time <= TimeSpan.TicksPerMillisecond * 25));
    }

    [Fact]
    public void A_device_failure_mid_song_raises_failed()
    {
        MidiFile song = MidiFile.Load(Path.Combine(TestPaths.ContentFolder, "Music", "Jingle Bells.mid"));
        var clock = new JumpingMusicClock();
        var output = new RecordingMidiOutput(() => clock.Now);
        Exception? failure = null;
        bool finishedRaised = false;
        using var failed = new ManualResetEventSlim();
        using var sequencer = new MidiSequencer(output, clock, NullAppLog.Instance);
        sequencer.EventSent += (_, _) => output.FailWith ??= new MidiOutputException(6, "The device was removed.");
        sequencer.Failed += (_, e) =>
        {
            failure = e.GetException();
            failed.Set();
        };
        sequencer.Finished += (_, _) => finishedRaised = true;

        sequencer.Play(song);

        Assert.True(failed.Wait(Timeout));
        Assert.IsType<MidiOutputException>(failure);
        Assert.False(finishedRaised);
    }

    [Fact]
    public void Play_reports_a_device_that_fails_at_once()
    {
        var clock = new JumpingMusicClock();
        var output = new RecordingMidiOutput(() => clock.Now) { FailWith = new MidiOutputException(6, "No device.") };
        using var sequencer = new MidiSequencer(output, clock, NullAppLog.Instance);

        Assert.Throws<MidiOutputException>(() => sequencer.Play(QuietSong));
    }

    [Fact]
    public void Dispose_stops_the_thread_and_closes_the_device()
    {
        var clock = new JumpingMusicClock();
        var output = new RecordingMidiOutput(() => clock.Now);
        var sequencer = new MidiSequencer(output, clock, NullAppLog.Instance);

        sequencer.Dispose();
        sequencer.Dispose();

        Assert.True(output.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => sequencer.Play(QuietSong));
    }

    /// <summary>A beat at 0 ms, notes at 100 and 200 ms, End-of-Track at 300 ms (1 tick = 10 ms); sent 50 ms later (settle).</summary>
    private static readonly MidiFile TwoNotes = MidiFile.Parse(Smf(0, 100, Track(
        0x00, 0xFF, 0x51, 0x03, 0x0F, 0x42, 0x40, 0x0A, 0x90, 60, 100, 0x0A, 0x90, 64, 100, 0x0A, 0xFF, 0x2F, 0x00)));

    private static long Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds).Ticks;

    [Fact]
    public void Events_are_stamped_when_the_gs_synth_is_heard_and_raised_40_ms_before()
    {
        var clock = new JumpingMusicClock();
        var output = new RecordingMidiOutput(() => clock.Now);
        var raised = new ConcurrentQueue<(long At, MusicEvent Event)>();
        long finishedAt = -1;
        using var finished = new ManualResetEventSlim();
        using var sequencer = new MidiSequencer(output, clock, NullAppLog.Instance, MidiOutputDevices.GsWavetableLatency)
        {
            SyncOffset = TimeSpan.FromMilliseconds(40),
        };
        sequencer.EventSent += (_, e) => raised.Enqueue((clock.Now, e));
        sequencer.Finished += (_, _) =>
        {
            finishedAt = clock.Now;
            finished.Set();
        };

        sequencer.Play(TwoNotes);

        Assert.True(finished.Wait(Timeout));
        Assert.Equal([Ms(150), Ms(250)], output.Messages.Where(m => m.Status == 0x90).Select(m => m.Time));

        // Sent at 50, 150 and 250 ms; heard 190 ms (the synthesizer) + 40 ms (music.syncOffsetMs) later; raised 40 ms ahead.
        Assert.Equal(
            [(Ms(240), MusicEventKind.Beat, Ms(280)), (Ms(340), MusicEventKind.NoteOn, Ms(380)), (Ms(440), MusicEventKind.NoteOn, Ms(480))],
            raised.Select(r => (r.At, r.Event.Kind, r.Event.Timestamp)));

        // The song ends (End-of-Track sent at 350 ms) once its last event was raised.
        Assert.Equal(Ms(440), finishedAt);
    }

    [Fact]
    public void Without_a_device_latency_events_are_raised_at_once_as_before()
    {
        var clock = new JumpingMusicClock();
        var output = new RecordingMidiOutput(() => clock.Now);
        var raised = new ConcurrentQueue<(long At, MusicEvent Event)>();
        using var finished = new ManualResetEventSlim();
        using var sequencer = new MidiSequencer(output, clock, NullAppLog.Instance) { SyncOffset = TimeSpan.FromMilliseconds(40) };
        sequencer.EventSent += (_, e) => raised.Enqueue((clock.Now, e));
        sequencer.Finished += (_, _) => finished.Set();

        sequencer.Play(TwoNotes);

        Assert.True(finished.Wait(Timeout));
        Assert.Equal([(Ms(50), Ms(90)), (Ms(150), Ms(190)), (Ms(250), Ms(290))], raised.Select(r => (r.At, r.Event.Timestamp)));
    }

    [Fact]
    public void Pause_and_stop_drop_events_that_were_not_raised_yet()
    {
        var clock = new ManualMusicClock();
        var output = new RecordingMidiOutput(() => clock.Now);
        var raised = new ConcurrentQueue<MusicEvent>();
        bool finishedRaised = false;
        using var sequencer = new MidiSequencer(output, clock, NullAppLog.Instance, MidiOutputDevices.GsWavetableLatency)
        {
            SyncOffset = TimeSpan.FromMilliseconds(40),
        };
        sequencer.EventSent += (_, e) => raised.Enqueue(e);
        sequencer.Finished += (_, _) => finishedRaised = true;

        sequencer.Play(TwoNotes);
        clock.Set(TimeSpan.FromMilliseconds(50));
        WaitFor(() => output.Messages.Any(m => m.Status == 0xBF && m.Data1 == 7));
        clock.Set(TimeSpan.FromMilliseconds(150));
        WaitFor(() => output.Messages.Any(m => m.Status == 0x90));
        clock.Set(TimeSpan.FromMilliseconds(245));
        WaitFor(() => !raised.IsEmpty);
        Thread.Sleep(20);
        Assert.Equal(MusicEventKind.Beat, raised.Single().Kind);

        // The first note (sent at 150 ms) would be raised at 340 ms: the pause drops it with the music.
        sequencer.Pause();
        clock.Set(TimeSpan.FromSeconds(1));
        Thread.Sleep(50);
        Assert.Single(raised);

        // After Resume the song goes on (the second note is sent 5 ms later), and Stop drops its event too.
        sequencer.Resume();
        clock.Set(TimeSpan.FromMilliseconds(1010));
        WaitFor(() => output.Messages.Count(m => m.Status == 0x90) == 2);
        sequencer.Stop();
        clock.Set(TimeSpan.FromSeconds(2));
        Thread.Sleep(50);
        Assert.Single(raised);
        Assert.False(finishedRaised);
    }

    [Fact]
    public void A_pause_after_the_end_of_track_drops_the_last_events_and_ends_the_song_at_once()
    {
        var clock = new ManualMusicClock();
        var output = new RecordingMidiOutput(() => clock.Now);
        var raised = new ConcurrentQueue<MusicEvent>();
        using var finished = new ManualResetEventSlim();
        using var sequencer = new MidiSequencer(output, clock, NullAppLog.Instance, MidiOutputDevices.GsWavetableLatency)
        {
            SyncOffset = TimeSpan.FromMilliseconds(40),
        };
        sequencer.EventSent += (_, e) => raised.Enqueue(e);
        sequencer.Finished += (_, _) => finished.Set();

        sequencer.Play(TwoNotes);
        foreach (int ms in new[] { 50, 150, 250, 350 })
        {
            int sends = output.Messages.Count;
            clock.Set(TimeSpan.FromMilliseconds(ms));
            WaitFor(() => output.Messages.Count > sends);
        }

        // The End-of-Track went out at 350 ms; the second note (heard at 480 ms) is still to be raised.
        WaitFor(() => raised.Count == 2);
        Assert.False(finished.Wait(50));
        sequencer.Pause();

        Assert.True(finished.Wait(Timeout));
        Assert.Equal(2, raised.Count);
    }

    [Fact]
    public void The_midi_song_player_publishes_events_with_the_time_the_gs_synth_is_heard()
    {
        var clock = new JumpingMusicClock();
        var output = new RecordingMidiOutput(() => clock.Now);
        var hub = new HolidayLights.Audio.Events.MusicEventHub();
        using IMusicEventReader reader = hub.Subscribe();
        using var ended = new ManualResetEventSlim();
        var sequencer = new MidiSequencer(output, clock, NullAppLog.Instance, MidiOutputDevices.GsWavetableLatency);
        using var player = new HolidayLights.Audio.Playback.MidiSongPlayer(
            TwoNotes, sequencer, new HolidayLights.Audio.Playback.SongPlaybackContext("", TimeSpan.FromMilliseconds(40), hub));
        player.Ended += (_, _) => ended.Set();

        player.Start(1, TimeSpan.Zero);

        Assert.True(ended.Wait(Timeout));
        var buffer = new MusicEvent[16];
        MusicEvent[] published = buffer[..reader.Read(buffer)];
        Assert.Equal(
            [(MusicEventKind.Beat, Ms(280)), (MusicEventKind.NoteOn, Ms(380)), (MusicEventKind.NoteOn, Ms(480))],
            published.Select(e => (e.Kind, e.Timestamp)));
    }

    [Fact]
    public void The_gs_synth_sounds_190_ms_after_a_message_and_other_devices_at_once()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(190), MidiOutputDevices.LatencyOf("Microsoft GS Wavetable Synth"));
        Assert.Equal(TimeSpan.Zero, MidiOutputDevices.LatencyOf("Roland Integra"));
        Assert.Equal(TimeSpan.Zero, MidiOutputDevices.LatencyOf(""));
        Assert.Equal("Loopback MIDI", MidiOutputDevices.NameOf(1, ["Microsoft GS Wavetable Synth", "Loopback MIDI"]));
        Assert.Equal("", MidiOutputDevices.NameOf(0xFFFFFFFF, []));
    }

    private static void WaitFor(Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < Timeout, "Timed out.");
            Thread.Sleep(5);
        }
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("Loopback MIDI", 2)]
    [InlineData("loopback midi", 2)]
    [InlineData("Unplugged Keyboard", 1)]
    public void The_midi_output_choice_falls_back_to_the_gs_synth(string configured, uint expected) =>
        Assert.Equal(expected, MidiOutputDevices.Choose(configured, ["Roland Integra", "Microsoft GS Wavetable Synth", "Loopback MIDI"]));

    [Fact]
    public void Without_a_gs_synth_device_0_is_used_and_without_devices_the_mapper()
    {
        Assert.Equal(0u, MidiOutputDevices.Choose("", ["Roland Integra", "Loopback MIDI"]));
        Assert.Equal(0xFFFFFFFFu, MidiOutputDevices.Choose("", []));
    }
}
