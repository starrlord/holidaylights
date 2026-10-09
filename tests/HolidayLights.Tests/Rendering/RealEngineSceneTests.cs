using HolidayLights.Core.Sprites;
using HolidayLights.Rendering.Scenes;
using HolidayLights.Tests.Rendering.RealCore;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Rendering;

/// <summary>
/// The Lights thread's scene preparation with the real Core engines together (catalog with every bundled bulb, classic
/// layout, flash engine, sprite provider) on the reference PC: two 3840 x 2160 displays at 150 %, the second at x = -3840.
/// </summary>
public sealed class RealEngineSceneTests : IClassFixture<RealLights>
{
    private readonly RealLights lights;

    public RealEngineSceneTests(RealLights lights) => this.lights = lights;

    /// <summary>Christmas 1 with bundled add-ons on the bottom, the left and one corner.</summary>
    private static SlotAssignment Arrangement() => SlotAssignment.Classic54Default with
    {
        Bottom = [BulbIds.AddOn("HalloweenBulbs"), BulbIds.AddOn("Snowman")],
        Left = [BulbIds.AddOn("Gemstones")],
        TopLeft = BulbIds.AddOn("MulticolorBubbleLights"),
    };

    private LightsScene Scene(FrameMode mode) =>
        lights.Scene(new SceneRecipe { Arrangement = Arrangement(), Frame = mode }, ReferencePc.Displays, LayerMode.BehindIcons);

    [Theory]
    [InlineData(FrameMode.EachDisplay)]
    [InlineData(FrameMode.AllDisplaysTogether)]
    public void ReferencePcScene_PreparesEveryBulbWithSpritesThatFillItsCell(FrameMode mode)
    {
        LightsScene scene = Scene(mode);
        Assert.Contains(scene.Layout.Placements, p => p.BulbId.StartsWith(BulbIds.AddOnPrefix, StringComparison.Ordinal));
        var preparer = new ScenePreparer(lights.Catalog, lights.Sprites, lights.Flash, new RecordingLog());

        PreparedScene prepared = preparer.Prepare(scene, new SequencerRequest(CreateNew: true, StartStep: 0), CancellationToken.None);

        Assert.Equal(0, prepared.SkippedBulbs);
        Assert.Equal(scene.Layout.Placements.Count, prepared.Bulbs.Count);
        foreach (PreparedBulb? bulb in prepared.Bulbs)
        {
            Assert.NotNull(bulb);
            Assert.True(lights.Catalog.TryGetBulb(bulb!.Placement.BulbId, out IBulb? art));
            BulbAnimationInfo animation = art!.GetAnimation(bulb.Placement.Slot, bulb.Placement.Flavor);
            Assert.Equal(animation.Kind, bulb.Kind);
            Assert.Equal(Math.Max(1, animation.FrameCount), bulb.Frames.Count);
            Assert.All(bulb.Frames, frame =>
            {
                // ArtScale is the only scaling rule: every sprite fills its cell to the pixel.
                Assert.Equal(bulb.Placement.Bounds.Size, frame.Size);
                Assert.Equal(bulb.Placement.Bounds.TopLeft, frame.Position);
                PremultipliedImage image = prepared.Images[frame.Id];
                Assert.Equal(frame.Size, new SizeI(image.Width, image.Height));
            });
            Assert.Equal(bulb.Kind == BulbAnimationKind.LightBulb, bulb.Glow is not null);
        }

        IFlashSequencer sequencer = Assert.IsAssignableFrom<IFlashSequencer>(prepared.NewSequencer);
        Assert.Same(scene.Layout, sequencer.Layout);
    }

    [Fact]
    public void ReferencePcScene_DrawsOnBothDisplaysInTheCpuPreview()
    {
        LightsScene scene = Scene(FrameMode.EachDisplay);
        var renderer = new ScenePreviewRenderer(lights.Sprites, new CpuCompositor());
        var request = new ScenePreviewRequest
        {
            Scene = scene,
            Bulbs = lights.Catalog,
            States = ScenePreviewRenderer.CreateStaticStates(scene.Layout, lights.Catalog),
            Zoom = 0.25,
            Backdrop = PreviewBackdrop.None,
            DrawTaskbarBand = false,
        };

        foreach (DisplayScene display in scene.Displays)
        {
            PremultipliedImage image = renderer.RenderDisplay(request, display.Display.DeviceId);
            Assert.Equal(new SizeI(960, 540), new SizeI(image.Width, image.Height));
            Assert.Contains(image.Pixels, pixel => pixel >> 24 != 0);
        }
    }
}
