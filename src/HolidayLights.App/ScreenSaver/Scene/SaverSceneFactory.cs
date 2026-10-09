using HolidayLights.App.ScreenSaver.Rendering;
using HolidayLights.App.ScreenSaver.Simulation;

namespace HolidayLights.App.ScreenSaver.Scene;

/// <summary>
/// Builds the scene of one display (5.4 <c>Saver_Start</c>): the bulbs, the module (drawing its start positions from the
/// random stream), the picture layout and the message layout. The message layout uses WPF text: call from an STA thread.
/// </summary>
internal sealed class SaverSceneFactory
{
    private readonly IBulbResolver bulbs;
    private readonly ILayoutEngine layout;
    private readonly IFlashEngine flash;

    /// <summary>Creates the factory.</summary>
    /// <param name="bulbs">Resolves bulbs (arrangement and add-on bulb animations).</param>
    /// <param name="layout">The layout engine.</param>
    /// <param name="flash">The flash engine.</param>
    public SaverSceneFactory(IBulbResolver bulbs, ILayoutEngine layout, IFlashEngine flash)
    {
        ArgumentNullException.ThrowIfNull(bulbs);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(flash);
        this.bulbs = bulbs;
        this.layout = layout;
        this.flash = flash;
    }

    /// <summary>Builds a scene.</summary>
    /// <param name="plan">The display and what it shows.</param>
    /// <param name="options">The options of the run.</param>
    /// <param name="picture">The decoded background picture (main display), or null for none or a missing file.</param>
    /// <param name="random">The random stream of this display's simulation.</param>
    /// <param name="startTimestamp">When the bulbs' step 0 begins.</param>
    /// <returns>The scene.</returns>
    public SaverScene Create(SaverDisplayPlan plan, SaverOptions options, DecodedPicture? picture, ISaverRandom random, long startTimestamp)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(random);
        SaverField field = plan.Field;
        if (!plan.ShowsContent)
        {
            return new SaverScene(plan, options, new SaverSimulation(null, null, options.Interval, null), null, null, null);
        }

        SaverBulbs saverBulbs = SaverBulbs.Create(plan.Display, options, bulbs, layout, flash, startTimestamp);
        SaverLook look = options.Look;
        SaverModule? module = SaverModules.Create(look.Animation, look.Style, field, random, bulbs);
        ISnowCatcher? snow = module is SnowModule ? BulbSnowCatcher.Create(saverBulbs.Layout, plan.Display, field, bulbs) : null;

        SaverPicture? saverPicture = null;
        SaverMessage? message = null;
        if (plan.IsMain)
        {
            if (picture is not null)
            {
                saverPicture = new SaverPicture(
                    picture.Image, picture.IsPixelArt, SaverLayoutRules.PlacePicture(field, picture.Image.Size, look.Placement));
            }

            message = SaverText.Layout(look, field, saverPicture?.Layout.Centered);
        }

        TextBounce? bounce = message is null ? null : new TextBounce(message.Range, message.TextHeight);
        var simulation = new SaverSimulation(module, bounce, options.Interval, snow);
        return new SaverScene(plan, options, simulation, saverBulbs, saverPicture, message);
    }
}

/// <summary>A decoded background picture.</summary>
/// <param name="Image">The picture at its own size.</param>
/// <param name="IsPixelArt">True for a bundled picture.</param>
internal sealed record DecodedPicture(Rgba32Image Image, bool IsPixelArt)
{
    /// <summary>Decodes the picture of the saver look (on any thread).</summary>
    /// <param name="pictures">The picture library.</param>
    /// <param name="pictureId">The picture id, or "(None)".</param>
    /// <returns>The picture, or null for "(None)" or a missing or undecodable file (the saver then runs without it, 5.4).</returns>
    public static DecodedPicture? Load(IPictureLibrary pictures, string pictureId)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        if (string.IsNullOrEmpty(pictureId) || pictureId == SaverPictures.None || pictures.LoadImage(pictureId) is not { } image)
        {
            return null;
        }

        bool bundled = MediaIds.TryParse(pictureId, out MediaOrigin origin, out _) && origin == MediaOrigin.Bundled;
        return new DecodedPicture(image, bundled);
    }
}
