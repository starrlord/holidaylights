namespace HolidayLights.App.ScreenSaver.Simulation;

/// <summary>
/// One picture that floats over the saver (5.4 <c>ScreenSaverFloater</c>): a built-in
/// bulb animation, a GIF or an add-on bulb, moved by the chosen style.
/// </summary>
internal sealed class Floater
{
    /// <summary>Creates floater <paramref name="index"/>; it starts out of phase with the others (frame = index mod frames).</summary>
    /// <param name="art">The picture.</param>
    /// <param name="index">0-15.</param>
    public Floater(SpriteArt art, int index)
    {
        ArgumentNullException.ThrowIfNull(art);
        Art = art;
        Frame = index % art.FrameCount;
    }

    /// <summary>The picture.</summary>
    public SpriteArt Art { get; }

    /// <summary>The current frame.</summary>
    public int Frame { get; set; }

    /// <summary>Steps since creation (never reset; Falling Leaves piles use it).</summary>
    public int Age { get; set; }

    /// <summary>Left in DIPs.</summary>
    public float X { get; set; }

    /// <summary>Top in DIPs.</summary>
    public float Y { get; set; }

    /// <summary>Horizontal speed.</summary>
    public float Vx { get; set; }

    /// <summary>Vertical speed (Bounce Off Sides, Attraction).</summary>
    public float Vy { get; set; }

    /// <summary>Gravity Well: the vertical speed; Falling Leaves: the horizontal acceleration.</summary>
    public float Aux { get; set; }

    /// <summary>Left before the last step.</summary>
    public float PreviousX { get; set; }

    /// <summary>Top before the last step.</summary>
    public float PreviousY { get; set; }

    /// <summary>Sprite width (frame 0).</summary>
    public int Width => Art.Size.Width;

    /// <summary>Sprite height (frame 0).</summary>
    public int Height => Art.Size.Height;

    /// <summary>Remembers the position before a step.</summary>
    public void BeginStep()
    {
        PreviousX = X;
        PreviousY = Y;
    }

    /// <summary>The floater jumped (respawned or wrapped): no interpolation across the jump.</summary>
    public void Teleported()
    {
        PreviousX = X;
        PreviousY = Y;
    }

    /// <summary>The sprite as drawn now.</summary>
    /// <returns>The draw item.</returns>
    public SpriteDraw ToDraw() => new(Art, Frame, X, Y, PreviousX, PreviousY);

    /// <summary>The sprite as stamped into the background now.</summary>
    /// <returns>The stamp.</returns>
    public SpriteStamp ToStamp() => new(Art, Frame, X, Y);
}
