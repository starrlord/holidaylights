using HolidayLights.App.ScreenSaver.Rendering;
using HolidayLights.App.ScreenSaver.Simulation;

namespace HolidayLights.App.ScreenSaver.Scene;

/// <summary>The background picture of the main display: the decoded picture and where it goes (DIPs).</summary>
/// <param name="Image">The picture at its own size (one pixel per DIP).</param>
/// <param name="IsPixelArt">True for a bundled picture (scaled as pixel art; photos are resampled).</param>
/// <param name="Layout">Its rectangles on the screen.</param>
internal sealed record SaverPicture(Rgba32Image Image, bool IsPixelArt, PictureLayout Layout);

/// <summary>
/// Everything one display of the saver shows, in 5.4 drawing order (PRODUCT-SPEC 6.2.2): the background colour, the
/// picture (main display), the stamped piles, the animation, the bulbs around the whole display with the snow on them, and
/// the message (main display). Renderers draw it; the simulation moves it.
/// </summary>
internal sealed class SaverScene
{
    /// <summary>Creates the scene.</summary>
    /// <param name="plan">The display and what it shows.</param>
    /// <param name="options">The options of the run.</param>
    /// <param name="simulation">The moving parts.</param>
    /// <param name="bulbs">The bulbs, or null on a black display.</param>
    /// <param name="picture">The picture, or null.</param>
    /// <param name="message">The message, or null.</param>
    public SaverScene(
        SaverDisplayPlan plan, SaverOptions options, SaverSimulation simulation, SaverBulbs? bulbs, SaverPicture? picture, SaverMessage? message)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(simulation);
        Plan = plan;
        Options = options;
        Simulation = simulation;
        Bulbs = bulbs;
        Picture = picture;
        Message = message;
    }

    /// <summary>The display and what it shows.</summary>
    public SaverDisplayPlan Plan { get; }

    /// <summary>The options of the run.</summary>
    public SaverOptions Options { get; }

    /// <summary>The simulated screen.</summary>
    public SaverField Field => Plan.Field;

    /// <summary>The background colour (black on a display that shows nothing).</summary>
    public RgbColor Background => Plan.ShowsContent ? Options.Look.Background : RgbColor.Black;

    /// <summary>The moving parts.</summary>
    public SaverSimulation Simulation { get; }

    /// <summary>The bulbs, or null.</summary>
    public SaverBulbs? Bulbs { get; }

    /// <summary>The picture, or null.</summary>
    public SaverPicture? Picture { get; }

    /// <summary>The message, or null.</summary>
    public SaverMessage? Message { get; }

    /// <summary>How far above the bottom edge the animation stamps into the background (DIPs).</summary>
    public int StampReach => Math.Min(Field.Height, Simulation.Module?.StampReach ?? 0);
}
