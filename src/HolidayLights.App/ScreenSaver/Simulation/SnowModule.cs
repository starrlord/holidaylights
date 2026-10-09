using HolidayLights.App.ScreenSaver.Art;

namespace HolidayLights.App.ScreenSaver.Simulation;

/// <summary>
/// "Snow" and "Snow Flakes" (5.4 <c>SnowFlakeModule</c>): W / 4 flakes (at most 500)
/// drift down in a random wind, pile up along the bottom (1 DIP every 400 steps, at most 24 DIPs) and, during the first
/// 2,000 steps, stick to the bulbs they fall on.
/// </summary>
internal sealed class SnowModule : SaverModule
{
    /// <summary>Most flakes on one screen.</summary>
    public const int MaxFlakes = 500;

    /// <summary>The snow pile stops growing at this height (DIPs).</summary>
    public const int MaxPileHeight = 24;

    /// <summary>Steps per DIP of pile growth.</summary>
    public const int StepsPerPileRow = 400;

    /// <summary>Snow sticks to the bulbs only during this many steps (about two minutes).</summary>
    public const int StickingSteps = 2000;

    /// <summary>The largest distance a flake falls in one step: (5 x 700 - 1) / 1000 + the top speed 8.</summary>
    private const int MaxFall = 12;

    private readonly SnowArt art;
    private readonly Flake[] flakes;
    private float wind;
    private float speed = 4f;
    private int frame;

    /// <summary>Creates the flakes above the screen (5.4 constructor: x, y and size from three <c>rand()</c> calls each).</summary>
    /// <param name="field">The simulated screen.</param>
    /// <param name="random">The random stream.</param>
    /// <param name="art">The flake pictures (shapes for Snow, the FLAKE bitmaps for Snow Flakes).</param>
    public SnowModule(SaverField field, ISaverRandom random, SnowArt art)
        : base(field, random)
    {
        ArgumentNullException.ThrowIfNull(art);
        this.art = art;
        flakes = new Flake[Math.Clamp(field.Width / 4, 0, MaxFlakes)];
        for (int i = 0; i < flakes.Length; i++)
        {
            float x = random.NextModulo(field.Width);
            float y = -random.NextModulo(field.Height);
            int size = random.NextModulo(4) + 2;
            flakes[i] = new Flake { X = x, Y = y, PreviousX = x, PreviousY = y, Size = size };
        }
    }

    /// <inheritdoc />
    public override int StampReach => MaxPileHeight + MaxFall + art.MaxHeight;

    /// <summary>The current wind (DIPs per step, -4 to 4).</summary>
    public float Wind => wind;

    /// <summary>The current fall speed (DIPs per step, 3 to 8).</summary>
    public float Speed => speed;

    /// <summary>The flakes (for tests and diagnostics).</summary>
    public IReadOnlyList<Flake> Flakes => flakes;

    /// <inheritdoc />
    public override void Step(SaverStepContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        frame++;
        wind = Math.Clamp((float)(Random.NextWalk(0.0002) + wind), -4f, 4f);
        speed = Math.Clamp((float)(Random.NextWalk(0.0002) + speed), 3f, 8f);

        int height = Field.Height;
        int ground = height - frame / StepsPerPileRow;
        bool full = ground < height - MaxPileHeight;
        if (full)
        {
            ground = height - MaxPileHeight;
        }

        for (int i = 0; i < flakes.Length; i++)
        {
            StepFlake(ref flakes[i], ground, full, context);
        }
    }

    private void StepFlake(ref Flake flake, int ground, bool full, SaverStepContext context)
    {
        flake.PreviousX = flake.X;
        flake.PreviousY = flake.Y;
        float dx = (float)(flake.Size * wind * 0.2 + (Random.Next() % 2001 - 1000) * 0.001 + wind);
        float dy = (float)(Random.NextModulo(flake.Size * 700) * 0.001 + speed);
        SpriteArt sprite = art.ForSize(flake.Size);
        if (flake.Y + dy < ground)
        {
            flake.Y += dy;
            flake.X += dx;
            if (flake.X > Field.Width + 10)
            {
                flake.X = -5;
                flake.PreviousX = flake.X;
            }
            else if (flake.X < -10)
            {
                flake.X = Field.Width + 5;
                flake.PreviousX = flake.X;
            }

            if (frame < StickingSteps && context.Bulbs is { } bulbs)
            {
                StickToBulbs(ref flake, bulbs, context.SnowCells);
            }

            context.Sprites.Add(new SpriteDraw(sprite, 0, flake.X, flake.Y, flake.PreviousX, flake.PreviousY));
            return;
        }

        if (!full)
        {
            // The flake joins the pile: drawn into the background and the frame where it is.
            context.Stamps.Add(new SpriteStamp(sprite, 0, flake.X, flake.Y));
            context.Sprites.Add(new SpriteDraw(sprite, 0, flake.X, flake.Y, flake.PreviousX, flake.PreviousY));
        }

        flake.Y -= Field.Height;
        flake.PreviousY = flake.Y;
        flake.PreviousX = flake.X;
    }

    /// <summary>5.4 <c>StickToBulbs</c>: a flake that newly lands on a bulb leaves one pixel of snow there (two for big flakes).</summary>
    private static void StickToBulbs(ref Flake flake, ISnowCatcher bulbs, List<SnowCell> cells)
    {
        int x = (int)(flake.X + flake.Size / 2);
        int y = (int)flake.Y;
        bool hit = bulbs.TryCatch(x, y, out uint color);
        if (hit && !flake.OnBulb)
        {
            cells.Add(new SnowCell(x, y, color));
            if (flake.Size > 2 && bulbs.TryCatch(x, y + 1, out uint below))
            {
                cells.Add(new SnowCell(x, y + 1, below));
            }
        }

        flake.OnBulb = hit;
    }

    /// <summary>One flake (5.4 <c>{ float x, y; short size; bool onBulb; }</c>).</summary>
    internal struct Flake
    {
        /// <summary>Left in DIPs.</summary>
        public float X;

        /// <summary>Top in DIPs.</summary>
        public float Y;

        /// <summary>Left before the last step.</summary>
        public float PreviousX;

        /// <summary>Top before the last step.</summary>
        public float PreviousY;

        /// <summary>2 to 5.</summary>
        public int Size;

        /// <summary>True while the flake is over a bulb (snow sticks only when it arrives).</summary>
        public bool OnBulb;
    }
}
