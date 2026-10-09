using HolidayLights.Rendering.Scenes;
using HolidayLights.Tests.Rendering.Fakes;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Rendering;

public sealed class ScenePreparationTests
{
    private static LightsScene Scene(LightsLayout layout, GlowLevel glow = GlowLevel.Soft, FlashPatternId pattern = FlashPatternId.FlashTogether, SpriteStyle pixels = SpriteStyle.Smooth) =>
        FakeLayout.Scene([SyntheticScene.Display], layout, LayerMode.BehindIcons, pattern, glow: glow) with
        {
            Effects = new SceneEffects { Pixels = pixels, Glow = glow },
        };

    [Theory]
    [InlineData(CellSlot.Top, 10, 20, 0, 0)]
    [InlineData(CellSlot.Left, 10, 20, 0, 0)]
    [InlineData(CellSlot.TopLeft, 10, 20, 0, 0)]
    [InlineData(CellSlot.Bottom, 10, 20, 0, 30)]
    [InlineData(CellSlot.BottomLeft, 10, 20, 0, 30)]
    [InlineData(CellSlot.Right, 10, 20, 38, 0)]
    [InlineData(CellSlot.TopRight, 10, 20, 38, 0)]
    [InlineData(CellSlot.BottomRight, 10, 20, 38, 30)]
    public void SpritesAreAlignedLikeTheClassicStrips(CellSlot slot, int width, int height, int expectedX, int expectedY)
    {
        var bounds = RectI.FromXYWH(100, 200, 48, 50);
        PointI position = SpriteAnchors.Position(slot, bounds, new SizeI(width, height));

        Assert.Equal(new PointI(100 + expectedX, 200 + expectedY), position);
        Assert.Equal(bounds.TopLeft, SpriteAnchors.Position(slot, bounds, bounds.Size));
    }

    [Fact]
    public void Prepare_GivesEveryBulbItsFramesAndLightBulbsTheirGlow()
    {
        LightsLayout layout = SyntheticScene.Layout(corner: "builtin:test-animated");
        var sprites = new FakeSpriteProvider();
        var engine = new FakeFlashEngine();
        var preparer = new ScenePreparer(SyntheticScene.Resolver, sprites, engine, new RecordingLog());

        PreparedScene prepared = preparer.Prepare(Scene(layout), new SequencerRequest(CreateNew: true, StartStep: 7), CancellationToken.None);

        Assert.Equal(0, prepared.SkippedBulbs);
        Assert.Equal(layout.Placements.Count, prepared.Bulbs.Count);
        foreach (PreparedBulb? bulb in prepared.Bulbs)
        {
            Assert.NotNull(bulb);
            if (bulb!.Placement.IsCorner)
            {
                Assert.Equal(BulbAnimationKind.Animation, bulb.Kind);
                Assert.Equal(4, bulb.Frames.Count);
                Assert.Null(bulb.Glow);
            }
            else
            {
                Assert.Equal(BulbAnimationKind.LightBulb, bulb.Kind);
                Assert.Equal(2, bulb.Frames.Count);
                Assert.NotNull(bulb.Glow);
                Assert.Equal(-1, bulb.Glow!.Value.Id.Frame);
                Assert.True(bulb.Glow.Value.Position.X < bulb.Frames[bulb.LitFrame].Position.X);
            }

            Assert.All(bulb.Frames, f => Assert.True(prepared.Images.ContainsKey(f.Id)));
            Assert.Equal(bulb.Placement.Bounds.TopLeft, bulb.Frames[0].Position);
        }

        // Light bulbs with 3 flavors on 4 sides: 3 x 4 x 2 frames; the animated corners 4 x 4 frames; 3 x 4 glows.
        Assert.Equal(24 + 16 + 12, prepared.Images.Count);
        FakeSequencer sequencer = Assert.IsType<FakeSequencer>(prepared.NewSequencer);
        Assert.Equal(7, sequencer.Step);
        Assert.Equal(1, engine.Created);
    }

    [Fact]
    public void Prepare_WithoutGlowOrNewSequencer()
    {
        LightsLayout layout = SyntheticScene.Layout();
        var engine = new FakeFlashEngine();
        var preparer = new ScenePreparer(SyntheticScene.Resolver, new FakeSpriteProvider(), engine, new RecordingLog());

        PreparedScene prepared = preparer.Prepare(Scene(layout, GlowLevel.Off), new SequencerRequest(false, 0), CancellationToken.None);

        Assert.All(prepared.Bulbs, b => Assert.Null(b!.Glow));
        Assert.Null(prepared.NewSequencer);
        Assert.Equal(0, engine.Created);
    }

    [Fact]
    public void Prepare_SkipsBulbsThatCannotBeResolved()
    {
        LightsLayout layout = SyntheticScene.Layout(corner: "builtin:missing");
        var preparer = new ScenePreparer(SyntheticScene.Resolver, new FakeSpriteProvider(), new FakeFlashEngine(), new RecordingLog());

        PreparedScene prepared = preparer.Prepare(Scene(layout), new SequencerRequest(false, 0), CancellationToken.None);

        Assert.Equal(4, prepared.SkippedBulbs);
        Assert.All(layout.Placements.Where(p => p.IsCorner), p => Assert.Null(prepared.Bulbs[p.Ordinal]));
        Assert.Equal(layout.Placements.Count - 4, prepared.BulbsOn(SyntheticScene.Display.DeviceId).Count());
    }

    [Fact]
    public void Prepare_LogsAndSkipsABulbWhoseArtFails()
    {
        LightsLayout layout = SyntheticScene.Layout();
        var log = new RecordingLog();
        var preparer = new ScenePreparer(SyntheticScene.Resolver, new FailingSprites(), new FakeFlashEngine(), log);

        PreparedScene prepared = preparer.Prepare(Scene(layout), new SequencerRequest(false, 0), CancellationToken.None);

        Assert.Equal(layout.Placements.Count, prepared.SkippedBulbs);
        Assert.Single(log.Entries, e => e.Level == AppLogLevel.Warning);
    }

    [Fact]
    public void Layouts_AreComparedByValue()
    {
        LightsLayout a = SyntheticScene.Layout();
        LightsLayout b = SyntheticScene.Layout();
        Assert.NotSame(a, b);
        Assert.True(SceneComparison.SameLayout(a, b));
        Assert.False(SceneComparison.SameLayout(a, SyntheticScene.Layout(corner: "builtin:test-static")));

        LightsScene sa = Scene(a);
        Assert.True(SceneComparison.SameSequencerInputs(sa, Scene(b)));
        Assert.False(SceneComparison.SameSequencerInputs(sa, Scene(b, pattern: FlashPatternId.Alternating)));
        Assert.True(SceneComparison.SameSprites(sa, Scene(b, GlowLevel.Bright)));
        Assert.False(SceneComparison.SameSprites(sa, Scene(b, GlowLevel.Off)));
        Assert.False(SceneComparison.SameSprites(sa, Scene(b, pixels: SpriteStyle.Crisp)));
    }

    [Fact]
    public void PlacementKeys_IgnoreOrdinalsAndRingsButNotPlaceOrBulb()
    {
        BulbPlacement placement = SyntheticScene.Layout().Placements[5];
        PlacementKey key = PlacementKey.Of(placement);

        Assert.Equal(key, PlacementKey.Of(placement with { Ordinal = 99, RingIndex = 3, RingId = 2, ChaseIndex = 9 }));
        Assert.Equal(key, PlacementKey.Of(placement with { BulbId = placement.BulbId.ToUpperInvariant() }));
        Assert.NotEqual(key, PlacementKey.Of(placement with { Flavor = placement.Flavor + 1 }));
        Assert.NotEqual(key, PlacementKey.Of(placement with { Bounds = placement.Bounds.Offset(1, 0) }));
        Assert.NotEqual(key, PlacementKey.Of(placement with { BulbId = "builtin:other" }));
    }

    private sealed class FailingSprites : ISpriteProvider
    {
        public PremultipliedImage GetSprite(IBulb bulb, CellSlot slot, int flavor, int frame, double scale, SpriteStyle style) =>
            throw new InvalidDataException("damaged art");

        public GlowSprite? GetGlow(IBulb bulb, CellSlot slot, int flavor, double scale) => null;

        public Task PrefetchAsync(IEnumerable<SpriteRequest> requests, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Evict(string contentKey)
        {
        }
    }
}
