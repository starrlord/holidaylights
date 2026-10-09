namespace HolidayLights.App.ScreenSaver.Simulation;

/// <summary>
/// The four movement styles of the floaters, exactly as 5.4 computes them per 60 ms step (5.4
/// <c>ScreenSaverFloater_Respawn</c>, <c>_Move</c>, <c>_Attract</c>). All constants are DIPs per step.
/// </summary>
internal sealed class FloaterMotion
{
    /// <summary>Gravity Well: speed added per step.</summary>
    public const float Gravity = 1.5f;

    /// <summary>Gravity Well: share of the speed kept by a bounce (negated).</summary>
    public const double Restitution = -0.7;

    /// <summary>Falling Leaves: the swing reverses beyond this speed.</summary>
    public const float LeafMaxSwing = 6f;

    /// <summary>Falling Leaves: the swing acceleration after a reversal.</summary>
    public const float LeafSwingAcceleration = 0.3f;

    /// <summary>Falling Leaves: the swing acceleration a leaf starts with.</summary>
    public const float LeafStartAcceleration = 0.1f;

    /// <summary>Falling Leaves: steps per DIP of leaf-pile growth.</summary>
    public const int StepsPerLeafPileRow = 100;

    /// <summary>Falling Leaves: the pile stops growing at this height (DIPs).</summary>
    public const int MaxLeafPileHeight = 24;

    /// <summary>Attraction: the pull strength.</summary>
    public const float AttractionStrength = 0.6f;

    /// <summary>Attraction: the speed limit.</summary>
    public const float AttractionMaxSpeed = 25f;

    private readonly SaverMovementStyle style;
    private readonly SaverField field;
    private readonly ISaverRandom random;

    /// <summary>Creates the motion of one style on one screen.</summary>
    /// <param name="style">The style.</param>
    /// <param name="field">The simulated screen.</param>
    /// <param name="random">The random stream.</param>
    public FloaterMotion(SaverMovementStyle style, SaverField field, ISaverRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        this.style = style;
        this.field = field;
        this.random = random;
    }

    /// <summary>The style.</summary>
    public SaverMovementStyle Style => style;

    /// <summary>
    /// 5.4 <c>Respawn</c>: a new speed and position for the style (when a floater is created and when a Gravity Well
    /// object came to rest). The order of the <c>rand()</c> calls is the original's.
    /// </summary>
    /// <param name="floater">The floater.</param>
    public void Respawn(Floater floater)
    {
        ArgumentNullException.ThrowIfNull(floater);
        switch (style)
        {
            case SaverMovementStyle.GravityWell:
                floater.Vx = random.Next() % 130 / 10 - 6;
                floater.Aux = random.Next() % 100 - 50;
                floater.X = RandomLeft(floater);
                floater.Y = -floater.Height;
                break;
            case SaverMovementStyle.FallingLeaves:
                floater.Vx = (float)random.NextWalk(0.01);
                floater.Vy = 0;
                floater.Aux = (random.Next() & 1) == 0 ? -LeafStartAcceleration : LeafStartAcceleration;
                floater.X = RandomLeft(floater);
                floater.Y = RandomTop(floater);
                break;
            case SaverMovementStyle.Attraction:
                RandomVelocity(floater, 10);
                floater.X = RandomLeft(floater);
                floater.Y = RandomTop(floater);
                break;
            default:
                RandomVelocity(floater, 4);
                floater.X = RandomLeft(floater);
                floater.Y = RandomTop(floater);
                break;
        }

        floater.Teleported();
    }

    /// <summary>
    /// 5.4 <c>Move</c>: one step of the style, stamping objects that come to rest (Gravity Well) or land while the leaf pile
    /// grows (Falling Leaves); then the age grows and, on a frame step, the frame advances.
    /// </summary>
    /// <param name="floater">The floater.</param>
    /// <param name="frameStep">True when sprites advance a frame in this step.</param>
    /// <param name="stamps">Where stamps go.</param>
    public void Move(Floater floater, bool frameStep, List<SpriteStamp> stamps)
    {
        ArgumentNullException.ThrowIfNull(floater);
        ArgumentNullException.ThrowIfNull(stamps);
        switch (style)
        {
            case SaverMovementStyle.GravityWell:
                MoveGravityWell(floater, stamps);
                break;
            case SaverMovementStyle.FallingLeaves:
                MoveFallingLeaves(floater, stamps);
                break;
            default:
                MoveBouncing(floater);
                break;
        }

        floater.Age++;
        if (frameStep && ++floater.Frame >= floater.Art.FrameCount)
        {
            floater.Frame = 0;
        }
    }

    /// <summary>
    /// 5.4 <c>Attract</c> for one pair: each pulls the other with <c>0.6 / d²</c>, where d is a twentieth of the distance (at
    /// least 5); speeds are limited to 25.
    /// </summary>
    /// <param name="a">The first floater (index i).</param>
    /// <param name="b">The second floater (index j &gt; i).</param>
    public static void Attract(Floater a, Floater b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        float distance = MathF.Sqrt(dx * dx + dy * dy) * 0.05f;
        if (distance < 5f)
        {
            distance = 5f;
        }

        float force = AttractionStrength / (distance * distance);
        float ax = dx / distance * force;
        float ay = dy / distance * force;
        b.Vx = ClampSpeed(b.Vx + ax);
        b.Vy = ClampSpeed(b.Vy + ay);
        a.Vx = ClampSpeed(a.Vx - ax);
        a.Vy = ClampSpeed(a.Vy - ay);
    }

    /// <summary>Bounce Off Sides and Attraction: straight lines, reversed at the edges (the position is not clamped).</summary>
    private void MoveBouncing(Floater floater)
    {
        floater.X += floater.Vx;
        if (floater.X < 0 || floater.X > field.Width - floater.Width)
        {
            floater.Vx = -floater.Vx;
        }

        floater.Y += floater.Vy;
        if (floater.Y < 0 || floater.Y > field.Height - floater.Height)
        {
            floater.Vy = -floater.Vy;
        }
    }

    /// <summary>Gravity Well: falls, bounces with 70 % restitution and, once at rest, is stamped and drops in again.</summary>
    private void MoveGravityWell(Floater floater, List<SpriteStamp> stamps)
    {
        floater.X += floater.Vx;
        if (floater.X < 0 || floater.X > field.Width - floater.Width)
        {
            floater.Vx = -floater.Vx;
        }

        floater.Aux += Gravity;
        floater.Y += floater.Aux;
        float floor = field.Height - floater.Height;
        if ((int)(floater.Y - floor) >= 0 && floater.Aux > 0)
        {
            floater.Aux = (float)(floater.Aux * Restitution);
            floater.Y = floor;
            if (floater.Aux > -1f)
            {
                stamps.Add(floater.ToStamp());
                Respawn(floater);
            }
        }
    }

    /// <summary>Falling Leaves: swings from side to side, falls while swinging fast, lands on the growing pile and starts again at the top.</summary>
    private void MoveFallingLeaves(Floater floater, List<SpriteStamp> stamps)
    {
        floater.X += floater.Vx;
        floater.Vx += floater.Aux;
        if (random.Next() % 5 == 0)
        {
            if (floater.Vx > LeafMaxSwing)
            {
                floater.Aux = -LeafSwingAcceleration;
            }
            else if (floater.Vx < -LeafMaxSwing)
            {
                floater.Aux = LeafSwingAcceleration;
            }
        }

        floater.Y += Math.Abs((int)floater.Vx);
        int ground = field.Height - floater.Age / StepsPerLeafPileRow;
        bool full = ground < field.Height - MaxLeafPileHeight;
        if (full)
        {
            ground = floater.Height + field.Height;
        }

        if (ground <= floater.Height + floater.Y)
        {
            if (!full)
            {
                stamps.Add(floater.ToStamp());
            }

            floater.Y = -floater.Height;
            floater.X = RandomLeft(floater);
            floater.Teleported();
        }

        if (floater.X < -floater.Width)
        {
            floater.X += floater.Width + field.Width;
            floater.Teleported();
        }
        else if (floater.X > field.Width)
        {
            floater.X -= floater.Width + field.Width;
            floater.Teleported();
        }
    }

    /// <summary>Speeds -limit..limit on both axes, never both zero (5.4 retries).</summary>
    private void RandomVelocity(Floater floater, int limit)
    {
        do
        {
            floater.Vx = random.Next() % (2 * limit + 1) - limit;
            floater.Vy = random.Next() % (2 * limit + 1) - limit;
        }
        while (floater.Vx == 0 && floater.Vy == 0);
    }

    private float RandomLeft(Floater floater) => random.NextModulo(field.Width - floater.Width);

    private float RandomTop(Floater floater) => random.NextModulo(field.Height - floater.Height);

    private static float ClampSpeed(float speed) => Math.Clamp(speed, -AttractionMaxSpeed, AttractionMaxSpeed);
}
