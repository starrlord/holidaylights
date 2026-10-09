using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Flash;
using HolidayLights.Core.Layout;
using HolidayLights.Core.Sprites;
using HolidayLights.Core.Themes;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Rendering.RealCore;

/// <summary>
/// The real Core engines the presenter works with in the app: the catalog with every bundled bulb, the classic layout, the
/// flash engine, the sprite provider (with a private disk cache) and the shipped themes. One instance is shared by the tests
/// of a class (<see cref="IClassFixture{TFixture}"/>); everything lives in a temporary data root.
/// </summary>
public sealed class RealLights : IDisposable
{
    private readonly TempDataRoot root = new();

    /// <summary>Starts the catalog (built-ins at once, the bundled add-ons indexed before the constructor returns).</summary>
    public RealLights()
    {
        var holding = new TestHoldingFolder(root.Paths);
        Catalog = new BulbCatalog(root.Paths, new InMemorySettingsStore(), holding, Log);
        if (!Catalog.StartAsync().Wait(TimeSpan.FromSeconds(60)))
        {
            throw new TimeoutException("The bulb catalog did not finish indexing.");
        }

        Sprites = new SpriteProvider(root.Paths, Log);
        Themes = new ThemeLibrary(root.Paths, holding, Log).ShippedOriginals;
    }

    /// <summary>The log of the Core engines.</summary>
    public RecordingLog Log { get; } = new();

    /// <summary>The catalog (resolves every built-in and bundled bulb).</summary>
    public BulbCatalog Catalog { get; }

    /// <summary>The sprite provider.</summary>
    public SpriteProvider Sprites { get; }

    /// <summary>The layout engine.</summary>
    public ILayoutEngine Layouts { get; } = new ClassicLayoutEngine();

    /// <summary>The flash engine.</summary>
    public IFlashEngine Flash { get; } = new FlashEngine();

    /// <summary>The 19 shipped themes.</summary>
    public IReadOnlyList<ThemeDefinition> Themes { get; }

    /// <summary>A shipped theme by name.</summary>
    public ThemeDefinition Theme(string name) => Themes.Single(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The scene the app's scene builder makes for a theme at the default settings (Modern Glow, Standard size, every display
    /// on, framed to its work area at its own scale), with fixed seeds so tests can rebuild the same sequencer.
    /// </summary>
    /// <param name="theme">The theme.</param>
    /// <param name="displays">The displays.</param>
    /// <param name="mode">The requested layer mode.</param>
    /// <param name="change">Changes the defaults (pattern, look, frame mode and so on).</param>
    /// <returns>The scene.</returns>
    public LightsScene Scene(ThemeDefinition theme, IReadOnlyList<DisplayInfo> displays, LayerMode mode, Func<SceneRecipe, SceneRecipe>? change = null)
    {
        var recipe = new SceneRecipe
        {
            Arrangement = theme.Arrangement ?? SlotAssignment.Classic54Default,
            Pattern = theme.Flash?.Pattern ?? FlashPatternId.FlashTogether,
            Interval = theme.Flash?.Interval ?? FlashSettings.DefaultInterval,
        };
        recipe = change?.Invoke(recipe) ?? recipe;
        return Scene(recipe, displays, mode);
    }

    /// <summary>Builds a scene from a recipe.</summary>
    public LightsScene Scene(SceneRecipe recipe, IReadOnlyList<DisplayInfo> displays, LayerMode mode)
    {
        LayoutTarget[] targets = [.. displays.Select(d => new LayoutTarget(d.DeviceId, d.WorkArea, ArtScale.Effective(d.Scale, recipe.Size)))];
        return new LightsScene
        {
            Revision = 1,
            LightsOn = recipe.LightsOn,
            RequestedLayer = mode,
            Displays = [.. displays.Select(d => new DisplayScene(d, true))],
            Layout = Layouts.Layout(targets, recipe.Arrangement, Catalog, recipe.Frame),
            Flash = new FlashOptions
            {
                Pattern = recipe.Pattern,
                SmoothFading = recipe.SmoothFading,
                LimitFlashing = recipe.LimitFlashing,
                StopFlashing = recipe.StopFlashing,
                PatternSeed = SceneRecipe.PatternSeed,
                ClassicRandomSeed = SceneRecipe.ClassicRandomSeed,
            },
            Interval = FlashClock.LimitInterval(recipe.Interval, recipe.LimitFlashing),
            Effects = new SceneEffects { Pixels = recipe.Pixels, Glow = recipe.Glow, ReducedMotion = recipe.ReducedMotion },
        };
    }

    /// <summary>Deletes the temporary data root.</summary>
    public void Dispose()
    {
        Catalog.Dispose();
        root.Dispose();
    }
}

/// <summary>The settings a test scene is built from (the defaults are the app's defaults).</summary>
public sealed record SceneRecipe
{
    /// <summary>The seed of the new patterns in every test scene.</summary>
    public const ulong PatternSeed = 0x5EED_2026_1008UL;

    /// <summary>The Random Flashing seed in every test scene.</summary>
    public const uint ClassicRandomSeed = 517;

    /// <summary>The arrangement.</summary>
    public SlotAssignment Arrangement { get; init; } = SlotAssignment.Classic54Default;

    /// <summary>The pattern.</summary>
    public FlashPatternId Pattern { get; init; } = FlashPatternId.FlashTogether;

    /// <summary>The interval (1-9).</summary>
    public int Interval { get; init; } = FlashSettings.DefaultInterval;

    /// <summary>Smooth Fading.</summary>
    public bool SmoothFading { get; init; } = true;

    /// <summary>Limit Flashing.</summary>
    public bool LimitFlashing { get; init; }

    /// <summary>Energy saver "Stop Flashing".</summary>
    public bool StopFlashing { get; init; }

    /// <summary>Show Lights.</summary>
    public bool LightsOn { get; init; } = true;

    /// <summary>Bulb Size.</summary>
    public BulbSize Size { get; init; } = BulbSize.Standard;

    /// <summary>Pixels.</summary>
    public SpriteStyle Pixels { get; init; } = SpriteStyle.Smooth;

    /// <summary>Glow.</summary>
    public GlowLevel Glow { get; init; } = GlowLevel.Soft;

    /// <summary>Reduced motion.</summary>
    public bool ReducedMotion { get; init; }

    /// <summary>Frame.</summary>
    public FrameMode Frame { get; init; } = FrameMode.EachDisplay;
}
