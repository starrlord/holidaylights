using HolidayLights.Rendering.Composition;
using Vortice.DirectComposition;

namespace HolidayLights.Rendering.Presentation;

/// <summary>
/// The DirectComposition animations created during one Lights-thread iteration. A curve object shared by many bulbs (all bulbs
/// switching on together at a step, a Dance group, Slow Glow) becomes one animation bound to every effect group; the
/// animations are released after the commit (the bindings keep what they need).
/// </summary>
internal sealed class AnimationBatch
{
    private readonly CompositionDevice device;
    private readonly Dictionary<OpacityCurve, IDCompositionAnimation> animations = new(ReferenceEqualityComparer.Instance);

    /// <summary>Creates the batch.</summary>
    public AnimationBatch(CompositionDevice device) => this.device = device;

    /// <summary>Number of animations created in this batch.</summary>
    public int Count => animations.Count;

    /// <summary>Sets an effect group's opacity: a constant directly, otherwise the curve's animation.</summary>
    public void Apply(IDCompositionEffectGroup effect, OpacityCurve curve)
    {
        if (curve.IsConstant)
        {
            effect.SetOpacity(curve.EndValue);
            return;
        }

        if (!animations.TryGetValue(curve, out IDCompositionAnimation? animation))
        {
            animation = device.CreateAnimation(curve);
            animations[curve] = animation;
        }

        effect.SetOpacity(animation);
    }

    /// <summary>Releases this iteration's animations (call after the commit); the batch is then reused.</summary>
    public void ReleaseAll()
    {
        foreach (IDCompositionAnimation animation in animations.Values)
        {
            animation.Dispose();
        }

        animations.Clear();
    }
}
