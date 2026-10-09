using HolidayLights.Audio.Native;
using HolidayLights.Audio.Timing;

namespace HolidayLights.Audio.Midi;

/// <summary>
/// Plays a <see cref="MidiFile"/> over WinMM (<c>midiOutShortMsg</c>/<c>midiOutLongMsg</c>), paced by a high-resolution
/// waitable timer on its own above-normal thread: GM System On before each song, CC 120/123/121 on all 16 channels at
/// stop, volume by CC7 scaling, pause/resume with program and controller state re-sent, note-on and quarter-note beat
/// events stamped with the time they are heard (PRODUCT-SPEC 6.1.3, 5.10). Owner: audio.
/// </summary>
/// <remarks>Every member may be called from any thread. Events are raised on the sequencer thread; keep handlers short.</remarks>
public sealed class MidiSequencer : IDisposable
{
    private const string LogSource = "Audio.Midi";

    /// <summary>
    /// How long before it is heard an event is raised: the lights read music events every 30 ms and schedule each flash
    /// ahead, so they need it a little early, but not so early that a later event of the same group overtakes it.
    /// </summary>
    internal static readonly TimeSpan EventLead = TimeSpan.FromMilliseconds(40);

    /// <summary>CC7 updates during a fade (40 per second: smooth, and far below the MIDI bandwidth).</summary>
    private static readonly TimeSpan FadeStep = TimeSpan.FromMilliseconds(25);

    private readonly Lock gate = new();
    private readonly IMidiOutput output;
    private readonly IMusicClock clock;
    private readonly IAppLog log;
    private readonly AutoResetEvent wake = new(false);
    private readonly Thread thread;
    private readonly List<MusicEvent> sent = [];
    private readonly Queue<MusicEvent> waiting = new();
    private readonly List<MusicEvent> raising = [];
    private MidiPlayback? playback;
    private double volume = 1;
    private GainRamp gain = GainRamp.Constant(1);
    private TimeSpan syncOffset;
    private bool finishWaiting;
    private bool disposed;

    /// <summary>Opens a MIDI output device.</summary>
    /// <param name="deviceName">"MIDI Output" name; empty = the first device whose name contains "GS Wavetable", else device 0, else the mapper.</param>
    /// <param name="log">The log.</param>
    /// <exception cref="IOException">The device cannot be opened (<c>MMSYSERR_ALLOCATED</c> when another app holds the synthesizer).</exception>
    public MidiSequencer(string deviceName, IAppLog log)
        : this(OpenDevice(deviceName), log)
    {
    }

    /// <summary>Creates a sequencer on an output and a clock (tests use a recorder and a fake clock).</summary>
    /// <param name="output">The output; disposed with the sequencer.</param>
    /// <param name="clock">The clock; disposed with the sequencer when it is disposable.</param>
    /// <param name="log">The log.</param>
    /// <param name="deviceLatency">How long the output takes to sound a message (<see cref="DeviceLatency"/>).</param>
    internal MidiSequencer(IMidiOutput output, IMusicClock clock, IAppLog log, TimeSpan deviceLatency = default)
    {
        this.output = output;
        this.clock = clock;
        this.log = log;
        DeviceLatency = deviceLatency;
        thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "Holiday Lights MIDI" };
        thread.Start();
    }

    private MidiSequencer((IMidiOutput Output, TimeSpan Latency) device, IAppLog log)
        : this(device.Output, new HighResolutionClock(), log, device.Latency)
    {
    }

    /// <summary>
    /// Raised on the sequencer thread for each note-on (velocity above 0) and each quarter-note beat. The timestamp is when
    /// the sound is heard: the send time + <see cref="DeviceLatency"/> + <see cref="SyncOffset"/>. The event is raised 40 ms
    /// before that time (at once when that is no later than the send), so the lights can schedule it without running ahead
    /// of the music. Events still waiting are dropped by <see cref="Pause"/>, <see cref="Stop"/> and a new <see cref="Play"/>.
    /// </summary>
    public event EventHandler<MusicEvent>? EventSent;

    /// <summary>Raised on the sequencer thread when a song reached its End-of-Track and every event of it was raised.</summary>
    public event EventHandler? Finished;

    /// <summary>Raised on the sequencer thread when the device failed while a song played; the song is abandoned.</summary>
    public event EventHandler<ErrorEventArgs>? Failed;

    /// <summary>Volume 0-1 (CC7 scaling on every channel, re-sent at once).</summary>
    public double Volume
    {
        get
        {
            lock (gate)
            {
                return volume;
            }
        }

        set
        {
            lock (gate)
            {
                volume = Math.Clamp(value, 0, 1);
                playback?.SetLevel(volume * gain.At(clock.Now));
            }
        }
    }

    /// <summary>Elapsed time of the current song.</summary>
    public TimeSpan Position
    {
        get
        {
            lock (gate)
            {
                return TimeSpan.FromMicroseconds(playback?.PositionMicroseconds(clock.Now) ?? 0);
            }
        }
    }

    /// <summary>How long the open device takes to sound a message it was sent (the Microsoft GS Wavetable Synth: 190 ms; other devices: zero).</summary>
    public TimeSpan DeviceLatency { get; }

    /// <summary>
    /// The rest of the way from the device to the listener (<c>music.syncOffsetMs</c>: the audio engine, device buffers and
    /// speakers, or an external synthesizer), added to <see cref="DeviceLatency"/> in event timestamps. Applies to the events
    /// sent from then on.
    /// </summary>
    public TimeSpan SyncOffset
    {
        get
        {
            lock (gate)
            {
                return syncOffset;
            }
        }

        set
        {
            lock (gate)
            {
                syncOffset = value < TimeSpan.Zero ? TimeSpan.Zero : value;
            }
        }
    }

    /// <summary>The MIDI output device names in device order.</summary>
    /// <returns>Device names.</returns>
    public static IReadOnlyList<string> GetDeviceNames() => MidiOutputDevices.GetNames();

    /// <summary>Starts a song from the beginning.</summary>
    /// <param name="song">The song.</param>
    /// <exception cref="MidiOutputException">The device failed.</exception>
    public void Play(MidiFile song)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            playback?.Stop();
            DropWaitingEventsLocked();
            long now = clock.Now;
            var next = new MidiPlayback(song, output, clock.Frequency, volume * gain.At(now));
            playback = next;
            next.Start(now);
        }

        wake.Set();
    }

    /// <summary>
    /// Pauses (all notes off; position kept). Events not raised yet are dropped: the lights stop with the music. A song
    /// that already sent its End-of-Track has nothing left to pause: <see cref="Finished"/> is raised at once.
    /// </summary>
    /// <exception cref="MidiOutputException">The device failed.</exception>
    public void Pause()
    {
        lock (gate)
        {
            waiting.Clear();
            playback?.Pause(clock.Now);
        }

        wake.Set();
    }

    /// <summary>Resumes from the paused position.</summary>
    /// <exception cref="MidiOutputException">The device failed.</exception>
    public void Resume()
    {
        lock (gate)
        {
            if (playback is { } current)
            {
                long now = clock.Now;
                current.SetLevel(volume * gain.At(now));
                current.Resume(now);
            }
        }

        wake.Set();
    }

    /// <summary>Changes the gain that multiplies <see cref="Volume"/> gradually (fade-in of the first song, fade-out at exit).</summary>
    /// <param name="target">The gain 0-1 to reach.</param>
    /// <param name="duration">How long the change takes; zero is immediate.</param>
    public void FadeTo(double target, TimeSpan duration)
    {
        lock (gate)
        {
            long now = clock.Now;
            long length = (long)(duration.TotalSeconds * clock.Frequency);
            gain = new GainRamp(gain.At(now), Math.Clamp(target, 0, 1), now, length);
            playback?.SetLevel(volume * gain.At(now));
        }

        wake.Set();
    }

    /// <summary>Stops and resets every channel.</summary>
    public void Stop()
    {
        lock (gate)
        {
            StopPlaybackLocked();
        }

        wake.Set();
    }

    /// <summary>Stops and closes the device.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            StopPlaybackLocked();
        }

        wake.Set();
        thread.Join();
        output.Dispose();
        (clock as IDisposable)?.Dispose();
        wake.Dispose();
    }

    private void Run()
    {
        NativeMethods.DisablePowerThrottlingForCurrentThread();
        while (true)
        {
            long due;
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                due = NextDueLocked();
            }

            if (!clock.WaitUntil(due, wake))
            {
                continue;
            }

            if (!AdvanceOnce(out bool finished, out Exception? failure))
            {
                return;
            }

            foreach (MusicEvent e in raising)
            {
                EventSent?.Invoke(this, e);
            }

            raising.Clear();
            if (failure is not null)
            {
                Failed?.Invoke(this, new ErrorEventArgs(failure));
            }
            else if (finished)
            {
                Finished?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Sends what is due and collects the events to raise now; returns false when the sequencer was disposed.</summary>
    private bool AdvanceOnce(out bool finished, out Exception? failure)
    {
        finished = false;
        failure = null;
        lock (gate)
        {
            if (disposed)
            {
                return false;
            }

            if (playback is { IsFinished: false } current)
            {
                try
                {
                    long now = clock.Now;
                    current.SetLevel(volume * gain.At(now));
                    finishWaiting |= current.Advance(now, sent);
                    long heardAfter = ToTicks(DeviceLatency + syncOffset);
                    foreach (MusicEvent e in sent)
                    {
                        waiting.Enqueue(e with { Timestamp = e.Timestamp + heardAfter });
                    }
                }
                catch (MidiOutputException ex)
                {
                    log.Warn(LogSource, "The MIDI device failed while a song played.", ex);
                    failure = ex;
                    playback = null;
                    DropWaitingEventsLocked();
                }
                finally
                {
                    sent.Clear();
                }
            }

            long raiseUntil = clock.Now + ToTicks(EventLead);
            while (waiting.Count > 0 && waiting.Peek().Timestamp <= raiseUntil)
            {
                raising.Add(waiting.Dequeue());
            }

            if (finishWaiting && waiting.Count == 0)
            {
                finishWaiting = false;
                finished = true;
            }
        }

        return true;
    }

    private long NextDueLocked()
    {
        long next = long.MaxValue;
        if (playback is { } current)
        {
            next = current.NextDue;
            long now = clock.Now;
            if (next != long.MaxValue && gain.IsRunning(now))
            {
                long step = (long)(FadeStep.TotalSeconds * clock.Frequency);
                next = Math.Min(next, Math.Min(now + step, gain.Start + gain.Length));
            }
        }

        if (waiting.Count > 0)
        {
            next = Math.Min(next, waiting.Peek().Timestamp - ToTicks(EventLead));
        }
        else if (finishWaiting)
        {
            // Paused after the End-of-Track: nothing is left to raise, so the end is due now.
            next = clock.Now;
        }

        return next;
    }

    private void StopPlaybackLocked()
    {
        DropWaitingEventsLocked();
        try
        {
            playback?.Stop();
        }
        catch (MidiOutputException ex)
        {
            log.Warn(LogSource, "Resetting the MIDI channels failed.", ex);
        }

        playback = null;
    }

    private void DropWaitingEventsLocked()
    {
        waiting.Clear();
        finishWaiting = false;
    }

    private static (IMidiOutput Output, TimeSpan Latency) OpenDevice(string deviceName)
    {
        IReadOnlyList<string> names = MidiOutputDevices.GetNames();
        uint id = MidiOutputDevices.Choose(deviceName, names);
        return (WinMmMidiOutput.Open(id), MidiOutputDevices.LatencyOf(MidiOutputDevices.NameOf(id, names)));
    }

    private long ToTicks(TimeSpan time) => (long)((Int128)time.Ticks * clock.Frequency / TimeSpan.TicksPerSecond);
}
