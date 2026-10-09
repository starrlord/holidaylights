namespace HolidayLights.App.ScreenSaver.Simulation;

/// <summary>
/// The 21 picture animations and add-on bulbs (5.4 <c>GenericModule</c>): up to 16
/// floaters moved by the chosen style.
/// </summary>
internal sealed class FloaterModule : SaverModule
{
    /// <summary>Most floaters of one animation.</summary>
    public const int MaxFloaters = 16;

    /// <summary>Falling Leaves: the shared wind is limited to this speed.</summary>
    public const float MaxWind = 20f;

    /// <summary>A leaf can fall a little further than the pile in its landing step; stamps never reach above this margin.</summary>
    private const int LeafLandingMargin = 32;

    private readonly FloaterMotion motion;
    private readonly List<Floater> floaters = [];
    private float wind;

    /// <summary>
    /// Creates floater i from <c>arts[i mod count]</c> for i = 0..15, each respawned at once; creation stops after the first
    /// floater that makes the sprites cover more than an eighth of the screen (5.4).
    /// </summary>
    /// <param name="field">The simulated screen.</param>
    /// <param name="random">The random stream.</param>
    /// <param name="style">The movement style.</param>
    /// <param name="arts">
    /// The pictures in 5.4 order: the table records of a built-in animation, the single GIF strip of a GIF animation, or the
    /// used entries of an add-on bulb.
    /// </param>
    public FloaterModule(SaverField field, ISaverRandom random, SaverMovementStyle style, IReadOnlyList<SpriteArt> arts)
        : base(field, random)
    {
        ArgumentNullException.ThrowIfNull(arts);
        motion = new FloaterMotion(style, field, random);
        long screen = (long)field.Width * field.Height;
        long covered = 0;
        for (int i = 0; i < MaxFloaters && arts.Count > 0; i++)
        {
            var floater = new Floater(arts[i % arts.Count], i);
            motion.Respawn(floater);
            floaters.Add(floater);
            covered += (long)floater.Width * floater.Height;
            if (covered * 8 > screen)
            {
                break;
            }
        }
    }

    /// <inheritdoc />
    public override int StampReach => motion.Style switch
    {
        SaverMovementStyle.GravityWell => TallestFloater,
        SaverMovementStyle.FallingLeaves => FloaterMotion.MaxLeafPileHeight + TallestFloater + LeafLandingMargin,
        _ => 0,
    };

    /// <summary>The floaters (for tests and diagnostics).</summary>
    public IReadOnlyList<Floater> Floaters => floaters;

    /// <summary>The Falling Leaves wind.</summary>
    public float Wind => wind;

    private int TallestFloater => floaters.Count == 0 ? 0 : floaters.Max(f => f.Height);

    /// <inheritdoc />
    public override void Step(SaverStepContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        bool leaves = motion.Style == SaverMovementStyle.FallingLeaves;
        bool attraction = motion.Style == SaverMovementStyle.Attraction;
        if (leaves)
        {
            wind = Math.Clamp((float)(Random.NextWalk(0.0002) + wind), -MaxWind, MaxWind);
        }

        foreach (Floater floater in floaters)
        {
            floater.BeginStep();
        }

        for (int i = 0; i < floaters.Count; i++)
        {
            Floater floater = floaters[i];
            if (attraction)
            {
                for (int j = i + 1; j < floaters.Count; j++)
                {
                    FloaterMotion.Attract(floater, floaters[j]);
                }
            }

            if (leaves)
            {
                floater.X += wind;
            }

            motion.Move(floater, context.FrameStep, context.Stamps);
            context.Sprites.Add(floater.ToDraw());
        }
    }
}
