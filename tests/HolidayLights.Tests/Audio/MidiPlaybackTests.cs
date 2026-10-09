using HolidayLights.Audio.Midi;
using static HolidayLights.Tests.Audio.SmfBuilder;

namespace HolidayLights.Tests.Audio;

/// <summary>
/// The sequencer's scheduling core on explicit times (clock ticks of 100 ns): what is sent when, CC7 volume scaling,
/// pause/resume with state re-sent, device resets and the end of a song.
/// </summary>
public sealed class MidiPlaybackTests
{
    private const long Second = 10_000_000;

    /// <summary>Song time 0 begins after the GM System On settle time.</summary>
    private const long LeadIn = Second / 20;

    /// <summary>100 ticks per quarter at 1,000,000 us per quarter: 1 tick = 10 ms.</summary>
    private static readonly MidiFile Song = MidiFile.Parse(Smf(0, 100, Track(
        0x00, 0xFF, 0x51, 0x03, 0x0F, 0x42, 0x40,
        0x00, 0xC0, 5,
        0x00, 0xB0, 10, 64,
        0x00, 0xB0, 7, 100,
        0x00, 0x90, 60, 100,
        0x32, 0x80, 60, 0,
        0x00, 0xB1, 7, 80,
        0x00, 0x91, 64, 90,
        0x32, 0xF0, 0x05, 0x7E, 0x7F, 0x09, 0x01, 0xF7,
        0x32, 0x81, 64, 0,
        0x32, 0xFF, 0x2F, 0x00)));

    private long now;

    [Fact]
    public void Start_sends_gm_system_on_then_after_50_ms_scaled_volume_on_every_channel()
    {
        var output = new RecordingMidiOutput(() => now);
        var playback = new MidiPlayback(Song, output, Second, level: 0.6);

        playback.Start(now);

        Assert.Equal(MidiPlayback.GmSystemOn, Assert.Single(output.Messages).SysEx);
        Assert.Equal(LeadIn, playback.NextDue);
        playback.SetLevel(0.6);
        Assert.Single(output.Messages);
        now = LeadIn;
        playback.Advance(now, []);
        Assert.Equal(
            Enumerable.Range(0, 16).Select(ch => (LeadIn, (byte)(0xB0 | ch), (byte)7, (byte)60)),
            output.Messages.Skip(1).Take(16).Select(m => (m.Time, m.Status, m.Data1, m.Data2)));
        Assert.Equal(0, playback.PositionMicroseconds(now));
    }

    [Fact]
    public void Every_event_is_sent_at_its_time_and_the_end_resets_every_channel()
    {
        var output = new RecordingMidiOutput(() => now);
        var playback = new MidiPlayback(Song, output, Second, level: 1);
        var sent = new List<MusicEvent>();
        playback.Start(now);
        output.Clear();

        bool finished = RunToEnd(playback, sent);

        Assert.True(finished);
        SentMessage[] messages = [.. output.Messages.Skip(16).Select(m => m with { Time = m.Time - LeadIn })];
        Assert.Equal((0L, 0xC0, 5), Describe(messages[0]));
        Assert.Equal((0L, 0xB0, 10), Describe(messages[1]));
        Assert.Equal((0L, 0xB0, 7), Describe(messages[2]));
        Assert.Equal((0L, 0x90, 60), Describe(messages[3]));
        Assert.Equal((Second / 2, 0x80, 60), Describe(messages[4]));
        Assert.Equal((Second / 2, 0xB1, 7), Describe(messages[5]));
        Assert.Equal((Second / 2, 0x91, 64), Describe(messages[6]));
        Assert.Equal(Second, messages[7].Time);
        Assert.Equal(MidiPlayback.GmSystemOn, messages[7].SysEx);

        // The GM reset in the song sets every channel back to CC7 100: the scaled volume follows it at once.
        Assert.Equal(Enumerable.Range(0, 16).Select(ch => (Second, 0xB0 | ch, 7)), messages.Skip(8).Take(16).Select(Describe));
        Assert.Equal((Second * 3 / 2, 0x81, 64), Describe(messages[24]));
        SentMessage[] reset = messages[25..];
        Assert.Equal(48, reset.Length);
        Assert.All(reset, m => Assert.Equal(Second * 2, m.Time));
        Assert.Equal(
            Enumerable.Range(0, 16).SelectMany(ch => new[] { (0xB0 | ch, 120), (0xB0 | ch, 123), (0xB0 | ch, 121) }),
            reset.Select(m => ((int)m.Status, (int)m.Data1)));

        Assert.Equal(long.MaxValue, playback.NextDue);
        Assert.Equal(2_000_000, playback.PositionMicroseconds(now));
    }

    [Fact]
    public void Note_ons_and_quarter_note_beats_are_reported_at_send_time()
    {
        var output = new RecordingMidiOutput(() => now);
        var playback = new MidiPlayback(Song, output, Second, level: 1);
        var sent = new List<MusicEvent>();
        playback.Start(now);

        RunToEnd(playback, sent);

        Assert.Equal(
            [
                new MusicEvent(MusicEventKind.NoteOn, LeadIn, 0, 60, 100),
                new MusicEvent(MusicEventKind.Beat, LeadIn, 0, 0, 127),
                new MusicEvent(MusicEventKind.NoteOn, LeadIn + Second / 2, 1, 64, 90),
                new MusicEvent(MusicEventKind.Beat, LeadIn + Second, 0, 0, 127),
            ],
            sent);
    }

    [Fact]
    public void Volume_scales_the_songs_own_cc7_and_resends_only_changed_channels()
    {
        var output = new RecordingMidiOutput(() => now);
        var playback = new MidiPlayback(Song, output, Second, level: 0.5);
        var sent = new List<MusicEvent>();
        playback.Start(now);
        AdvanceTo(playback, LeadIn + Second / 2, sent);
        output.Clear();

        playback.SetLevel(0.25);

        // Channel 1 now has the song's CC7 80 (-> 20); every other channel the default 100 (-> 25).
        Assert.Equal(
            Enumerable.Range(0, 16).Select(ch => ((byte)(0xB0 | ch), (byte)7, (byte)(ch == 1 ? 20 : 25))),
            output.Messages.Select(m => (m.Status, m.Data1, m.Data2)));
        output.Clear();
        playback.SetLevel(0.25);
        Assert.Empty(output.Messages);
    }

    [Fact]
    public void Pause_silences_and_resume_sends_program_and_controllers_then_continues_in_time()
    {
        var output = new RecordingMidiOutput(() => now);
        var playback = new MidiPlayback(Song, output, Second, level: 1);
        var sent = new List<MusicEvent>();
        playback.Start(now);
        AdvanceTo(playback, LeadIn + Second / 4, sent);
        output.Clear();

        playback.Pause(now);

        Assert.Equal(
            Enumerable.Range(0, 16).SelectMany(ch => new[] { (0xB0 | ch, 123), (0xB0 | ch, 120) }),
            output.Messages.Select(m => ((int)m.Status, (int)m.Data1)));
        Assert.Equal(long.MaxValue, playback.NextDue);
        now += Second;
        Assert.Equal(250_000, playback.PositionMicroseconds(now));
        output.Clear();

        playback.Resume(now);

        SentMessage[] replay = [.. output.Messages];
        Assert.Equal((0xC0, 5, 0), ((int)replay[0].Status, (int)replay[0].Data1, (int)replay[0].Data2));
        Assert.Contains(replay, m => m.Status == 0xB0 && m.Data1 == 10 && m.Data2 == 64);
        Assert.Equal(16, replay.Count(m => m.Data1 == 7 && (m.Status & 0xF0) == 0xB0));
        Assert.Equal(now + Second / 4, playback.NextDue);
        Assert.Equal(250_000, playback.PositionMicroseconds(now));
    }

    [Fact]
    public void Stop_resets_every_channel_once()
    {
        var output = new RecordingMidiOutput(() => now);
        var playback = new MidiPlayback(Song, output, Second, level: 1);
        playback.Start(now);
        output.Clear();

        playback.Stop();
        playback.Stop();

        Assert.Equal(48, output.Messages.Count);
        Assert.True(playback.IsFinished);
    }

    private static (long Time, int Status, int Data1) Describe(SentMessage m) => (m.Time, m.Status, m.Data1);

    private bool RunToEnd(MidiPlayback playback, List<MusicEvent> sent)
    {
        bool finished = false;
        while (!finished && playback.NextDue != long.MaxValue)
        {
            now = playback.NextDue;
            finished = playback.Advance(now, sent);
        }

        return finished;
    }

    private void AdvanceTo(MidiPlayback playback, long target, List<MusicEvent> sent)
    {
        while (playback.NextDue <= target)
        {
            now = playback.NextDue;
            playback.Advance(now, sent);
        }

        now = target;
    }
}
