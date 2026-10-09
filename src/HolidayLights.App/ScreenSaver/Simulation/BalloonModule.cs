namespace HolidayLights.App.ScreenSaver.Simulation;

/// <summary>
/// "Balloons" (5.4 <c>BalloonModule</c>): W / 25 balloons in six colours rise 4-6 DIPs
/// per step with a random sideways drift and come back in at the bottom.
/// </summary>
internal sealed class BalloonModule : SaverModule
{
    /// <summary>Screen width per balloon.</summary>
    public const int WidthPerBalloon = 25;

    /// <summary>Width of one balloon in the BALLOON strip.</summary>
    public const int FrameWidth = 21;

    /// <summary>A balloon wraps back to the bottom once it is this far above the top (the last 10 DIPs of string are cut off, 5.4).</summary>
    public const int WrapHeight = 40;

    private readonly SpriteArt art;
    private readonly Balloon[] balloons;

    /// <summary>Creates the balloons below the screen (5.4 constructor: x, y and colour from three <c>rand()</c> calls each).</summary>
    /// <param name="field">The simulated screen.</param>
    /// <param name="random">The random stream.</param>
    /// <param name="art">The six balloons (frame = colour: red, blue, orange, purple, yellow, green).</param>
    public BalloonModule(SaverField field, ISaverRandom random, SpriteArt art)
        : base(field, random)
    {
        ArgumentNullException.ThrowIfNull(art);
        this.art = art;
        balloons = new Balloon[Math.Max(0, field.Width / WidthPerBalloon)];
        for (int i = 0; i < balloons.Length; i++)
        {
            float x = random.NextModulo(field.Width + FrameWidth) - FrameWidth / 2;
            float y = random.NextModulo(field.Height) + field.Height;
            int color = random.NextModulo(art.FrameCount);
            balloons[i] = new Balloon { X = x, Y = y, PreviousX = x, PreviousY = y, Color = color };
        }
    }

    /// <inheritdoc />
    public override int StampReach => 0;

    /// <summary>The balloons (for tests and diagnostics).</summary>
    public IReadOnlyList<Balloon> Balloons => balloons;

    /// <inheritdoc />
    public override void Step(SaverStepContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        for (int i = 0; i < balloons.Length; i++)
        {
            ref Balloon balloon = ref balloons[i];
            balloon.PreviousX = balloon.X;
            balloon.PreviousY = balloon.Y;
            balloon.Y -= (float)((Random.Next() % 200 + 400) * 0.01);
            float drift = Math.Clamp((float)Random.NextWalk(0.0001), -0.5f, 0.5f);
            balloon.Vx += drift;
            balloon.X += balloon.Vx;
            if (balloon.X < -FrameWidth)
            {
                balloon.X += FrameWidth + Field.Width;
                balloon.PreviousX = balloon.X;
            }
            else if (balloon.X > Field.Width)
            {
                balloon.X -= FrameWidth + Field.Width;
                balloon.PreviousX = balloon.X;
            }

            if (balloon.Y < -WrapHeight)
            {
                balloon.Y += WrapHeight + Field.Height;
                balloon.X = Random.NextModulo(Field.Width + FrameWidth) - FrameWidth / 2;
                balloon.PreviousX = balloon.X;
                balloon.PreviousY = balloon.Y;
            }

            context.Sprites.Add(new SpriteDraw(art, balloon.Color, balloon.X, balloon.Y, balloon.PreviousX, balloon.PreviousY));
        }
    }

    /// <summary>One balloon (5.4 <c>{ float x, y; int color; float vx; }</c>).</summary>
    internal struct Balloon
    {
        /// <summary>Left in DIPs.</summary>
        public float X;

        /// <summary>Top in DIPs.</summary>
        public float Y;

        /// <summary>Left before the last step.</summary>
        public float PreviousX;

        /// <summary>Top before the last step.</summary>
        public float PreviousY;

        /// <summary>Colour 0-5 (the frame of the strip).</summary>
        public int Color;

        /// <summary>Sideways speed: an unbounded random walk.</summary>
        public float Vx;
    }
}
