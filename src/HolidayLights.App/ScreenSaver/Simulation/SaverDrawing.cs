namespace HolidayLights.App.ScreenSaver.Simulation;

/// <summary>The simulated screen in DIPs: 5.4's drawing rectangle with left = top = 0 (a 4K display at 150 % is 2560 x 1440).</summary>
/// <param name="Width">W.</param>
/// <param name="Height">H.</param>
internal readonly record struct SaverField(int Width, int Height);

/// <summary>
/// An animation strip in art pixels (one art pixel is one DIP): what a floater, balloon or flake shows. Instances are
/// shared and immutable; renderers cache their scaled frames by reference.
/// </summary>
internal sealed class SpriteArt
{
    /// <summary>Creates the strip.</summary>
    /// <param name="key">A stable name for logs and tests (for example <c>gif:3104</c>).</param>
    /// <param name="frames">The frames, straight alpha; frame 0 sets the sprite rectangle.</param>
    /// <exception cref="ArgumentException">There are no frames.</exception>
    public SpriteArt(string key, IReadOnlyList<Rgba32Image> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            throw new ArgumentException("A sprite needs at least one frame.", nameof(frames));
        }

        Key = key;
        Frames = frames;
    }

    /// <summary>A stable name for logs and tests.</summary>
    public string Key { get; }

    /// <summary>The frames (straight alpha). Callers must not modify them.</summary>
    public IReadOnlyList<Rgba32Image> Frames { get; }

    /// <summary>Number of frames.</summary>
    public int FrameCount => Frames.Count;

    /// <summary>The sprite rectangle: the size of frame 0 (5.4 sized floaters from their first cell or GIF frame).</summary>
    public SizeI Size => Frames[0].Size;

    /// <inheritdoc />
    public override string ToString() => Key;
}

/// <summary>One sprite drawn in a step, and where it was one step earlier (Smooth Motion interpolates between the two).</summary>
/// <param name="Art">The strip.</param>
/// <param name="Frame">The frame.</param>
/// <param name="X">Left in DIPs after the step.</param>
/// <param name="Y">Top in DIPs after the step.</param>
/// <param name="PreviousX">Left before the step (equal to <paramref name="X"/> after a jump such as a wrap or respawn).</param>
/// <param name="PreviousY">Top before the step.</param>
internal readonly record struct SpriteDraw(SpriteArt Art, int Frame, float X, float Y, float PreviousX, float PreviousY);

/// <summary>A sprite drawn into the background for good: snow and leaf piles, Gravity Well objects at rest (5.4).</summary>
/// <param name="Art">The strip.</param>
/// <param name="Frame">The frame.</param>
/// <param name="X">Left in DIPs.</param>
/// <param name="Y">Top in DIPs.</param>
internal readonly record struct SpriteStamp(SpriteArt Art, int Frame, float X, float Y);

/// <summary>A pixel of snow that stuck to a bulb (5.4 <c>AddSnowPixel</c>), one DIP square.</summary>
/// <param name="X">Column in DIPs.</param>
/// <param name="Y">Row in DIPs.</param>
/// <param name="Color">Packed BGRA colour (white, or grey on the black outline of the art).</param>
internal readonly record struct SnowCell(int X, int Y, uint Color);

/// <summary>Where falling snow can stick to the bulbs around the saver (5.4 <c>BulbManager_HitTest</c> and <c>AddSnowPixel</c>).</summary>
internal interface ISnowCatcher
{
    /// <summary>Tests a point against frame 0 of every bulb.</summary>
    /// <param name="x">Column in DIPs.</param>
    /// <param name="y">Row in DIPs.</param>
    /// <param name="color">The snow colour there when the point is on a bulb.</param>
    /// <returns>True when the point is an opaque pixel of a bulb.</returns>
    bool TryCatch(int x, int y, out uint color);
}

/// <summary>What one simulation step drew and changed (5.4's WORK and BACKGROUND device contexts).</summary>
internal sealed class SaverStepContext
{
    /// <summary>Creates the context.</summary>
    /// <param name="bulbs">Where snow sticks, or null when no bulbs are shown.</param>
    public SaverStepContext(ISnowCatcher? bulbs) => Bulbs = bulbs;

    /// <summary>True when sprites advance a frame in this step (every Flash Interval steps).</summary>
    public bool FrameStep { get; set; }

    /// <summary>Where snow sticks, or null.</summary>
    public ISnowCatcher? Bulbs { get; }

    /// <summary>Sprites drawn into the frame in this step, in drawing order.</summary>
    public List<SpriteDraw> Sprites { get; } = [];

    /// <summary>Sprites drawn into the background in this step.</summary>
    public List<SpriteStamp> Stamps { get; } = [];

    /// <summary>Snow pixels that stuck to bulbs in this step.</summary>
    public List<SnowCell> SnowCells { get; } = [];

    /// <summary>Forgets what the previous step drew.</summary>
    public void Clear()
    {
        Sprites.Clear();
        Stamps.Clear();
        SnowCells.Clear();
    }
}
