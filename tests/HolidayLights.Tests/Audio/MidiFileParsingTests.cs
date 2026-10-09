using HolidayLights.Audio.Midi;
using static HolidayLights.Tests.Audio.SmfBuilder;

namespace HolidayLights.Tests.Audio;

/// <summary>The tolerant SMF reader on hand-built files: what Windows' sequencer plays, Holiday Lights plays.</summary>
public sealed class MidiFileParsingTests
{
    [Fact]
    public void Running_status_is_kept_across_meta_events()
    {
        byte[] file = Smf(0, 96, Track(0x00, 0x90, 60, 100, 0x00, 0xFF, 0x01, 0x01, (byte)'x', 0x10, 62, 90, 0x10, 0x80, 60, 0, EndOfTrack));

        MidiFile song = MidiFile.Parse(file);

        Assert.Equal([(0x90, 60, 100), (0x90, 62, 90), (0x80, 60, 0)], song.Events.Select(e => ((int)e.Status, (int)e.Data1, (int)e.Data2)));
    }

    [Fact]
    public void Note_on_with_velocity_zero_is_passed_on_unchanged()
    {
        MidiFile song = MidiFile.Parse(Smf(0, 96, Track(0x00, 0x91, 64, 80, 0x60, 64, 0, EndOfTrack)));

        Assert.Equal([(0x91, 80), (0x91, 0)], song.Events.Select(e => ((int)e.Status, (int)e.Data2)));
    }

    [Fact]
    public void Tempo_map_converts_ticks_exactly()
    {
        // 96 ticks per quarter; tick 0: 500,000 us/quarter; tick 192: 250,000 us/quarter.
        byte[] file = Smf(1, 96,
            Track(0x00, 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20, 0x81, 0x40, 0xFF, 0x51, 0x03, 0x03, 0xD0, 0x90, EndOfTrack),
            Track(0x81, 0x40, 0x90, 60, 100, 0x81, 0x40, 0x80, 60, 0, 0x60, 0xFF, 0x2F, 0x00));

        MidiFile song = MidiFile.Parse(file);

        Assert.Equal([new MidiTempoChange(0, 500_000), new MidiTempoChange(192, 250_000)], song.TempoChanges);
        Assert.Equal([1_000_000L, 1_500_000L], song.Events.Select(e => e.TimeMicroseconds));
        Assert.Equal(1_750_000, song.DurationMicroseconds);
        Assert.Equal(1_500_000, song.LastEventMicroseconds);
        Assert.Equal([0L, 500_000, 1_000_000, 1_250_000, 1_500_000], song.BeatMicroseconds);
    }

    [Fact]
    public void Tracks_merge_in_time_order_and_keep_track_order_on_ties()
    {
        byte[] file = Smf(1, 96,
            Track(0x00, 0xC0, 5, 0x60, 0x90, 60, 100, EndOfTrack),
            Track(0x00, 0xC1, 7, 0x00, 0xB1, 7, 90, 0x30, 0x91, 64, 100, EndOfTrack));

        MidiFile song = MidiFile.Parse(file);

        Assert.Equal([0xC0, 0xC1, 0xB1, 0x91, 0x90], song.Events.Select(e => (int)e.Status));
        Assert.Equal(2, song.TrackCount);
    }

    [Fact]
    public void SysEx_keeps_its_leading_byte_and_escapes_stay_raw()
    {
        byte[] file = Smf(0, 96, Track(0x00, 0xF0, 0x05, 0x7E, 0x7F, 0x09, 0x01, 0xF7, 0x00, 0xF7, 0x02, 0xF8, 0xFA, EndOfTrack));

        MidiFile song = MidiFile.Parse(file);

        Assert.Equal([0xF0, 0x7E, 0x7F, 0x09, 0x01, 0xF7], song.Events[0].SysEx);
        Assert.Equal([0xF8, 0xFA], song.Events[1].SysEx);
        Assert.Equal(0xF7, song.Events[1].Status);
    }

    [Fact]
    public void Riff_wrapped_rmi_files_are_unwrapped()
    {
        byte[] smf = Smf(0, 96, Track(0x00, 0x90, 60, 100, 0x60, 0x80, 60, 0, EndOfTrack));
        byte[] info = [.. "LIST"u8, 4, 0, 0, 0, .. "INFO"u8];
        byte[] data = [.. "data"u8, .. LittleEndian(smf.Length), .. smf];
        byte[] rmi = [.. "RIFF"u8, .. LittleEndian(4 + info.Length + data.Length), .. "RMID"u8, .. info, .. data];

        MidiFile song = MidiFile.Parse(rmi);

        Assert.Equal(2, song.Events.Count);
        Assert.Equal(500_000, song.DurationMicroseconds);
    }

    [Fact]
    public void Damaged_tracks_keep_the_events_before_the_damage()
    {
        // Track 1 ends without End-of-Track in the middle of a message; track 2 has a stray system byte.
        byte[] file = Smf(1, 96,
            Track(0x00, 0x90, 60, 100, 0x60, 0x80, 60),
            Track(0x00, 0x91, 61, 100, 0x30, 0xF2, 0x01, 0x02));

        MidiFile song = MidiFile.Parse(file);

        Assert.Equal([(0x90, 60), (0x91, 61)], song.Events.Select(e => ((int)e.Status, (int)e.Data1)));
        Assert.Equal(2, song.TrackCount);
        Assert.Equal(500_000, song.DurationMicroseconds);
    }

    [Fact]
    public void Unknown_chunks_and_chunks_running_past_the_end_are_tolerated()
    {
        byte[] track = Track(0x00, 0x90, 60, 100, 0x60, 0x80, 60, 0, EndOfTrack);
        byte[] unknown = [.. "XFIH"u8, 0, 0, 0, 2, 0xAA, 0xBB];
        byte[] truncated = [.. "MTrk"u8, 0, 0, 0x10, 0, 0x00, 0x92, 70, 100];
        byte[] file = [.. Header(1, 3, 96), .. unknown, .. track, .. truncated];

        MidiFile song = MidiFile.Parse(file);

        Assert.Equal(2, song.TrackCount);
        Assert.Equal([0x90, 0x92, 0x80], song.Events.Select(e => (int)e.Status));
    }

    [Fact]
    public void Smpte_division_counts_frames()
    {
        // -25 frames per second, 40 ticks per frame: 1,000 ticks per second; Set Tempo is ignored.
        int division = (0x100 - 25) << 8 | 40;
        byte[] file = Smf(0, division, Track(0x00, 0xFF, 0x51, 0x03, 0x01, 0x00, 0x00, 0x87, 0x68, 0x90, 60, 100, 0x87, 0x68, 0xFF, 0x2F, 0x00));

        MidiFile song = MidiFile.Parse(file);

        Assert.Equal(1_000_000, song.Events[0].TimeMicroseconds);
        Assert.Equal(2_000_000, song.DurationMicroseconds);
        Assert.Equal([0L, 500_000, 1_000_000, 1_500_000], song.BeatMicroseconds);
    }

    [Theory]
    [InlineData(new byte[] { 0x4D, 0x54, 0x72, 0x6B, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 4, 0, 0, 0, 0x57, 0x41, 0x56, 0x45 })]
    [InlineData(new byte[] { 0x4D, 0x54, 0x68, 0x64, 0, 0, 0, 6, 0, 0, 0, 1, 0, 0 })]
    public void Files_that_are_not_midi_are_rejected(byte[] data) =>
        Assert.Throws<InvalidDataException>(() => MidiFile.Parse(data));
}
