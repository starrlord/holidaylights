using HolidayLights.Rendering.Animation;
using HolidayLights.Rendering.Composition;
using HolidayLights.Rendering.Layers;
using HolidayLights.Rendering.Presentation;

namespace HolidayLights.Rendering.Engine;

/// <summary>Visibility: which displays show lights (Show Lights, resting, per-display pauses), the clock that follows them, layer moves.</summary>
internal sealed partial class LightsEngine
{
    /// <summary>Displays whose layer stays hidden because the monitor vanished or moved until the next scene arrives.</summary>
    private readonly HashSet<string> staleDisplays = new(StringComparer.Ordinal);

    /// <summary>Layers fading out before their window is replaced in a new mode (Bulb Drawing change, hot key).</summary>
    private readonly HashSet<string> movingLayers = new(StringComparer.Ordinal);

    /// <summary>Layers of displays that were disabled or disconnected, fading out before they are released.</summary>
    private readonly List<(DisplayLayer Layer, long ReleaseAt)> retiringLayers = [];

    private LightsPauseState pause = LightsPauseState.None;

    private bool AnyLayerVisible => layers.Values.Any(l => l.IsVisible);

    /// <summary>Command: apply the pause state (PRODUCT-SPEC 5.12.2).</summary>
    public void SetPause(LightsPauseState state)
    {
        long now = Now;
        PauseReasons before = pause.Global;
        pause = state;
        double fade = RestFade();
        if (Wakes(before, state.Global))
        {
            // After unlock, display on or resume: the wallpaper host can change (Lively issue), so check everything now.
            Maintain(now, topologyEvent: true);
            RetryPreferred(now, fade);
        }
        else
        {
            UpdateVisibility(now, fade, applyState: true);
        }

        ConfigureMusic();
    }

    /// <summary>True for the reasons that end with the user back at the desktop (lock, display off, absence).</summary>
    private static bool Wakes(PauseReasons before, PauseReasons after)
    {
        const PauseReasons away = PauseReasons.SessionLocked | PauseReasons.DisplayOff | PauseReasons.UserNotPresent;
        return (before & away) != 0 && (after & away) == 0;
    }

    /// <summary>
    /// Resting and resuming fade over 300 ms (PRODUCT-SPEC 4.4); hiding for a locked, dark or absent desktop is immediate
    /// because nobody sees it, and reduced motion is always immediate.
    /// </summary>
    private double RestFade()
    {
        const PauseReasons unseen = PauseReasons.SessionLocked | PauseReasons.DisplayOff | PauseReasons.UserNotPresent | PauseReasons.OwnScreenSaver;
        return Scene is { Effects.ReducedMotion: true } || (pause.Global & unseen) != 0 ? 0 : MotionTimings.RestFade;
    }

    private static double VisibilityFade(LightsScene? previous, LightsScene scene, SceneTransition transition)
    {
        if (previous is null || scene.Effects.ReducedMotion || transition == SceneTransition.None || PowerUpPlan.IsPowerUp(transition))
        {
            return 0;
        }

        return previous.LightsOn != scene.LightsOn ? MotionTimings.LightsOffFade : MotionTimings.BulbFade;
    }

    /// <summary>
    /// Creates the layers of newly enabled displays; the layers of disabled or disconnected displays fade out (150 ms) and are
    /// released.
    /// </summary>
    private void SyncLayers(LightsScene scene, long now, bool reduced)
    {
        var wanted = new Dictionary<string, DisplayInfo>(StringComparer.Ordinal);
        foreach (DisplayScene display in scene.Displays)
        {
            if (display.Enabled && scene.Layout.Placements.Any(p => p.DisplayId == display.Display.DeviceId))
            {
                wanted[display.Display.DeviceId] = display.Display;
            }
        }

        foreach (DisplayLayer layer in layers.Values.Where(l => !wanted.ContainsKey(l.DisplayId)).ToList())
        {
            layers.Remove(layer.DisplayId);
            movingLayers.Remove(layer.DisplayId);
            double fade = reduced || !layer.IsVisible ? 0 : MotionTimings.BulbFade;
            layer.FadeOutAndHide(now, fade, batch!);
            retiringLayers.Add((layer, now + Ticks(fade)));
        }

        foreach ((string id, DisplayInfo display) in wanted)
        {
            if (!layers.TryGetValue(id, out DisplayLayer? layer))
            {
                layers[id] = new DisplayLayer(device!, display);
                continue;
            }

            bool moved = layer.Display.Bounds != display.Bounds;
            layer.Display = display;
            if (moved)
            {
                layer.Window?.MoveTo(display.Bounds);
            }
        }

        ReleaseRetiredLayers(now);
    }

    /// <summary>Releases removed layers whose fade-out has ended.</summary>
    /// <returns>The next release time, or null.</returns>
    private long? ReleaseRetiredLayers(long now)
    {
        long? next = null;
        for (int i = retiringLayers.Count - 1; i >= 0; i--)
        {
            if (retiringLayers[i].ReleaseAt <= now)
            {
                retiringLayers[i].Layer.Dispose();
                retiringLayers.RemoveAt(i);
            }
            else
            {
                next = Earliest(next, retiringLayers[i].ReleaseAt);
            }
        }

        return next;
    }

    /// <summary>
    /// Shows the layers that should be visible (creating their windows) and fades out the others. Layers that come back get
    /// the animator's full state, since their visuals were not updated while hidden.
    /// </summary>
    private void UpdateVisibility(long now, double fadeMilliseconds, bool applyState)
    {
        if (batch is null)
        {
            return;
        }

        bool lightsVisible = Scene is { LightsOn: true } && !pause.IsGloballyPaused && device is not null && !stopping;
        var shown = new List<DisplayLayer>();
        foreach (DisplayLayer layer in layers.Values)
        {
            bool want = lightsVisible
                && !pause.RestingDisplayIds.Contains(layer.DisplayId)
                && !staleDisplays.Contains(layer.DisplayId)
                && !movingLayers.Contains(layer.DisplayId)
                && layer.Scene is { Bulbs.Count: > 0 };
            if (want && !layer.IsVisible && EnsureWindow(layer, now))
            {
                OpacityCurve opacity = fadeMilliseconds > 0
                    ? OpacityCurves.Ramp(now, layer.WindowOpacity.Curve.Evaluate(now), 1, fadeMilliseconds)
                    : OpacityCurve.Constant(1);
                ShowLayer(layer, opacity);
                shown.Add(layer);
            }
            else if (!want && (layer.IsVisible || (layer.Window?.IsShown ?? false)) && !layer.IsFadingOut)
            {
                layer.FadeOutAndHide(now, fadeMilliseconds, batch);
            }
        }

        RebuildRoutes();

        // The clock first: layers that come back after a rest show the step of the resumed clock.
        SyncClock(now);
        if (applyState && animator is not null && shown.Count > 0)
        {
            animator.ApplyFullState(shown.SelectMany(l => l.Scene!.Bulbs.Values).Select(b => b.Bulb.Placement.Ordinal), now, clock.TimingAt(now), router);
        }

        dirty = true;
        PublishDiagnostics();
    }

    /// <summary>The clock runs while any layer shows lights and stops when everything rests (PRODUCT-SPEC 5.12).</summary>
    private void SyncClock(long now)
    {
        bool visible = AnyLayerVisible;
        if (!visible && (clock.IsRunning || clock.StartAt is not null))
        {
            clock.Freeze(now);
        }
        else if (visible && animator is not null && clock.Resume(now))
        {
            PublishClock();
        }
    }

    /// <summary>
    /// Bulb Drawing changed: visible layers fade out (150 ms) and come back in the new mode; hidden ones just lose their window.
    /// A layer still fading out for an earlier change keeps its fade: <see cref="CompleteLayerMove"/> gives it the window of
    /// the mode requested when the fade ends, so changes in quick succession (a double-tapped hot key, Undo) end in the last one.
    /// </summary>
    private void BeginLayerMoves(long now, bool instant)
    {
        foreach (DisplayLayer layer in layers.Values)
        {
            bool moving = movingLayers.Contains(layer.DisplayId);
            if (moving && !instant)
            {
                continue;
            }

            if (layer.IsVisible && !instant)
            {
                movingLayers.Add(layer.DisplayId);
                layer.FadeOutAndHide(now, MotionTimings.LayerMoveFade, batch!);
            }
            else
            {
                // Reduced motion or no transition: the lights move at once (UpdateVisibility shows the new window), which also
                // ends a move still fading out.
                bool wasShowing = layer.IsVisible || moving;
                movingLayers.Remove(layer.DisplayId);
                layer.Detach()?.Dispose();
                if (wasShowing)
                {
                    layer.WindowOpacity.Set(OpacityCurve.Constant(1), batch!);
                }
            }
        }
    }

    /// <summary>
    /// Ends the moves whose fade-out is over, however it ended: replaces the window and fades the lights in again. A moving
    /// layer that is not fading out any more (its fade ended, or its window was taken away) must not stay in
    /// <see cref="movingLayers"/>, or it would never be shown again.
    /// </summary>
    /// <returns>True when a move ended.</returns>
    private bool CompleteLayerMoves(long now)
    {
        if (movingLayers.Count == 0)
        {
            return false;
        }

        List<DisplayLayer> ended = [.. layers.Values.Where(l => !l.IsFadingOut && movingLayers.Contains(l.DisplayId))];
        foreach (DisplayLayer layer in ended)
        {
            CompleteLayerMove(layer, now);
        }

        return ended.Count > 0;
    }

    /// <summary>The fade-out of a moving layer ended: replace its window and fade the lights in again.</summary>
    private void CompleteLayerMove(DisplayLayer layer, long now)
    {
        if (!movingLayers.Remove(layer.DisplayId))
        {
            return;
        }

        layer.Detach()?.Dispose();
        UpdateVisibility(now, MotionTimings.LayerMoveFade, applyState: true);
    }
}
