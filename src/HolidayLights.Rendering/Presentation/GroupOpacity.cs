using System.Diagnostics;
using HolidayLights.Rendering.Composition;
using Vortice.DirectComposition;

namespace HolidayLights.Rendering.Presentation;

/// <summary>
/// The opacity of a visual with children (a display's window root, a scene root, a bulb container). The effect group is
/// attached only while the opacity is not a constant 1, and a finished fade is replaced by its end value
/// (<see cref="SettleIfDue"/>), so a settled subtree is composed directly instead of through an intermediate surface.
/// </summary>
internal sealed class GroupOpacity : IDisposable
{
    private readonly CompositionDevice device;
    private readonly IDCompositionVisual visual;
    private IDCompositionEffectGroup? effect;

    /// <summary>Wraps a visual (initially fully opaque).</summary>
    public GroupOpacity(CompositionDevice device, IDCompositionVisual visual)
    {
        this.device = device;
        this.visual = visual;
    }

    /// <summary>The curve last applied.</summary>
    public OpacityCurve Curve { get; private set; } = OpacityCurve.Constant(1);

    /// <summary>When the current fade ends and should be settled, or null (constant or repeating).</summary>
    public long? SettleAt { get; private set; }

    /// <summary>Applies a curve.</summary>
    public void Set(OpacityCurve curve, AnimationBatch batch)
    {
        Curve = curve;
        SettleAt = curve.IsConstant || curve.Repeats
            ? null
            : curve.BeginTimestamp + (long)Math.Ceiling(curve.TerminalOffsetSeconds * Stopwatch.Frequency);
        if (curve.IsConstant && curve.EndValue >= 1f)
        {
            Detach();
            return;
        }

        if (effect is null)
        {
            effect = device.CreateEffectGroup();
            visual.SetEffect(effect);
        }

        batch.Apply(effect, curve);
    }

    /// <summary>Replaces a finished fade by its end value (detaching the effect at 1).</summary>
    /// <returns>True when something changed.</returns>
    public bool SettleIfDue(long now, AnimationBatch batch)
    {
        if (SettleAt is not { } at || now < at)
        {
            return false;
        }

        Set(OpacityCurve.Constant(Curve.EndValue), batch);
        return true;
    }

    /// <summary>Detaches and releases the effect group.</summary>
    public void Dispose() => Detach();

    private void Detach()
    {
        if (effect is not null)
        {
            visual.SetEffect(null!);
            effect.Dispose();
            effect = null;
        }
    }
}
