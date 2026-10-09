namespace HolidayLights.App.ScreenSaver.Simulation;

/// <summary>Where the background picture is drawn: one rectangle, or the tiles (DIPs; rectangles may reach past the screen).</summary>
/// <param name="Placement">The placement.</param>
/// <param name="Rectangles">The rectangles the picture is drawn into, in drawing order.</param>
internal sealed record PictureLayout(PicturePlacement Placement, IReadOnlyList<RectI> Rectangles)
{
    /// <summary>The rectangle of a centred picture (the message may sit below it), else null.</summary>
    public RectI? Centered => Placement == PicturePlacement.Center ? Rectangles[0] : null;
}

/// <summary>
/// The 5.4 placement rules of the background picture and the message, on the
/// simulated screen in DIPs. Integer arithmetic truncates like the original C.
/// </summary>
internal static class SaverLayoutRules
{
    /// <summary>
    /// Places the picture: Center (x centred; y at a third of the free height when the picture is shorter than the screen,
    /// centred otherwise), Tile (from the top-left until the screen is covered), Stretch (the whole screen) or Fit (the
    /// largest undistorted size, centred).
    /// </summary>
    /// <param name="field">The screen.</param>
    /// <param name="picture">The picture size in DIPs (one picture pixel per DIP).</param>
    /// <param name="placement">The placement.</param>
    /// <returns>The layout; empty for an empty picture or screen.</returns>
    public static PictureLayout PlacePicture(SaverField field, SizeI picture, PicturePlacement placement)
    {
        if (picture.IsEmpty || field.Width <= 0 || field.Height <= 0)
        {
            return new PictureLayout(placement, []);
        }

        return placement switch
        {
            PicturePlacement.Tile => new PictureLayout(placement, Tiles(field, picture)),
            PicturePlacement.Stretch => new PictureLayout(placement, [new RectI(0, 0, field.Width, field.Height)]),
            PicturePlacement.Fit => new PictureLayout(placement, [Fit(field, picture)]),
            _ => new PictureLayout(PicturePlacement.Center, [Center(field, picture)]),
        };
    }

    /// <summary>
    /// The message range: between W / 8 margins horizontally; vertically below a centred picture when the text fits in
    /// seven tenths of the space under it (gaps of a tenth of that space), otherwise between H / 8 margins.
    /// </summary>
    /// <param name="field">The screen.</param>
    /// <param name="textHeight">The height of the wrapped message in DIPs.</param>
    /// <param name="centeredPicture">The rectangle of a centred picture, or null.</param>
    /// <returns>The range.</returns>
    public static TextRange MessageRange(SaverField field, int textHeight, RectI? centeredPicture)
    {
        int side = field.Width / 8;
        int left = side;
        int right = field.Width - side;
        if (centeredPicture is { } picture)
        {
            int below = field.Height - picture.Bottom;
            if (textHeight * 10 < below * 7)
            {
                int gap = below / 10;
                return new TextRange(left, right, picture.Bottom + gap, field.Height - 2 * gap);
            }
        }

        int margin = field.Height / 8;
        return new TextRange(left, right, margin, field.Height - margin);
    }

    private static RectI Center(SaverField field, SizeI picture)
    {
        int free = field.Height - picture.Height;
        int y = free / (free > 0 ? 3 : 2);
        int x = (field.Width - picture.Width) / 2;
        return RectI.FromXYWH(x, y, picture.Width, picture.Height);
    }

    private static RectI Fit(SaverField field, SizeI picture)
    {
        double scale = Math.Min((double)field.Width / picture.Width, (double)field.Height / picture.Height);
        int width = Math.Max(1, (int)Math.Round(picture.Width * scale, MidpointRounding.AwayFromZero));
        int height = Math.Max(1, (int)Math.Round(picture.Height * scale, MidpointRounding.AwayFromZero));
        return RectI.FromXYWH((field.Width - width) / 2, (field.Height - height) / 2, width, height);
    }

    private static RectI[] Tiles(SaverField field, SizeI picture)
    {
        var tiles = new List<RectI>();
        for (int y = 0; y < field.Height; y += picture.Height)
        {
            for (int x = 0; x < field.Width; x += picture.Width)
            {
                tiles.Add(RectI.FromXYWH(x, y, picture.Width, picture.Height));
            }
        }

        return [.. tiles];
    }
}
