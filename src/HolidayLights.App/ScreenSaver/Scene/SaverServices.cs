using HolidayLights.App.ScreenSaver.Rendering;
using HolidayLights.App.ScreenSaver.Simulation;

namespace HolidayLights.App.ScreenSaver.Scene;

/// <summary>The services the saver draws with (a narrow view of <see cref="IAppServices"/>, so tests can pass real engines and fakes).</summary>
/// <param name="Bulbs">Resolves bulbs.</param>
/// <param name="Layout">The layout engine.</param>
/// <param name="Flash">The flash engine.</param>
/// <param name="BulbSprites">Bulb sprites and glow.</param>
/// <param name="Compositor">The CPU compositor.</param>
/// <param name="Pictures">The background pictures.</param>
/// <param name="Log">The log.</param>
internal sealed record SaverServices(
    IBulbResolver Bulbs, ILayoutEngine Layout, IFlashEngine Flash, ISpriteProvider BulbSprites, ICpuCompositor Compositor, IPictureLibrary Pictures,
    IAppLog Log)
{
    /// <summary>The saver's view of the application services.</summary>
    /// <param name="services">The application services.</param>
    /// <returns>The saver services.</returns>
    public static SaverServices From(IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new SaverServices(
            services.Bulbs, services.Layout, services.Flash, services.Sprites, services.Compositor, services.Pictures, services.Log);
    }

    /// <summary>
    /// Builds the scenes of a run (5.4 <c>Saver_Start</c> per display): each display draws from its own random stream and,
    /// apart from the main display, seeds its random flash patterns with its own ordinal (<see cref="SaverOptions.ForDisplay"/>);
    /// only the main display gets the picture and the message. Uses WPF text: call from an STA thread.
    /// </summary>
    /// <param name="plans">The displays.</param>
    /// <param name="options">The options of the run.</param>
    /// <param name="picture">The decoded picture, or null.</param>
    /// <param name="seed">The run's random seed.</param>
    /// <param name="startTimestamp">When the run starts.</param>
    /// <returns>One scene per plan, in order.</returns>
    public IReadOnlyList<SaverScene> CreateScenes(
        IReadOnlyList<SaverDisplayPlan> plans, SaverOptions options, DecodedPicture? picture, uint seed, long startTimestamp)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(options);
        var factory = new SaverSceneFactory(Bulbs, Layout, Flash);
        int[] ordinals = DisplayOrdinals(plans);
        return [.. plans.Select((plan, i) => factory.Create(
            plan, options.ForDisplay(ordinals[i]), plan.IsMain ? picture : null, new CrtSaverRandom(unchecked(seed + (uint)i)), startTimestamp))];
    }

    /// <summary>The CPU renderer of a scene at a zoom (Settings preview, <c>/p</c>, frame dumps). STA thread.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="zoom">Target pixels per physical display pixel.</param>
    /// <param name="sprites">The run's sprite cache.</param>
    /// <param name="viewport">The part of the zoomed display to draw (target pixels), or null for the whole display.</param>
    /// <returns>The renderer.</returns>
    public CpuSaverRenderer CreateRenderer(SaverScene scene, double zoom, SaverSpriteCache sprites, RectI? viewport = null) =>
        new(scene, zoom, Compositor, BulbSprites, Bulbs, sprites, viewport);

    /// <summary>The ordinal of each plan for <see cref="SaverOptions.ForDisplay"/>: 0 for the main display, then 1, 2, ... in order.</summary>
    private static int[] DisplayOrdinals(IReadOnlyList<SaverDisplayPlan> plans)
    {
        int main = 0;
        for (int i = 0; i < plans.Count; i++)
        {
            if (plans[i].IsMain)
            {
                main = i;
                break;
            }
        }

        var ordinals = new int[plans.Count];
        int next = 1;
        for (int i = 0; i < plans.Count; i++)
        {
            ordinals[i] = i == main ? 0 : next++;
        }

        return ordinals;
    }
}
