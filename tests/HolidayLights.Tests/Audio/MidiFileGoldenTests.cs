using System.Security.Cryptography;
using System.Text.Json;
using HolidayLights.Audio.Midi;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Audio;

/// <summary>
/// The 47 MIDI files of the 5.4 distribution against golden <c>midi.json</c>: the 46 bundled songs (<c>Content\Music</c>)
/// and the hidden <c>reset.mid</c> (not bundled; a test fixture).
/// </summary>
public sealed class MidiFileGoldenTests
{
    private const string ResetFile = "reset.mid";
    private static readonly Lazy<Dictionary<string, JsonElement>> Records = new(LoadRecords);

    public static TheoryData<string> FileNames()
    {
        var data = new TheoryData<string>();
        foreach (string name in Records.Value.Keys.Order(StringComparer.Ordinal))
        {
            data.Add(name);
        }

        return data;
    }

    [Fact]
    public void Golden_covers_every_shipped_midi_file()
    {
        string[] onDisk = Directory.GetFiles(Path.Combine(TestPaths.ContentFolder, "Music"), "*.mid")
            .Select(Path.GetFileName)
            .Append(ResetFile)
            .Order(StringComparer.Ordinal)
            .ToArray()!;

        Assert.Equal(47, Records.Value.Count);
        Assert.Equal(onDisk, Records.Value.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(FileNames))]
    public void Song_matches_golden(string fileName)
    {
        JsonElement golden = Records.Value[fileName];
        byte[] bytes = File.ReadAllBytes(PathOf(fileName));
        Assert.Equal(golden.GetProperty("bytes").GetInt32(), bytes.Length);
        Assert.Equal(golden.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));

        MidiFile song = MidiFile.Parse(bytes);
        Assert.Equal(golden.GetProperty("format").GetInt32(), song.Format);
        Assert.Equal(golden.GetProperty("trackCount").GetInt32(), song.TrackCount);
        Assert.Equal(golden.GetProperty("division").GetInt32(), song.Division);
        Assert.Equal(golden.GetProperty("durationMicroseconds").GetInt64(), song.DurationMicroseconds);
        Assert.Equal(golden.GetProperty("lastEventMicroseconds").GetInt64(), song.LastEventMicroseconds);
        Assert.Equal(
            golden.GetProperty("tempoChanges").EnumerateArray().Select(t => new MidiTempoChange(t.GetProperty("tick").GetInt64(), t.GetProperty("usPerQuarter").GetInt32())),
            song.TempoChanges);

        JsonElement totals = golden.GetProperty("totals");
        Assert.Equal(CountsOf(totals), ChannelCounts(song.Events.Select(e => (e.Status, e.Data2))));
    }

    [Theory]
    [MemberData(nameof(FileNames))]
    public void Tracks_match_golden(string fileName)
    {
        JsonElement golden = Records.Value[fileName];
        SmfDocument document = SmfReader.Read(File.ReadAllBytes(PathOf(fileName)));
        Assert.Equal(golden.GetProperty("trackCountHeader").GetInt32(), document.DeclaredTrackCount);
        Assert.Equal(golden.GetProperty("lastTick").GetInt64(), document.Tracks.Max(t => t.EndTick));
        Assert.Equal(
            golden.GetProperty("lastEventTick").GetInt64(),
            document.Tracks.SelectMany(t => t.Events).Where(e => !e.IsEndOfTrack).Select(e => e.Tick).DefaultIfEmpty().Max());

        var tempoTracks = document.Tracks
            .SelectMany((track, index) => track.Events.Where(e => e.IsMeta && e.MetaType == SmfEvent.SetTempoType).Select(e => (e.Tick, index)))
            .Order()
            .Select(t => t.index);
        Assert.Equal(golden.GetProperty("tempoChanges").EnumerateArray().Select(t => t.GetProperty("track").GetInt32()), tempoTracks);

        JsonElement[] tracks = [.. golden.GetProperty("tracks").EnumerateArray()];
        Assert.Equal(tracks.Length, document.Tracks.Count);
        for (int i = 0; i < tracks.Length; i++)
        {
            SmfTrack track = document.Tracks[i];
            Assert.Equal(tracks[i].GetProperty("bytes").GetInt32(), track.Length);
            Assert.Equal(tracks[i].GetProperty("events").GetInt32(), track.Events.Count);
            Assert.Equal(tracks[i].GetProperty("lastTick").GetInt64(), track.EndTick);
            Assert.True(track.HasEndOfTrack);
            Assert.Equal(CountsOf(tracks[i].GetProperty("counts")), ChannelCounts(track.Events.Where(e => !e.IsMeta).Select(e => (e.Status, e.Data2))));
            Assert.Equal(MetaCountsOf(tracks[i].GetProperty("meta")), MetaCounts(track.Events));
            int velocityZero = tracks[i].GetProperty("quirks").TryGetProperty("noteOnVelocity0", out JsonElement quirk) ? quirk.GetInt32() : 0;
            Assert.Equal(velocityZero, track.Events.Count(e => (e.Status & 0xF0) == 0x90 && e.Data2 == 0));
        }
    }

    [Theory]
    [MemberData(nameof(FileNames))]
    public void Beats_are_the_quarter_notes_before_the_end(string fileName)
    {
        JsonElement golden = Records.Value[fileName];
        MidiFile song = MidiFile.Load(PathOf(fileName));
        long lastTick = golden.GetProperty("lastTick").GetInt64();
        int division = golden.GetProperty("division").GetInt32();

        Assert.Equal((lastTick + division - 1) / division, song.BeatMicroseconds.Count);
        Assert.Equal(0, song.BeatMicroseconds[0]);
        Assert.All(song.BeatMicroseconds, beat => Assert.InRange(beat, 0, song.DurationMicroseconds - 1));
        Assert.True(song.BeatMicroseconds.Zip(song.BeatMicroseconds.Skip(1)).All(p => p.First < p.Second));
    }

    [Fact]
    public void Reset_file_is_the_2003_controller_reset()
    {
        MidiFile reset = MidiFile.Load(PathOf(ResetFile));

        Assert.Equal(16, reset.Events.Count);
        Assert.All(reset.Events, e => Assert.Equal((0, 121, 0), ((int)e.TimeMicroseconds, (int)e.Data1, (int)e.Data2)));
        Assert.Equal(Enumerable.Range(0xB0, 16).Select(s => (byte)s), reset.Events.Select(e => e.Status));
        Assert.Equal(2_000_000, reset.DurationMicroseconds);
    }

    /// <summary>A bundled song, or the <c>reset.mid</c> fixture.</summary>
    private static string PathOf(string fileName) =>
        fileName == ResetFile ? TestPaths.Fixture(Path.Combine("Music", ResetFile)) : Path.Combine(TestPaths.ContentFolder, "Music", fileName);

    private static Dictionary<string, JsonElement> LoadRecords()
    {
        using JsonDocument document = GoldenData.ReadJson("midi.json");
        return document.RootElement.GetProperty("files").EnumerateArray()
            .ToDictionary(f => f.GetProperty("file").GetString()!, f => f.Clone(), StringComparer.Ordinal);
    }

    private static int[] CountsOf(JsonElement counts) =>
    [
        counts.GetProperty("noteOn").GetInt32(),
        counts.GetProperty("noteOff").GetInt32(),
        counts.GetProperty("controlChange").GetInt32(),
        counts.GetProperty("programChange").GetInt32(),
        counts.GetProperty("pitchBend").GetInt32(),
        counts.GetProperty("polyPressure").GetInt32(),
        counts.GetProperty("channelPressure").GetInt32(),
        counts.GetProperty("sysex").GetInt32(),
    ];

    /// <summary>The golden counting rules: note-on with velocity 0 is a note-off; F0 and F7 are SysEx.</summary>
    private static int[] ChannelCounts(IEnumerable<(byte Status, byte Data2)> events)
    {
        var counts = new int[8];
        foreach ((byte status, byte data2) in events)
        {
            int index = status switch
            {
                0xF0 or 0xF7 => 7,
                _ => (status & 0xF0) switch
                {
                    0x90 when data2 > 0 => 0,
                    0x80 or 0x90 => 1,
                    0xB0 => 2,
                    0xC0 => 3,
                    0xE0 => 4,
                    0xA0 => 5,
                    0xD0 => 6,
                    _ => throw new InvalidOperationException($"Unexpected status 0x{status:X2}."),
                },
            };
            counts[index]++;
        }

        return counts;
    }

    private static Dictionary<string, int> MetaCountsOf(JsonElement meta) =>
        meta.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());

    private static Dictionary<string, int> MetaCounts(IEnumerable<SmfEvent> events) =>
        events.Where(e => e.IsMeta).GroupBy(e => $"0x{e.MetaType:X2}").ToDictionary(g => g.Key, g => g.Count());
}
