namespace HolidayLights.App.ScreenSaver.Scene;

/// <summary>
/// One running saver: the scenes of its displays on one 60 ms timeline, with music events for "Dance to the Music". Each
/// frame, <see cref="Advance"/> runs the due simulation steps, feeds the music and samples the bulbs; the front end then
/// draws with <see cref="Progress"/>.
/// </summary>
/// <remarks>Used by one thread (the front end's). Dispose to stop listening to music events.</remarks>
internal sealed class SaverRun : IDisposable
{
    private const int MusicBufferSize = 256;

    private readonly SaverTimeline timeline;
    private readonly MusicEvent[] musicBuffer = new MusicEvent[MusicBufferSize];
    private IMusicEventReader? music;

    /// <summary>Starts the run.</summary>
    /// <param name="scenes">The scenes, one per display that shows content.</param>
    /// <param name="startTimestamp">When the run starts (the bulbs' step 0 and the origin of the simulation steps).</param>
    public SaverRun(IReadOnlyList<SaverScene> scenes, long startTimestamp)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        Scenes = scenes;
        timeline = new SaverTimeline(startTimestamp);
    }

    /// <summary>The scenes.</summary>
    public IReadOnlyList<SaverScene> Scenes { get; }

    /// <summary>How far into the current step the last <see cref="Advance"/> was (0-1).</summary>
    public float Progress { get; private set; }

    /// <summary>Follows music events for "Dance to the Music" from now on (replacing an earlier source).</summary>
    /// <param name="source">The events, or null for none.</param>
    public void ListenTo(IMusicEventSource? source)
    {
        music?.Dispose();
        music = source?.Subscribe();
    }

    /// <summary>Runs the steps due at a moment, feeds the music and samples the bulbs.</summary>
    /// <param name="timestamp">Now.</param>
    /// <param name="stepped">Called after each simulation step of each scene (front ends keep stamps and snow).</param>
    /// <returns>The number of steps run.</returns>
    public int Advance(long timestamp, Action<SaverScene> stepped)
    {
        ArgumentNullException.ThrowIfNull(stepped);
        int steps = timeline.Advance(timestamp);
        for (int i = 0; i < steps; i++)
        {
            foreach (SaverScene scene in Scenes)
            {
                scene.Simulation.Step();
                stepped(scene);
            }
        }

        FeedMusic();
        foreach (SaverScene scene in Scenes)
        {
            scene.Bulbs?.Sample(timestamp);
        }

        Progress = timeline.Progress(timestamp);
        return steps;
    }

    /// <summary>Stops listening to music events.</summary>
    public void Dispose() => ListenTo(null);

    private void FeedMusic()
    {
        if (music is null)
        {
            return;
        }

        int count;
        while ((count = music.Read(musicBuffer)) > 0)
        {
            ReadOnlySpan<MusicEvent> events = musicBuffer.AsSpan(0, count);
            foreach (SaverScene scene in Scenes)
            {
                scene.Bulbs?.ApplyMusic(events);
            }
        }
    }
}
