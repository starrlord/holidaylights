using System.Diagnostics;
using HolidayLights.Rendering.Composition;
using HolidayLights.Rendering.Layers;
using HolidayLights.Rendering.Scenes;
using Vortice.DirectComposition;

namespace HolidayLights.Rendering.Presentation;

/// <summary>
/// Everything the lights show on one display: the layer window (in one of the three modes, replaceable), a window root with
/// the layer-wide opacity (lights off, resting, layer moves), the current scene's bulbs and any older scenes still fading out.
/// Lights thread only.
/// </summary>
internal sealed class DisplayLayer : IDisposable
{
    private readonly List<(SceneVisuals Scene, long ReleaseAt)> retiring = [];
    private long? hideAt;

    /// <summary>Creates the layer of a display (no window yet).</summary>
    public DisplayLayer(CompositionDevice device, DisplayInfo display)
    {
        Display = display;
        WindowRoot = device.CreateVisual();
        WindowOpacity = new GroupOpacity(device, WindowRoot);
    }

    /// <summary>The display (bounds and DPI from the latest scene).</summary>
    public DisplayInfo Display { get; set; }

    /// <summary>The display id.</summary>
    public string DisplayId => Display.DeviceId;

    /// <summary>The window, or null while none could be created.</summary>
    public LayerWindow? Window { get; private set; }

    /// <summary>The root of the window's visual tree.</summary>
    public IDCompositionVisual WindowRoot { get; }

    /// <summary>The layer-wide opacity.</summary>
    public GroupOpacity WindowOpacity { get; }

    /// <summary>The bulbs of the current scene.</summary>
    public SceneVisuals? Scene { get; private set; }

    /// <summary>True while the window is shown and not fading out.</summary>
    public bool IsVisible => Window is { IsShown: true } && hideAt is null;

    /// <summary>True while a fade-out will hide the window.</summary>
    public bool IsFadingOut => hideAt is not null;

    /// <summary>The window's top-left in virtual-screen pixels (visual offsets are relative to it).</summary>
    public PointI Origin => Display.Bounds.TopLeft;

    /// <summary>Visuals in use (diagnostics).</summary>
    public int VisualCount => 1 + (Scene?.VisualCount ?? 0) + retiring.Sum(r => r.Scene.VisualCount);

    /// <summary>Puts the visual tree into a window.</summary>
    /// <returns>False when the window refused the tree.</returns>
    public bool Attach(LayerWindow window)
    {
        Window = window;
        return window.SetRoot(WindowRoot);
    }

    /// <summary>Takes the window away from the layer (the caller disposes it).</summary>
    public LayerWindow? Detach()
    {
        LayerWindow? window = Window;
        Window = null;
        hideAt = null;
        window?.SetRoot(null);
        return window;
    }

    /// <summary>The factor on the glow of this display (half while the display runs in HDR).</summary>
    public float GlowScale { get; private set; } = 1f;

    /// <summary>Makes <paramref name="scene"/> the current scene; the previous one is kept until <paramref name="releaseAt"/> (it may be fading out).</summary>
    public void ReplaceScene(SceneVisuals scene, long releaseAt, AnimationBatch batch)
    {
        if (Scene is not null)
        {
            retiring.Add((Scene, releaseAt));
        }

        Scene = scene;
        scene.SetGlowScale(GlowScale, batch);
        WindowRoot.AddInFront(scene.Root);
    }

    /// <summary>Scales the glow of every scene on this display.</summary>
    public void SetGlowScale(float scale, AnimationBatch batch)
    {
        GlowScale = scale;
        Scene?.SetGlowScale(scale, batch);
        foreach ((SceneVisuals scene, _) in retiring)
        {
            scene.SetGlowScale(scale, batch);
        }
    }

    /// <summary>Shows the window with its opacity at <paramref name="opacity"/> and cancels a pending hide.</summary>
    public void Show(nint insertAfter, OpacityCurve opacity, AnimationBatch batch)
    {
        hideAt = null;
        WindowOpacity.Set(opacity, batch);
        if (Window is { IsShown: false } window)
        {
            window.Show(insertAfter);
        }
    }

    /// <summary>Fades the layer out and hides the window at the end (immediately for 0 ms).</summary>
    public void FadeOutAndHide(long now, double milliseconds, AnimationBatch batch)
    {
        if (Window is not { IsShown: true })
        {
            return;
        }

        if (milliseconds <= 0)
        {
            WindowOpacity.Set(OpacityCurve.Constant(0), batch);
            Window.Hide();
            hideAt = null;
            return;
        }

        if (hideAt is null)
        {
            WindowOpacity.Set(OpacityCurves.Ramp(now, WindowOpacity.Curve.Evaluate(now), 0, milliseconds), batch);
            hideAt = now + (long)(milliseconds * Stopwatch.Frequency / 1000);
        }
    }

    /// <summary>Completes due work: hides after a fade-out, settles finished fades, releases faded scenes and bulbs.</summary>
    /// <param name="now">The current timestamp.</param>
    /// <param name="batch">The animation batch.</param>
    /// <param name="changed">Set when something changed that needs a commit.</param>
    /// <returns>The next time this layer needs a wake-up, or null.</returns>
    public long? Tick(long now, AnimationBatch batch, ref bool changed)
    {
        long? next = null;
        if (hideAt is { } hide)
        {
            if (hide <= now)
            {
                hideAt = null;
                Window?.Hide();
                WindowOpacity.Set(OpacityCurve.Constant(0), batch);
                changed = true;
            }
            else
            {
                next = hide;
            }
        }
        else
        {
            changed |= WindowOpacity.SettleIfDue(now, batch);
            next = Min(next, WindowOpacity.SettleAt);
        }

        if (Scene is not null)
        {
            next = Min(next, Scene.Tick(now, batch, ref changed));
        }

        for (int i = retiring.Count - 1; i >= 0; i--)
        {
            if (retiring[i].ReleaseAt <= now)
            {
                WindowRoot.RemoveVisual(retiring[i].Scene.Root);
                retiring[i].Scene.Dispose();
                retiring.RemoveAt(i);
                changed = true;
            }
            else
            {
                next = Min(next, retiring[i].ReleaseAt);
            }
        }

        return next;
    }

    /// <summary>The sprites of every scene still shown (current and fading out).</summary>
    public void CollectSprites(HashSet<SpriteId> live)
    {
        Scene?.CollectSprites(live);
        foreach ((SceneVisuals scene, _) in retiring)
        {
            scene.CollectSprites(live);
        }
    }

    /// <summary>Releases the visuals; the window is detached and destroyed.</summary>
    public void Dispose()
    {
        Detach()?.Dispose();
        foreach ((SceneVisuals scene, _) in retiring)
        {
            scene.Dispose();
        }

        retiring.Clear();
        Scene?.Dispose();
        Scene = null;
        WindowOpacity.Dispose();
        WindowRoot.RemoveAllVisuals();
        WindowRoot.Dispose();
    }

    private static long? Min(long? a, long? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);
}
