using System.Diagnostics;

namespace HolidayLights.App.Shell;

/// <summary>What a scene is built from.</summary>
/// <param name="Settings">The settings.</param>
/// <param name="Displays">The connected displays (main display first).</param>
/// <param name="EnergySaverOn">Windows Energy Saver is on.</param>
/// <param name="AnimationsEnabled">Windows "Animation effects" (false = reduced motion).</param>
public sealed record SceneInputs(AppSettings Settings, IReadOnlyList<DisplayInfo> Displays, bool EnergySaverOn, bool AnimationsEnabled);

/// <summary>The random seeds of a flash pattern run (PRODUCT-SPEC 5.6, 5.7).</summary>
/// <param name="Pattern">Seed of the new patterns' <c>u(i, s)</c>.</param>
/// <param name="ClassicRandom">The MSVC <c>srand</c> seed of Random Flashing (5.4 used the millisecond of the clock).</param>
public readonly record struct SceneSeeds(ulong Pattern, uint ClassicRandom)
{
    /// <summary>Seeds taken from the clock, as 5.4 did.</summary>
    /// <returns>New seeds.</returns>
    public static SceneSeeds FromClock() => new((ulong)Stopwatch.GetTimestamp(), (uint)DateTime.Now.Millisecond);
}

/// <summary>
/// Builds the immutable <see cref="LightsScene"/> the desktop shows (CONTRACTS 6.2) from settings, displays and the
/// Energy Saver state, on the UI thread. The layout is computed again only when its inputs change (enabled displays,
/// work areas, scales, arrangement, frame mode) or when asked to (a bulb in use changed); new seeds are drawn when the
/// layout or the pattern changes, so Random Flashing re-rolls on every rebuild (5.4). A build that changes nothing
/// returns the previous scene instance.
/// </summary>
public sealed class SceneBuilder
{
    private readonly ILayoutEngine layoutEngine;
    private readonly IBulbResolver bulbs;
    private readonly Func<SceneSeeds> newSeeds;
    private LayoutKey? layoutKey;
    private LightsLayout layout = LightsLayout.Empty;
    private SceneSeeds seeds;
    private FlashPatternId? seededPattern;
    private long revision;

    /// <summary>Creates the builder.</summary>
    /// <param name="layoutEngine">The layout engine.</param>
    /// <param name="bulbs">Resolves the bulbs of the arrangement.</param>
    public SceneBuilder(ILayoutEngine layoutEngine, IBulbResolver bulbs)
        : this(layoutEngine, bulbs, SceneSeeds.FromClock)
    {
    }

    /// <summary>Creates the builder with a seed source (tests).</summary>
    /// <param name="layoutEngine">The layout engine.</param>
    /// <param name="bulbs">Resolves the bulbs of the arrangement.</param>
    /// <param name="newSeeds">Draws the seeds of a new pattern run.</param>
    internal SceneBuilder(ILayoutEngine layoutEngine, IBulbResolver bulbs, Func<SceneSeeds> newSeeds)
    {
        ArgumentNullException.ThrowIfNull(layoutEngine);
        ArgumentNullException.ThrowIfNull(bulbs);
        ArgumentNullException.ThrowIfNull(newSeeds);
        this.layoutEngine = layoutEngine;
        this.bulbs = bulbs;
        this.newSeeds = newSeeds;
    }

    /// <summary>The last scene built (<see cref="LightsScene.Empty"/> before the first build).</summary>
    public LightsScene Current { get; private set; } = LightsScene.Empty;

    /// <summary>Builds the scene for the inputs.</summary>
    /// <param name="inputs">Settings, displays and system state.</param>
    /// <param name="relayout">Lay out again even when the layout inputs are unchanged (a bulb in use changed).</param>
    /// <returns>The new scene, or <see cref="Current"/> when nothing changed.</returns>
    public LightsScene Build(SceneInputs inputs, bool relayout = false)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        AppSettings settings = inputs.Settings;
        IReadOnlyList<DisplayScene> displays = SceneRules.SelectDisplays(inputs.Displays, settings.Lights.Displays.Disabled);
        var key = new LayoutKey(SceneRules.Targets(displays, settings.Lights.Size), settings.Current.Arrangement, settings.Lights.FrameMode);
        bool layoutChanged = relayout || layoutKey is null || !layoutKey.Equals(key);
        if (layoutChanged)
        {
            layout = layoutEngine.Layout(key.Targets, key.Arrangement, bulbs, key.Mode);
            layoutKey = key;
        }

        FlashOptions options = SceneRules.FlashOptions(settings, inputs.EnergySaverOn);
        if (layoutChanged || seededPattern != options.Pattern)
        {
            seeds = newSeeds();
            seededPattern = options.Pattern;
        }

        var candidate = new LightsScene
        {
            Revision = revision + 1,
            LightsOn = SceneRules.LightsOn(settings, inputs.EnergySaverOn),
            RequestedLayer = LayerModes.FromSettings(settings.Lights.Drawing, settings.Lights.BehindIcons),
            Displays = displays,
            Layout = layout,
            Flash = options with { PatternSeed = seeds.Pattern, ClassicRandomSeed = seeds.ClassicRandom },
            Interval = SceneRules.EffectiveInterval(settings, inputs.EnergySaverOn),
            Effects = SceneRules.Effects(settings, inputs.EnergySaverOn, inputs.AnimationsEnabled),
            EnergySaverInEffect = SceneRules.EnergySaverInEffect(settings, inputs.EnergySaverOn),
        };

        if (IsEquivalent(Current, candidate))
        {
            return Current;
        }

        revision++;
        Current = candidate;
        return candidate;
    }

    /// <summary>True when two scenes show the same thing (everything but the revision).</summary>
    /// <param name="a">A scene.</param>
    /// <param name="b">Another scene.</param>
    /// <returns>True when equivalent.</returns>
    public static bool IsEquivalent(LightsScene a, LightsScene b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a.LightsOn == b.LightsOn
            && a.RequestedLayer == b.RequestedLayer
            && ReferenceEquals(a.Layout, b.Layout)
            && a.Flash == b.Flash
            && a.Interval == b.Interval
            && a.Effects == b.Effects
            && a.EnergySaverInEffect == b.EnergySaverInEffect
            && a.Displays.SequenceEqual(b.Displays);
    }

    /// <summary>The inputs of the layout engine; equal keys give the same layout.</summary>
    private sealed record LayoutKey(IReadOnlyList<LayoutTarget> Targets, SlotAssignment Arrangement, FrameMode Mode)
    {
        public bool Equals(LayoutKey? other) =>
            other is not null && Mode == other.Mode && Arrangement.Equals(other.Arrangement) && Targets.SequenceEqual(other.Targets);

        public override int GetHashCode() => HashCode.Combine(Mode, Arrangement, Targets.Count);
    }
}
