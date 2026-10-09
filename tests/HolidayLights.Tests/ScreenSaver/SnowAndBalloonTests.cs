using HolidayLights.App.ScreenSaver.Art;
using HolidayLights.App.ScreenSaver.Simulation;

namespace HolidayLights.Tests.ScreenSaver;

public sealed class SnowAndBalloonTests
{
    [Theory]
    [InlineData(400, 100)]
    [InlineData(1000, 250)]
    [InlineData(2560, 500)]
    [InlineData(3, 0)]
    public void Snow_HasAFlakePerFourDipsOfWidth_AtMost500(int width, int flakes)
    {
        var snow = new SnowModule(new SaverField(width, 300), new ConstantRandom(7), SnowArt.Shapes);
        Assert.Equal(flakes, snow.Flakes.Count);
    }

    [Fact]
    public void Snow_StartsAboveTheScreen_WithSizesTwoToFive()
    {
        // 5.4 constructor: x = rand() % W, y = -(rand() % H), size = rand() % 4 + 2.
        var snow = new SnowModule(new SaverField(4, 100), new ScriptedRandom(2, 50, 1), SnowArt.Shapes);
        SnowModule.Flake flake = Assert.Single(snow.Flakes);
        Assert.Equal(2f, flake.X);
        Assert.Equal(-50f, flake.Y);
        Assert.Equal(3, flake.Size);
    }

    [Fact]
    public void SnowStep_MovesWithTheWindAndSpeedFormulas()
    {
        var random = new ScriptedRandom(2, 50, 1, 600, 400, 1500, 1000);
        var snow = new SnowModule(new SaverField(4, 100), random, SnowArt.Shapes);
        var context = new SaverStepContext(null);

        snow.Step(context);

        // wind = (600 - 500) x 0.0002; speed = 4 + (400 - 500) x 0.0002.
        Assert.Equal(0.02f, snow.Wind, 5);
        Assert.Equal(3.98f, snow.Speed, 5);
        // dx = 3 x wind x 0.2 + (1500 - 1000) x 0.001 + wind; dy = (1000 % 2100) x 0.001 + speed.
        SpriteDraw drawn = Assert.Single(context.Sprites);
        Assert.Equal(2f + 0.532f, drawn.X, 4);
        Assert.Equal(-50f + 4.98f, drawn.Y, 4);
        Assert.Equal(2f, drawn.PreviousX);
        Assert.Equal(-50f, drawn.PreviousY);
        Assert.Same(SnowArt.Shapes.ForSize(3), drawn.Art);
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void SnowWindAndSpeed_StayInTheirLimits()
    {
        var up = new SnowModule(new SaverField(8, 100), new ConstantRandom(1000), SnowArt.Shapes);
        var down = new SnowModule(new SaverField(8, 100), new ConstantRandom(0), SnowArt.Shapes);
        for (int i = 0; i < 1000; i++)
        {
            up.Step(new SaverStepContext(null));
            down.Step(new SaverStepContext(null));
        }

        Assert.Equal(4f, up.Wind);
        Assert.Equal(8f, up.Speed);
        Assert.Equal(-4f, down.Wind);
        Assert.Equal(3f, down.Speed);
    }

    [Fact]
    public void LandingFlake_JoinsThePile_ThenStartsAgainAboveTheScreen()
    {
        // With rand() = 500 the wind and speed stay put: dx = -0.5, dy = 0.5 + 4 for a size-2 flake.
        var snow = new SnowModule(new SaverField(4, 100), new ConstantRandom(500), SnowArt.Shapes);
        var context = new SaverStepContext(null);
        int step = 0;
        while (context.Stamps.Count == 0)
        {
            context.Clear();
            snow.Step(context);
            step++;
        }

        // 22 moves of 4.5 DIPs reach y = 99; the flake wrapped from x = -10.5 to W + 5 = 9 on the way.
        Assert.Equal(23, step);
        SpriteStamp stamp = Assert.Single(context.Stamps);
        Assert.Equal(99f, stamp.Y, 4);
        Assert.Equal(8.5f, stamp.X, 4);
        SnowModule.Flake flake = Assert.Single(snow.Flakes);
        Assert.Equal(-1f, flake.Y, 4);
        Assert.Equal(flake.Y, flake.PreviousY);
    }

    [Fact]
    public void SnowPile_GrowsOneDipEvery400Steps_AndStopsAt24()
    {
        var snow = new SnowModule(new SaverField(400, 60), new CrtSaverRandom(1), SnowArt.Shapes);
        var lowestStampByStep = new List<(int Step, float Bottom)>();
        int lastStampStep = 0;
        for (int step = 1; step <= 12_000; step++)
        {
            var context = new SaverStepContext(null);
            snow.Step(context);
            foreach (SpriteStamp stamp in context.Stamps)
            {
                // A flake lands when its next position would reach the ground, so it lies above the ground line.
                Assert.True(stamp.Y < 60 - step / 400 + 1);
                lastStampStep = step;
            }
        }

        // From step 10,000 on the ground is H - 25: the pile is full and flakes no longer stick to it.
        Assert.InRange(lastStampStep, 9_500, 9_999);
    }

    [Fact]
    public void Snow_SticksToBulbs_OncePerArrival_OnlyDuringTheFirst2000Steps()
    {
        // rand() = 501: size 3 (two snow pixels per arrival), steady fall.
        var catcher = new BandCatcher(top: 50, bottom: 70);
        var snow = new SnowModule(new SaverField(4, 100), new ConstantRandom(501), SnowArt.Shapes);
        var arrivals = new List<int>();
        for (int step = 1; step <= 2400; step++)
        {
            var context = new SaverStepContext(catcher);
            snow.Step(context);
            if (context.SnowCells.Count > 0)
            {
                Assert.Equal(2, context.SnowCells.Count);
                Assert.Equal(context.SnowCells[0].Y + 1, context.SnowCells[1].Y);
                Assert.All(context.SnowCells, c => Assert.Equal(BandCatcher.Color, c.Color));
                arrivals.Add(step);
            }
        }

        Assert.True(arrivals.Count > 50);
        Assert.All(arrivals, step => Assert.True(step < SnowModule.StickingSteps));
        Assert.All(arrivals.Zip(arrivals.Skip(1)), pair => Assert.True(pair.Second - pair.First > 1));
    }

    [Fact]
    public void Balloons_OnePer25Dips_RiseFourToSixDipsAndDrift()
    {
        var balloons = new BalloonModule(new SaverField(100, 200), new ConstantRandom(0), SaverArtLibrary.Balloons);
        Assert.Equal(4, balloons.Balloons.Count);
        Assert.All(balloons.Balloons, b =>
        {
            Assert.Equal(-10f, b.X);
            Assert.Equal(200f, b.Y);
            Assert.Equal(0, b.Color);
        });

        var context = new SaverStepContext(null);
        balloons.Step(context);

        Assert.Equal(4, context.Sprites.Count);
        SpriteDraw drawn = context.Sprites[0];
        Assert.Equal(196f, drawn.Y, 4);
        Assert.Equal(-10.05f, drawn.X, 4);
        Assert.Same(SaverArtLibrary.Balloons, drawn.Art);
    }

    [Fact]
    public void Balloons_ComeBackInAtTheBottom()
    {
        // Rising 4 DIPs per step from y = 200, a balloon passes -40 in step 61 and re-enters 240 DIPs lower at a new x.
        var balloons = new BalloonModule(new SaverField(100, 200), new ConstantRandom(0), SaverArtLibrary.Balloons);
        var context = new SaverStepContext(null);
        for (int step = 1; step <= 61; step++)
        {
            context.Clear();
            balloons.Step(context);
        }

        SpriteDraw reentered = context.Sprites[0];
        Assert.Equal(196f, reentered.Y, 4);
        Assert.Equal(-10f, reentered.X);
        Assert.Equal(reentered.X, reentered.PreviousX);
        Assert.Equal(reentered.Y, reentered.PreviousY);
    }

    /// <summary>Bulbs that cover a horizontal band of the screen.</summary>
    private sealed class BandCatcher(int top, int bottom) : ISnowCatcher
    {
        public const uint Color = 0xFFFFFFFF;

        public bool TryCatch(int x, int y, out uint color)
        {
            color = y >= top && y < bottom ? Color : 0;
            return color != 0;
        }
    }
}
