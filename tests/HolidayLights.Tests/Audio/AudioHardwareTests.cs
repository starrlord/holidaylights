using System.Collections.Concurrent;
using System.Diagnostics;
using HolidayLights.Audio;
using HolidayLights.Audio.Events;
using HolidayLights.Audio.Library;
using HolidayLights.Audio.Midi;
using HolidayLights.Audio.Native;
using HolidayLights.Audio.Playback;
using HolidayLights.Tests.Shared;
using NAudio.Wave;
using static HolidayLights.Tests.Audio.SmfBuilder;

namespace HolidayLights.Tests.Audio;

/// <summary>
/// A test that needs a MIDI output device that can open (skipped on machines without one, and while no audio output exists:
/// the Microsoft GS Wavetable Synth then fails to open with MMSYSERR_ERROR). It never plays a note.
/// </summary>
public sealed class MidiDeviceFactAttribute : FactAttribute
{
    public MidiDeviceFactAttribute()
    {
        if (NativeMethods.midiOutGetNumDevs() == 0)
        {
            Skip = "No MIDI output device.";
        }
        else if (WaveOut.DeviceCount == 0 && UsesTheGsSynth())
        {
            Skip = "No audio output: the Microsoft GS Wavetable Synth cannot open.";
        }
    }

    private static bool UsesTheGsSynth()
    {
        IReadOnlyList<string> names = MidiOutputDevices.GetNames();
        return MidiOutputDevices.NameOf(MidiOutputDevices.Choose("", names), names).Contains("GS Wavetable", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>A test that needs an audio output device (skipped on machines without one). It only plays silence.</summary>
public sealed class AudioOutputFactAttribute : FactAttribute
{
    public AudioOutputFactAttribute()
    {
        if (WaveOut.DeviceCount == 0)
        {
            Skip = "No audio output device.";
        }
    }
}

/// <summary>The one audible test: opt-in with HOLIDAYLIGHTS_LIVE_AUDIO=1 (plays 1 s of a song at low volume).</summary>
public sealed class LiveAudioFactAttribute : FactAttribute
{
    public const string Variable = "HOLIDAYLIGHTS_LIVE_AUDIO";

    public LiveAudioFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
        {
            Skip = $"Plays sound: set {Variable}=1 to run it.";
        }
    }
}

/// <summary>The real WinMM and WaveOut paths, silently; one opt-in live smoke test with sound.</summary>
[Collection(nameof(AudioHardwareCollection))]
public sealed class AudioHardwareTests
{
    /// <summary>300 ms without notes: controllers only (1 tick = 10 ms).</summary>
    private static readonly MidiFile SilentSong = MidiFile.Parse(Smf(0, 100, Track(
        0x00, 0xFF, 0x51, 0x03, 0x0F, 0x42, 0x40, 0x00, 0xB0, 10, 64, 0x1E, 0xFF, 0x2F, 0x00)));

    [MidiDeviceFact]
    [Trait("Category", "Audio")]
    public void The_synthesizer_paces_a_silent_song_with_the_high_resolution_timer()
    {
        Assert.NotEmpty(MidiSequencer.GetDeviceNames());
        MidiSequencer sequencer;
        try
        {
            sequencer = new MidiSequencer("", NullAppLog.Instance);
        }
        catch (MidiOutputException ex) when (ex.IsDeviceBusy)
        {
            // Another app holds the synthesizer right now: exactly the state the Music Box waits out.
            return;
        }

        using (sequencer)
        using (var finished = new ManualResetEventSlim())
        {
            sequencer.Finished += (_, _) => finished.Set();
            sequencer.Volume = 0;
            var watch = Stopwatch.StartNew();

            sequencer.Play(SilentSong);

            Assert.True(finished.Wait(TimeSpan.FromSeconds(10)));
            Assert.InRange(watch.Elapsed.TotalMilliseconds, 295, 2000);
        }
    }

    [MidiDeviceFact]
    [Trait("Category", "Audio")]
    public async Task The_music_box_plays_a_bundled_song_end_to_end_while_muted()
    {
        using var root = new TempDataRoot();
        var settings = new InMemorySettingsStore();
        using var library = new MusicLibrary(root.Paths, settings, new TestHoldingFolder(root.Paths), new FakeShellOperations(), NullAppLog.Instance);
        library.Start();
        using var director = new MusicDirector(library, NullAppLog.Instance);
        using IMusicEventReader reader = director.Events.Subscribe();
        string only = MediaIds.Bundled("Good King Wenceslaus.mid");
        var others = library.Songs.Select(s => s.Id).Where(id => id != only).ToHashSet(MediaIds.Comparer);
        long started = Stopwatch.GetTimestamp();

        // Muted: CC7 is 0 on every channel, so the synthesizer stays silent while the whole engine runs.
        director.ApplyPolicy(new MusicPolicy { Enabled = true, Mode = PlayMode.Always, Muted = true, DisabledSongs = others });

        var buffer = new MusicEvent[512];
        var received = new List<MusicEvent>();
        var watch = Stopwatch.StartNew();
        while (!received.Any(e => e.Kind == MusicEventKind.NoteOn) && watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (director.State.Status == MusicStatus.WaitingForSynthesizer)
            {
                // Another app holds the synthesizer right now: exactly the state the Music Box waits out.
                return;
            }

            received.AddRange(buffer.Take(reader.Read(buffer)));
            await Task.Delay(20);
        }

        Assert.Equal(MusicStatus.Playing, director.State.Status);
        Assert.Equal(only, director.State.CurrentSong?.Id);
        Assert.Equal(MusicEventKind.SongStarted, received[0].Kind);
        MusicEvent note = received.First(e => e.Kind == MusicEventKind.NoteOn);
        Assert.InRange(Stopwatch.GetElapsedTime(started, note.Timestamp).TotalMilliseconds, 40, 10_000);
        await director.StopAsync(TimeSpan.FromMilliseconds(200)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(MusicStatus.BetweenSongs, director.State.Status);
    }

    [AudioOutputFact]
    [Trait("Category", "Audio")]
    public void An_audio_file_plays_to_its_end_through_waveout()
    {
        string folder = Path.Combine(Path.GetTempPath(), "HolidayLightsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "silence.wav");
        MusicLibraryTests.WriteWave(path, TimeSpan.FromMilliseconds(300));
        try
        {
            using var ended = new ManualResetEventSlim();
            Exception? error = null;
            using (AudioFileSongPlayer player = AudioFileSongPlayer.Open(path, SongKind.Wav, new MusicEventHub()))
            {
                player.Ended += (_, e) =>
                {
                    error = e.Error;
                    ended.Set();
                };

                player.Start(volume: 0, fadeIn: TimeSpan.Zero);

                Assert.True(ended.Wait(TimeSpan.FromSeconds(10)));
                Assert.Null(error);
                Assert.Equal(TimeSpan.FromMilliseconds(300), player.Position);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [LiveAudioFact]
    [Trait("Category", "Live")]
    public async Task Live_one_second_of_jingle_bells_at_low_volume()
    {
        var song = new SongInfo
        {
            Id = MediaIds.Bundled("Jingle Bells.mid"),
            Title = "Jingle Bells",
            FilePath = Path.Combine(TestPaths.ContentFolder, "Music", "Jingle Bells.mid"),
            Origin = MediaOrigin.Bundled,
            Kind = SongKind.Midi,
            SortKey = "JINGLE BELLS.MID",
        };
        using var director = new MusicDirector(new SingleSongLibrary(song), NullAppLog.Instance);
        using IMusicEventReader reader = director.Events.Subscribe();
        var states = new ConcurrentQueue<MusicStatus>();
        director.StateChanged += (_, _) => states.Enqueue(director.State.Status);

        director.ApplyPolicy(new MusicPolicy { Enabled = true, Mode = PlayMode.Always, Volume = 15 });
        await Task.Delay(TimeSpan.FromSeconds(1));
        await director.StopAsync(TimeSpan.FromMilliseconds(300)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains(MusicStatus.Playing, states);
        var buffer = new MusicEvent[256];
        Assert.Contains(buffer.Take(reader.Read(buffer)), e => e.Kind == MusicEventKind.NoteOn);
    }

    /// <summary>
    /// Dance to the Music on the real Microsoft GS Wavetable Synth (PRODUCT-SPEC 5.10): three quiet notes through the app's
    /// MIDI song player; the default render endpoint is recorded through WASAPI loopback with QPC packet times. Each note
    /// event must be stamped when its sound is heard (no earlier than it reaches the audio engine, at most 45 ms after),
    /// and published shortly before that time. Before the fix the stamps were about 178 ms early.
    /// </summary>
    [LiveAudioFact]
    [Trait("Category", "Live")]
    public void Live_dance_events_are_stamped_when_the_gs_synth_is_heard()
    {
        if (!MidiSequencer.GetDeviceNames().Any(n => n.Contains("GS Wavetable", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        // 1 tick = 10 ms: piano, note 76 at 200, 900 and 1600 ms (70 ms each), End-of-Track at 2170 ms.
        byte[] smf = Smf(0, 100, Track(
            0x00, 0xFF, 0x51, 0x03, 0x0F, 0x42, 0x40, 0x00, 0xC0, 0,
            20, 0x90, 76, 100, 7, 0x80, 76, 0, 63, 0x90, 76, 100, 7, 0x80, 76, 0, 63, 0x90, 76, 100, 7, 0x80, 76, 0,
            50, 0xFF, 0x2F, 0x00));
        string folder = Path.Combine(Path.GetTempPath(), "HolidayLightsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "three-notes.mid");
        File.WriteAllBytes(path, smf);
        var hub = new MusicEventHub();
        using IMusicEventReader reader = hub.Subscribe();
        var arrivals = new List<(long ReadAt, MusicEvent Event)>();
        LoopbackRecorder? recorder = null;
        try
        {
            recorder = LoopbackRecorder.TryStart();
            if (recorder is null)
            {
                return;
            }

            Thread.Sleep(300);
            MidiSongPlayer player;
            try
            {
                player = MidiSongPlayer.Open(path, new SongPlaybackContext("", TimeSpan.FromMilliseconds(40), hub), NullAppLog.Instance);
            }
            catch (MidiOutputException ex) when (ex.IsDeviceBusy)
            {
                return;
            }

            using (player)
            using (var ended = new ManualResetEventSlim())
            {
                player.Ended += (_, _) => ended.Set();
                player.Start(volume: 0.2, fadeIn: TimeSpan.Zero);
                var buffer = new MusicEvent[64];
                var watch = Stopwatch.StartNew();
                while (!ended.IsSet && watch.Elapsed < TimeSpan.FromSeconds(6))
                {
                    int count = reader.Read(buffer);
                    long readAt = Stopwatch.GetTimestamp();
                    arrivals.AddRange(buffer.Take(count).Select(e => (readAt, e)));
                    Thread.Sleep(1);
                }

                Assert.True(ended.IsSet, "the song did not end");
            }

            Thread.Sleep(400);
            recorder.Stop();
            var notes = arrivals.Where(a => a.Event.Kind == MusicEventKind.NoteOn).ToList();
            Assert.Equal(3, notes.Count);
            foreach ((long readAt, MusicEvent note) in notes)
            {
                double? onset = recorder.FirstSoundAfter(note.Timestamp - (Stopwatch.Frequency * 150 / 1000));
                Assert.True(onset.HasValue, "no sound was recorded after a note event");
                double heardAfterEngine = Milliseconds(note.Timestamp) - onset.Value;
                double lead = Milliseconds(note.Timestamp) - Milliseconds(readAt);
                Assert.True(heardAfterEngine is >= -15 and <= 45, $"stamped {heardAfterEngine:0.0} ms after the sound reached the audio engine");
                Assert.True(lead is >= 0 and <= 60, $"published {lead:0.0} ms before it is heard");
            }
        }
        finally
        {
            recorder?.Stop();
            Directory.Delete(folder, recursive: true);
        }
    }

    private static double Milliseconds(long stopwatchTimestamp) => stopwatchTimestamp * 1000.0 / Stopwatch.Frequency;

    /// <summary>WASAPI loopback of the default render endpoint: the peak of every frame with its QPC time (ms on the Stopwatch clock).</summary>
    private sealed class LoopbackRecorder
    {
        private readonly NAudio.CoreAudioApi.MMDeviceEnumerator enumerator;
        private readonly NAudio.CoreAudioApi.MMDevice device;
        private readonly NAudio.CoreAudioApi.AudioClient client;
        private readonly NAudio.CoreAudioApi.AudioCaptureClient capture;
        private readonly int channels;
        private readonly double rate;
        private readonly Thread thread;
        private readonly List<(double Ms, float Peak)> frames = new(400_000);
        private volatile bool running = true;
        private bool stopped;

        private LoopbackRecorder(NAudio.CoreAudioApi.MMDeviceEnumerator enumerator, NAudio.CoreAudioApi.MMDevice device, NAudio.CoreAudioApi.AudioClient client)
        {
            this.enumerator = enumerator;
            this.device = device;
            this.client = client;
            WaveFormat format = client.MixFormat;
            channels = format.Channels;
            rate = format.SampleRate;
            client.Initialize(NAudio.CoreAudioApi.AudioClientShareMode.Shared, NAudio.CoreAudioApi.AudioClientStreamFlags.Loopback, 2_000_000, 0, format, Guid.Empty);
            capture = client.AudioCaptureClient;
            thread = new Thread(Read) { IsBackground = true, Priority = ThreadPriority.Highest };
            client.Start();
            thread.Start();
        }

        /// <summary>Starts recording, or returns null without a render endpoint or with a mix format other than 32-bit float.</summary>
        public static LoopbackRecorder? TryStart()
        {
            var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            if (!enumerator.HasDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia))
            {
                enumerator.Dispose();
                return null;
            }

            NAudio.CoreAudioApi.MMDevice device = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            NAudio.CoreAudioApi.AudioClient client = device.CreateAudioClient();
            if (client.MixFormat.BitsPerSample != 32)
            {
                client.Dispose();
                device.Dispose();
                enumerator.Dispose();
                return null;
            }

            return new LoopbackRecorder(enumerator, device, client);
        }

        /// <summary>The time (ms on the Stopwatch clock) of the first frame clearly above the noise after a Stopwatch timestamp.</summary>
        public double? FirstSoundAfter(long stopwatchTimestamp)
        {
            double from = Milliseconds(stopwatchTimestamp);
            float noise = 0;
            foreach ((double ms, float peak) in frames)
            {
                if (ms >= from - 100 && ms < from)
                {
                    noise = Math.Max(noise, peak);
                }
            }

            float threshold = Math.Max(0.0005f, noise * 4);
            foreach ((double ms, float peak) in frames)
            {
                if (ms >= from && peak > threshold)
                {
                    return ms;
                }
            }

            return null;
        }

        public void Stop()
        {
            if (stopped)
            {
                return;
            }

            stopped = true;
            running = false;
            thread.Join();
            client.Stop();
            client.Dispose();
            device.Dispose();
            enumerator.Dispose();
        }

        private void Read()
        {
            while (running)
            {
                while (capture.GetNextPacketSize() > 0)
                {
                    IntPtr data = capture.GetBuffer(out int count, out NAudio.CoreAudioApi.AudioClientBufferFlags flags, out _, out long qpc);
                    bool silent = (flags & NAudio.CoreAudioApi.AudioClientBufferFlags.Silent) != 0;
                    double start = qpc / 1e4;
                    for (int i = 0; i < count; i++)
                    {
                        float peak = 0;
                        if (!silent)
                        {
                            for (int c = 0; c < channels; c++)
                            {
                                peak = Math.Max(peak, Math.Abs(System.Runtime.InteropServices.Marshal.PtrToStructure<float>(data + (((i * channels) + c) * 4))));
                            }
                        }

                        frames.Add((start + (i * 1000.0 / rate), peak));
                    }

                    capture.ReleaseBuffer(count);
                }

                Thread.Sleep(2);
            }
        }
    }

    /// <summary>A library with one song.</summary>
    private sealed class SingleSongLibrary(SongInfo song) : ISongLibrary
    {
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<SongInfo> Songs { get; } = [song];

        public IReadOnlyList<SongInfo> HiddenSongs { get; } = [];

        public void Start()
        {
        }

        public bool TryGetSong(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SongInfo? found)
        {
            found = MediaIds.Comparer.Equals(id, song.Id) ? song : null;
            return found is not null;
        }

        public IReadOnlyList<MediaImportResult> AddFiles(IEnumerable<string> paths) => [];

        public HeldItem? Remove(string id) => null;

        public void Restore(HeldItem item)
        {
        }

        public void RestoreHiddenSongs()
        {
        }
    }
}

/// <summary>Hardware tests share one synthesizer and one output: they never run in parallel with each other.</summary>
[CollectionDefinition(nameof(AudioHardwareCollection), DisableParallelization = true)]
public sealed class AudioHardwareCollection;
