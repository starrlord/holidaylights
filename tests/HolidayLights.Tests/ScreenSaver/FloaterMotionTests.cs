using HolidayLights.App.ScreenSaver.Simulation;

namespace HolidayLights.Tests.ScreenSaver;

public sealed class FloaterMotionTests
{
    private static readonly SaverField Field = new(400, 300);

    [Fact]
    public void BounceOffSides_Respawn_RetriesAStandstillThenPlacesInside()
    {
        // vx = rand() % 9 - 4, vy = rand() % 9 - 4 (retried while both are 0), x = rand() % (W - w), y = rand() % (H - h).
        var random = new ScriptedRandom(4, 4, 5, 7, 30, 40);
        Floater floater = Respawned(SaverMovementStyle.BounceOffSides, random);
        Assert.Equal((1f, 3f, 30f, 40f), (floater.Vx, floater.Vy, floater.X, floater.Y));
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void GravityWell_Respawn_DropsInFromAboveTheScreen()
    {
        // vx = (rand() % 130) / 10 - 6 (integer division), aux = rand() % 100 - 50, x = rand() % (W - w), y = -h.
        Floater floater = Respawned(SaverMovementStyle.GravityWell, new ScriptedRandom(129, 99, 30));
        Assert.Equal((6f, 49f, 30f, -20f), (floater.Vx, floater.Aux, floater.X, floater.Y));
    }

    [Theory]
    [InlineData(3, 0.1f)]
    [InlineData(4, -0.1f)]
    public void FallingLeaves_Respawn_SwingsFromAnEvenOrOddCoin(int coin, float acceleration)
    {
        Floater floater = Respawned(SaverMovementStyle.FallingLeaves, new ScriptedRandom(1000, coin, 30, 40));
        Assert.Equal(5f, floater.Vx, 5);
        Assert.Equal(0f, floater.Vy);
        Assert.Equal(acceleration, floater.Aux);
        Assert.Equal((30f, 40f), (floater.X, floater.Y));
    }

    [Fact]
    public void Attraction_Respawn_UsesSpeedsUpToTen()
    {
        Floater floater = Respawned(SaverMovementStyle.Attraction, new ScriptedRandom(10, 20, 30, 40));
        Assert.Equal((0f, 10f, 30f, 40f), (floater.Vx, floater.Vy, floater.X, floater.Y));
    }

    [Fact]
    public void Bouncing_ReversesPastTheEdges_WithoutClampingThePosition()
    {
        var motion = new FloaterMotion(SaverMovementStyle.BounceOffSides, Field, new ScriptedRandom());
        Floater floater = Sprite(20, 20);
        floater.X = Field.Width - 20 - 0.5f;
        floater.Y = 0.5f;
        floater.Vx = 1;
        floater.Vy = -1;

        motion.Move(floater, frameStep: false, []);

        Assert.Equal(Field.Width - 20 + 0.5f, floater.X);
        Assert.Equal(-0.5f, floater.Y);
        Assert.Equal((-1f, 1f), (floater.Vx, floater.Vy));
        Assert.Equal(1, floater.Age);
    }

    [Fact]
    public void GravityWell_BouncesWith70PercentAndStampsWhenAtRest()
    {
        var random = new ScriptedRandom(70, 50, 100);
        var motion = new FloaterMotion(SaverMovementStyle.GravityWell, Field, random);
        Floater floater = Sprite(20, 20);
        float floor = Field.Height - 20;
        floater.X = 100;
        floater.Y = floor - 3;
        floater.Aux = 2;
        var stamps = new List<SpriteStamp>();

        // aux 3.5, y = floor + 0.5: bounce, aux = -2.45.
        motion.Move(floater, false, stamps);
        Assert.Equal(floor, floater.Y);
        Assert.Equal(-2.45f, floater.Aux, 4);

        // aux -0.95: rising, no bounce (aux must be positive).
        motion.Move(floater, false, stamps);
        Assert.Equal(-0.95f, floater.Aux, 4);
        Assert.Empty(stamps);

        // aux 0.55, y = floor - 0.4: (int)(y - floor) = 0, bounce to -0.385 > -1: at rest, stamped, dropped in again.
        motion.Move(floater, false, stamps);
        SpriteStamp stamp = Assert.Single(stamps);
        Assert.Equal((100f, floor), (stamp.X, stamp.Y));
        Assert.Equal((1f, 0f, 100f, -20f), (floater.Vx, floater.Aux, floater.X, floater.Y));
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void FallingLeaves_SwingBackPastSixAndFallOnlyWhileSwingingFast()
    {
        var motion = new FloaterMotion(SaverMovementStyle.FallingLeaves, Field, new ScriptedRandom(5, 7));
        Floater floater = Sprite(10, 10);
        floater.X = 100;
        floater.Y = 50;
        floater.Vx = 5.9f;
        floater.Aux = 0.3f;

        // x += 5.9; vx = 6.2; rand() % 5 == 0 and vx > 6: the swing turns back; y += |(int)6.2|.
        motion.Move(floater, false, []);
        Assert.Equal(105.9f, floater.X, 4);
        Assert.Equal(6.2f, floater.Vx, 4);
        Assert.Equal(-0.3f, floater.Aux);
        Assert.Equal(56f, floater.Y);

        // rand() % 5 != 0: no change of swing; vx = 5.9: falls 5.
        motion.Move(floater, false, []);
        Assert.Equal(-0.3f, floater.Aux);
        Assert.Equal(61f, floater.Y);
    }

    [Fact]
    public void FallingLeaves_LandOnThePileAndStartAgainAtTheTop()
    {
        var motion = new FloaterMotion(SaverMovementStyle.FallingLeaves, Field, new ScriptedRandom(1, 77));
        Floater floater = Sprite(10, 10);
        floater.X = 100;
        floater.Y = Field.Height - 12;
        floater.Vx = 3;
        floater.Age = 250;
        var stamps = new List<SpriteStamp>();

        // The ground is H - 250/100 = H - 2; the leaf's bottom reaches it: stamped at y + 3, then back above the screen.
        motion.Move(floater, false, stamps);

        SpriteStamp stamp = Assert.Single(stamps);
        Assert.Equal(Field.Height - 9f, stamp.Y);
        Assert.Equal((77f, -10f), (floater.X, floater.Y));
        Assert.Equal((floater.X, floater.Y), (floater.PreviousX, floater.PreviousY));
    }

    [Fact]
    public void FallingLeaves_FallOffTheScreenOnceThePileIsFull()
    {
        // At age 2,500 the ground would be H - 25: the pile is full and leaves fall until they are below the screen.
        var motion = new FloaterMotion(SaverMovementStyle.FallingLeaves, Field, new ScriptedRandom(1, 1, 77));
        Floater floater = Sprite(10, 10);
        floater.Y = Field.Height - 10;
        floater.Vx = 3;
        floater.Age = 2500;
        var stamps = new List<SpriteStamp>();

        motion.Move(floater, false, stamps);
        Assert.Equal(Field.Height - 7f, floater.Y);

        floater.Y = Field.Height - 1;
        motion.Move(floater, false, stamps);
        Assert.Empty(stamps);
        Assert.Equal((77f, -10f), (floater.X, floater.Y));
    }

    [Fact]
    public void Attraction_PullsBothFloatersTogether()
    {
        Floater a = Sprite(10, 10);
        Floater b = Sprite(10, 10);
        b.X = 200;

        FloaterMotion.Attract(a, b);

        // d = 200 x 0.05 = 10, f = 0.6 / 100, a = (dx / d) x f = -0.12.
        Assert.Equal(0.12f, a.Vx, 5);
        Assert.Equal(-0.12f, b.Vx, 5);
        Assert.Equal(0f, a.Vy);
        Assert.Equal(0f, b.Vy);
    }

    [Fact]
    public void Attraction_ClampsCloseDistancesAndSpeeds()
    {
        Floater a = Sprite(10, 10);
        Floater b = Sprite(10, 10);
        b.X = 50;
        b.Vx = -24.9f;
        a.Vx = 24.9f;

        FloaterMotion.Attract(a, b);

        // d = 2.5 is raised to 5: f = 0.024, a = -0.24; both speeds pass 25 and are limited.
        Assert.Equal(-25f, b.Vx);
        Assert.Equal(25f, a.Vx);
    }

    [Fact]
    public void FloaterModule_StopsCreatingOnceAnEighthOfTheScreenIsCovered()
    {
        var art = new SpriteArt("square", [new Rgba32Image(20, 20)]);
        var module = new FloaterModule(new SaverField(100, 100), new ConstantRandom(3), SaverMovementStyle.BounceOffSides, [art]);

        // 4 x 400 x 8 = 12,800 > 10,000 after the fourth; 3 x 400 x 8 = 9,600 is not.
        Assert.Equal(4, module.Floaters.Count);
    }

    [Fact]
    public void FloaterModule_StartsFloatersOutOfPhase_AndCyclesThePictures()
    {
        var three = new SpriteArt("three", [new Rgba32Image(5, 5), new Rgba32Image(5, 5), new Rgba32Image(5, 5)]);
        var two = new SpriteArt("two", [new Rgba32Image(5, 5), new Rgba32Image(5, 5)]);
        var module = new FloaterModule(new SaverField(2000, 2000), new ConstantRandom(1), SaverMovementStyle.BounceOffSides, [three, two]);

        Assert.Equal(FloaterModule.MaxFloaters, module.Floaters.Count);
        for (int i = 0; i < module.Floaters.Count; i++)
        {
            SpriteArt expected = i % 2 == 0 ? three : two;
            Assert.Same(expected, module.Floaters[i].Art);
            Assert.Equal(i % expected.FrameCount, module.Floaters[i].Frame);
        }
    }

    [Fact]
    public void Simulation_AdvancesSpriteFramesEveryFlashInterval()
    {
        var art = new SpriteArt("four", [new Rgba32Image(5, 5), new Rgba32Image(5, 5), new Rgba32Image(5, 5), new Rgba32Image(5, 5)]);
        var module = new FloaterModule(new SaverField(2000, 2000), new ConstantRandom(1), SaverMovementStyle.BounceOffSides, [art]);
        var simulation = new SaverSimulation(module, null, frameInterval: 3, bulbs: null);
        var frames = new List<int>();
        for (int step = 0; step < 9; step++)
        {
            simulation.Step();
            frames.Add(simulation.Sprites[0].Frame);
        }

        Assert.Equal([0, 0, 1, 1, 1, 2, 2, 2, 3], frames);
    }

    [Fact]
    public void FallingLeavesModule_AddsTheSharedWindBeforeMoving()
    {
        var art = new SpriteArt("leaf", [new Rgba32Image(10, 10)]);
        var module = new FloaterModule(new SaverField(2000, 2000), new ConstantRandom(1000), SaverMovementStyle.FallingLeaves, [art]);
        float before = module.Floaters[0].X;

        module.Step(new SaverStepContext(null));

        // wind = (1000 - 500) x 0.0002 = 0.1; then x += vx (5) and the swing.
        Assert.Equal(0.1f, module.Wind, 5);
        Assert.Equal(before + 0.1f + 5f, module.Floaters[0].X, 3);
    }

    private static Floater Respawned(SaverMovementStyle style, ScriptedRandom random)
    {
        Floater floater = Sprite(20, 20);
        new FloaterMotion(style, Field, random).Respawn(floater);
        return floater;
    }

    private static Floater Sprite(int width, int height) => new(new SpriteArt("sprite", [new Rgba32Image(width, height)]), 0);
}
