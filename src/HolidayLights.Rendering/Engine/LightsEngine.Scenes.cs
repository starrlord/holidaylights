using System.Diagnostics;
using HolidayLights.Rendering.Animation;
using HolidayLights.Rendering.Composition;
using HolidayLights.Rendering.Presentation;
using HolidayLights.Rendering.Scenes;
using HolidayLights.Rendering.Shell;

namespace HolidayLights.Rendering.Engine;

/// <summary>Scenes: preparation on the thread pool, applying them to the visuals, power-ups, theme transitions, steps and music.</summary>
internal sealed partial class LightsEngine
{
    private readonly MusicEvent[] musicBuffer = new MusicEvent[512];
    private SceneRequest? pendingRequest;
    private PreparedRequest? deferredScene;
    private Task<PreparedScene>? preparation;
    private SceneTransition preparationTransition;
    private long preparationStartedAt;
    private PreparedScene? prepared;
    private BulbAnimator? animator;
    private IMusicEventReader? music;
    private long nextMusicPoll;
    private bool musicUnavailable;

    private LightsScene? Scene => prepared?.Scene;

    private bool MusicWanted => music is not null && animator is { IsDancePattern: true } && clock.IsRunning && AnyLayerVisible;

    /// <summary>Command: show a scene (the latest request wins; see <see cref="SceneRequest.Merge"/>).</summary>
    public void RequestScene(SceneRequest request)
    {
        pendingRequest = SceneRequest.Merge(pendingRequest, request);
        StartNextPreparation();
    }

    private void StartNextPreparation()
    {
        if (preparation is not null || pendingRequest is not { } request || device is null || stopping)
        {
            return;
        }

        pendingRequest = null;
        LightsScene next = request.Scene;
        bool newSequencer = prepared is null || animator is null || !SceneComparison.SameSequencerInputs(prepared.Scene, next);
        if (!newSequencer && SceneComparison.SameSprites(prepared!.Scene, next))
        {
            ApplyScene(new PreparedScene(next, null, prepared.Bulbs, prepared.Images, prepared.SkippedBulbs), request.Transition);
            return;
        }

        long startStep = RestartsPattern(next, request.Transition) ? 0 : Math.Max(0, clock.IsRunning ? clock.Clock.StepAt(Now) : clock.LastStep);
        preparationTransition = request.Transition;
        preparationStartedAt = Now;
        Task<PreparedScene> task = preparer.PrepareAsync(next, new SequencerRequest(newSequencer, startStep), lifetime.Token);
        preparation = task;
        task.ContinueWith(_ => Post(engine => engine.OnPrepared(task)), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void OnPrepared(Task<PreparedScene> task)
    {
        if (!ReferenceEquals(task, preparation))
        {
            return;
        }

        preparation = null;
        if (task.IsCanceled || stopping)
        {
            return;
        }

        if (task.Exception is { } error)
        {
            Log.Error(LogSource, "The lights scene could not be prepared; the previous lights stay.", error.GetBaseException());
        }
        else if (device is null)
        {
            // The device went away while the scene was prepared: show it as soon as a device exists again.
            deferredScene = new PreparedRequest(task.Result, preparationTransition);
        }
        else
        {
            lastPreparationMilliseconds = (Now - preparationStartedAt) * 1000.0 / Stopwatch.Frequency;
            ApplyScene(task.Result, preparationTransition);
        }

        StartNextPreparation();
    }

    /// <summary>A prepared scene waiting for a graphics device.</summary>
    private readonly record struct PreparedRequest(PreparedScene Scene, SceneTransition Transition);

    private bool RestartsPattern(LightsScene next, SceneTransition transition) =>
        PowerUpPlan.IsPowerUp(transition)
        || prepared is null
        || prepared.Scene.Flash.Pattern != next.Flash.Pattern
        || prepared.Scene.Flash.StopFlashing != next.Flash.StopFlashing;

    /// <summary>Shows a prepared scene: displays, bulbs (kept, faded in or out), layer moves, the clock, power-ups and visibility.</summary>
    private void ApplyScene(PreparedScene next, SceneTransition transition)
    {
        if (device is null || batch is null || surfaces is null)
        {
            return;
        }

        long now = Now;
        LightsScene? previous = Scene;
        LightsScene scene = next.Scene;
        bool restart = RestartsPattern(scene, transition);
        bool reduced = scene.Effects.ReducedMotion;
        float intensity = GlowLevels.Intensity(scene.Effects.Glow);
        bool powerUp = PowerUpPlan.IsPowerUp(transition) && scene.LightsOn;
        bool themeFade = transition == SceneTransition.ThemeTransition && AnyLayerVisible;
        double oldSceneFade = themeFade ? (reduced ? MotionTimings.ReducedMotionCrossfade : MotionTimings.ThemeFadeOut) : 0;
        double powerUpDelay = themeFade && !reduced ? MotionTimings.ThemeFadeOut : 0;
        bool newAnimator = next.NewSequencer is not null || animator is null;
        bool glowChanged = !newAnimator && animator!.GlowIntensity != intensity;

        prepared = next;
        if (next.NewSequencer is { } sequencer)
        {
            animator = new BulbAnimator(sequencer, intensity);
        }

        if (next.SkippedBulbs > 0)
        {
            Log.Warn(LogSource, $"{next.SkippedBulbs} bulb placement(s) could not be drawn and were left out.");
        }

        if (previous is not null && previous.RequestedLayer != scene.RequestedLayer)
        {
            // A power-up brings its own fades: the layers move at once so the wave runs on them in their new place.
            BeginLayerMoves(now, instant: reduced || transition == SceneTransition.None || powerUp);
        }

        SyncLayers(scene, now, reduced);
        RefreshGlowScales(force: false);
        topology = MonitorTopology.Capture();
        RefreshStaleDisplays();
        VisualContext context = CreateContext(next);
        var freshOrdinals = new List<int>();
        foreach (DisplayLayer layer in layers.Values)
        {
            List<PreparedBulb> bulbs = [.. next.BulbsOn(layer.DisplayId)];
            if (powerUp || layer.Scene is null || layer.Scene.Origin != layer.Origin)
            {
                ReplaceSceneVisuals(layer, bulbs, now, oldSceneFade, crossfade: themeFade && reduced, context);
                freshOrdinals.AddRange(bulbs.Select(b => b.Placement.Ordinal));
            }
            else
            {
                double fade = transition == SceneTransition.Automatic && !reduced && layer.IsVisible ? MotionTimings.BulbFade : 0;
                freshOrdinals.AddRange(ReconcileBulbs(layer.Scene, bulbs, now, fade, context));
            }
        }

        ReleaseUnusedSurfaces();
        UpdateVisibility(now, VisibilityFade(previous, scene, transition), applyState: !powerUp);
        if (powerUp && !reduced)
        {
            StartPowerUp(transition, now, powerUpDelay);
        }
        else
        {
            if (powerUp)
            {
                foreach (DisplayLayer layer in layers.Values.Where(l => l.IsVisible))
                {
                    layer.Scene?.Opacity.Set(OpacityCurves.Ramp(now, 0, 1, MotionTimings.ReducedMotionPowerUp), batch);
                }
            }

            RunPattern(now, restart || powerUp, newAnimator, glowChanged, freshOrdinals);
        }

        ConfigureMusic();
        PublishDiagnostics();
        dirty = true;
    }

    private VisualContext CreateContext(PreparedScene scene)
    {
        var context = new VisualContext(device!, surfaces!, scene.Images, batch!);
        router.Context = context;
        return context;
    }

    private void ReplaceSceneVisuals(DisplayLayer layer, List<PreparedBulb> bulbs, long now, double oldSceneFade, bool crossfade, VisualContext context)
    {
        var visuals = new SceneVisuals(device!, layer.Origin);
        foreach (PreparedBulb bulb in bulbs)
        {
            visuals.Add(bulb, context);
        }

        if (layer.Scene is { } old && oldSceneFade > 0)
        {
            old.Opacity.Set(OpacityCurves.Ramp(now, old.Opacity.Curve.Evaluate(now), 0, oldSceneFade), batch!);
        }

        if (crossfade)
        {
            visuals.Opacity.Set(OpacityCurves.Ramp(now, 0, 1, oldSceneFade), batch!);
        }

        layer.ReplaceScene(visuals, now + Ticks(oldSceneFade), batch!);
    }

    /// <summary>Keeps bulbs whose placement is unchanged, fades new ones in and removed ones out (PRODUCT-SPEC 4.4).</summary>
    /// <returns>The ordinals of bulbs that got new visuals.</returns>
    private static List<int> ReconcileBulbs(SceneVisuals scene, List<PreparedBulb> bulbs, long now, double fade, VisualContext context)
    {
        var wanted = new Dictionary<PlacementKey, PreparedBulb>();
        foreach (PreparedBulb bulb in bulbs)
        {
            wanted[PlacementKey.Of(bulb.Placement)] = bulb;
        }

        foreach (PlacementKey key in scene.Bulbs.Keys.Where(key => !wanted.ContainsKey(key)).ToList())
        {
            scene.Remove(key, now, fade, context);
        }

        var fresh = new List<int>();
        foreach ((PlacementKey key, PreparedBulb bulb) in wanted)
        {
            if (scene.Bulbs.TryGetValue(key, out BulbVisuals? existing))
            {
                if (!ReferenceEquals(existing.Bulb, bulb))
                {
                    existing.Update(bulb, context);
                }
            }
            else
            {
                BulbVisuals added = scene.Add(bulb, context);
                if (fade > 0)
                {
                    added.SetOpacity(OpacityCurves.Ramp(now, 0, 1, fade), context);
                }

                fresh.Add(bulb.Placement.Ordinal);
            }
        }

        return fresh;
    }

    /// <summary>The power-up wave (PRODUCT-SPEC 4.3.2): dark bulbs fade in, the wave lights them, then the pattern starts at step 0.</summary>
    private void StartPowerUp(SceneTransition transition, long now, double delayMilliseconds)
    {
        PowerUpPlan plan = PowerUpPlan.Create(transition, reducedMotion: false, Scene!.Layout);
        var ordinals = new List<int>();
        foreach (DisplayLayer layer in layers.Values.Where(l => l.IsVisible && l.Scene is not null))
        {
            var fadeIn = new OpacityCurveBuilder(now);
            fadeIn.Hold(0, delayMilliseconds / 1000).Linear(0, 1, plan.SceneFadeInMilliseconds / 1000);
            layer.Scene!.Opacity.Set(fadeIn.End(1), batch!);
            ordinals.AddRange(layer.Scene.Bulbs.Values.Select(b => b.Bulb.Placement.Ordinal));
        }

        animator!.ApplyPowerUp(plan, now, delayMilliseconds, ordinals, router);
        clock.StartLater(now + Ticks(delayMilliseconds + plan.PatternStartMilliseconds));
    }

    private void StartPatternAfterPowerUp()
    {
        long start = clock.StartAt!.Value;
        clock.Restart(start, Scene?.Interval ?? clock.Interval);
        PublishClock();
        if (animator is not null)
        {
            animator.AssumeAllLit();
            animator.Step(0, start, clock.TimingAt(Now), router);
            clock.MarkShown(0);
        }

        PublishDiagnostics();
        dirty = true;
    }

    /// <summary>
    /// Starts or continues the pattern without a power-up and shows its current state. A new animator, a restarted pattern and
    /// bulbs that got new visuals first take over what the visuals show, so the step that follows fades from there instead of
    /// jumping (and repeating animations start again in phase with the clock). While a power-up still runs, the pattern waits
    /// for its end (<see cref="StartPatternAfterPowerUp"/> shows every bulb then).
    /// </summary>
    private void RunPattern(long now, bool restart, bool newAnimator, bool glowChanged, List<int> freshOrdinals)
    {
        if (animator is null || Scene is null)
        {
            return;
        }

        if (restart)
        {
            clock.Restart(now, Scene.Interval);
            PublishClock();
        }
        else if (clock.ChangeInterval(Scene.Interval, now))
        {
            PublishClock();
        }

        SyncClock(now);
        WaveTiming timing = clock.TimingAt(now);
        if (glowChanged)
        {
            animator.SetGlowIntensity(GlowLevels.Intensity(Scene.Effects.Glow), now, timing, router);
        }

        if (clock.StartAt is not null)
        {
            return;
        }

        if (newAnimator || restart || freshOrdinals.Count > 0)
        {
            HashSet<int>? fresh = newAnimator || restart ? null : [.. freshOrdinals];
            BulbAnimator current = animator;
            current.SeedShown(ordinal => fresh is null || fresh.Contains(ordinal) ? router.Current(ordinal, now, current.GlowIntensity) : null);
        }

        long step = clock.IsRunning ? Math.Max(0, clock.Clock.StepAt(now)) : Math.Max(0, clock.LastStep);
        animator.Step(step, now, timing, router);
        clock.MarkShown(step);
    }

    private void PollMusic(long now)
    {
        nextMusicPoll = now + MusicPollInterval;
        int count = music!.Read(musicBuffer);
        if (count > 0 && animator is not null)
        {
            animator.ApplyMusic(musicBuffer.AsSpan(0, count), now, clock.TimingAt(now), router);
            dirty = true;
        }
    }

    /// <summary>Subscribes to music events only while "Dance to the Music" can show them.</summary>
    private void ConfigureMusic()
    {
        bool wanted = animator is { IsDancePattern: true } && Scene is { LightsOn: true } && !pause.IsGloballyPaused && !stopping;
        if (wanted && music is null && !musicUnavailable)
        {
            try
            {
                music = dependencies.Music.Subscribe();
                nextMusicPoll = Now;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                musicUnavailable = true;
                Log.Warn(LogSource, "Music events are not available; Dance to the Music glows slowly.", exception);
            }
        }
        else if (!wanted)
        {
            DisposeMusicReader();
        }
    }

    private void DisposeMusicReader()
    {
        music?.Dispose();
        music = null;
    }

    private void RebuildRoutes()
    {
        int count = Scene?.Layout.Placements.Count ?? 0;
        router.Reset(count, layers.Values
            .Where(l => l.IsVisible && l.Scene is not null)
            .SelectMany(l => l.Scene!.Bulbs.Values)
            .Select(b => (b.Bulb.Placement.Ordinal, b)));
    }

    private void ReleaseUnusedSurfaces()
    {
        var live = new HashSet<SpriteId>();
        foreach (DisplayLayer layer in layers.Values)
        {
            layer.CollectSprites(live);
        }

        surfaces?.Retain(live);
    }
}
