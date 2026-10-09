using System.Windows;
using System.Windows.Controls;
using HolidayLights.App.Controls;
using HolidayLights.App.Preview;
using HolidayLights.App.Settings;
using HolidayLights.App.Settings.Pages;
using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.Ui.Visuals;

/// <summary>
/// PO decision 8: previews show real bulb art at a legible size with the exact layout. Theme cards zoom into the top-left
/// corner, Home's stage magnifies a corner of each display, and a try-on zooms the Bulb Factory stage into its target.
/// </summary>
[Collection(nameof(WpfCollection))]
public sealed class PreviewZoomTests(UiThread ui)
{
    private static readonly string CandyCanes = BulbIds.BuiltIn("candy-canes");

    [Fact]
    public void ThemeCardsShowACornerAtALegibleSize() => ui.Run(() =>
    {
        using var services = Ready();
        var card = new LightStage
        {
            Services = services,
            Geometry = StageGeometry.DisplayAlone,
            Arrangement = SlotAssignment.Classic54Default,
            IsAnimated = false,
            Width = 184,
            Height = 104,
        };
        Host(card, () =>
        {
            DisplayInfo main = services.DisplaysFake.Primary;
            Assert.Equal(LightStage.CornerScale, card.CurrentZoom, 6);
            RectI region = card.ShownRegion;
            Assert.Equal(main.Bounds.Right, region.Right);
            Assert.Equal(main.Bounds.Top, region.Top);
            Assert.True(region.Width < main.Bounds.Width / 3 && region.Height < main.Bounds.Height / 3, $"Shown region {region}.");

            // The exact desktop layout: the corner and the ends of the top and right strips, each bulb well over 12 px.
            LightsLayout layout = Assert.IsType<LightsLayout>(card.CurrentLayout);
            BulbPlacement[] shown = [.. layout.Placements.Where(p => p.Bounds.Intersect(region) == p.Bounds)];
            Assert.True(shown.Length >= 6, $"Only {shown.Length} bulbs in the corner.");
            Assert.All(shown, p => Assert.True(p.Bounds.Width * card.CurrentZoom >= 12, $"A bulb of {p.Bounds.Width * card.CurrentZoom:0.0} px."));

            // Any corner can be asked for, and the whole display.
            card.ZoomCorner = Corner.TopLeft;
            UiHarness.Pump(TimeSpan.FromMilliseconds(50));
            Assert.Equal(main.Bounds.Left, card.ShownRegion.Left);
            card.Zoom = StageZoom.Fit;
            UiHarness.Pump(TimeSpan.FromMilliseconds(50));
            Assert.True(card.CurrentZoom < 0.1);
            Assert.Equal(main.Bounds, card.ShownRegion);
        });
    });

    [Fact]
    public void HomeMagnifiesACornerOfEachDisplayWithLights() => ui.Run(() =>
    {
        using var services = Ready();
        var stage = new LightStage { Services = services, ShowAllDisplays = true, ShowStatusOverlays = true, Width = 900, Height = 214 };
        Host(stage, () =>
        {
            int enabled = services.Lights.Scene.Displays.Count(d => d.Enabled);
            Assert.True(enabled > 0);
            Assert.True(stage.CurrentZoom < 0.25, $"Fit zoom {stage.CurrentZoom}.");
            Assert.Equal(enabled, stage.Lenses.Count);
            Rect area = stage.ScreenArea;
            area.Inflate(1, 1);
            Assert.All(stage.Lenses, lens =>
            {
                Assert.True(lens.Zoom >= LightStage.CornerScale);
                Assert.True(area.Contains(lens.Area), $"Lens {lens.Area} outside {stage.ScreenArea}.");
            });

            stage.Zoom = StageZoom.Fit;
            UiHarness.Pump(TimeSpan.FromMilliseconds(50));
            Assert.Empty(stage.Lenses);
        });
    });

    [Fact]
    public void TryOnZoomsIntoItsTargetAndShowsInTheSamplesAndCorners() => WithPage((services, page) =>
    {
        ArrangementEditor editor = page.Editor;
        SlotAssignment real = services.Settings.Current.Current.Arrangement;
        SlotAssignment tried = SlotAssignment.Empty
            .WithEdge(Side.Top, [CandyCanes]).WithEdge(Side.Bottom, [CandyCanes]).WithEdge(Side.Left, [CandyCanes]).WithEdge(Side.Right, [CandyCanes])
            .WithCorner(Corner.TopLeft, CandyCanes);
        Assert.NotEqual(real.GetCorner(Corner.TopLeft), CandyCanes);

        editor.ShowTryOn(tried, "Trying On: Candy Canes - the whole frame");
        UiHarness.Pump(TimeSpan.FromMilliseconds(100));
        Assert.Equal(StageZoom.Corner, editor.Stage.Zoom);
        Assert.Equal(Corner.TopLeft, editor.Stage.ZoomCorner);
        Assert.Equal(LightStage.CornerScale, editor.Stage.CurrentZoom, 6);
        LightStrip[] samples = [.. UiHarness.Descendants<LightStrip>(editor)];
        Assert.Equal(4, samples.Length);
        Assert.All(samples, s => Assert.Same(tried, s.Arrangement));
        ArrangementChip topLeft = UiHarness.Descendants<ArrangementChip>(editor).Single(c => c.Slot == CellSlot.TopLeft);
        Assert.Equal(CandyCanes, topLeft.TryOnBulbId);
        Assert.True(topLeft.IsTryingOn);
        if (UiHarness.SnapshotFolder is { } folder)
        {
            UiHarness.Pump(TimeSpan.FromMilliseconds(400));
            UiHarness.SavePng(UiHarness.Render(page, dark: false), Path.Combine(folder, "BulbFactory-tryon-light.png"));
        }

        // The try-on of a box zooms into that box's corner.
        Assert.Equal(Corner.BottomRight, ArrangementEditor.TryOnCorner(ArrangementTarget.ForCorner(Corner.BottomRight)));
        Assert.Equal(Corner.TopRight, ArrangementEditor.TryOnCorner(ArrangementTarget.ForPlus(Side.Right)));
        Assert.Equal(Corner.BottomLeft, ArrangementEditor.TryOnCorner(ArrangementTarget.ForChip(Side.Bottom, 0)));

        editor.ShowTryOn(null, null);
        UiHarness.Pump(TimeSpan.FromMilliseconds(100));
        Assert.Equal(StageZoom.Auto, editor.Stage.Zoom);
        Assert.True(editor.Stage.CurrentZoom < LightStage.CornerScale);
        Assert.All(samples, s => Assert.Equal(real, s.Arrangement));
        Assert.Null(topLeft.TryOnBulbId);
        Assert.False(topLeft.IsTryingOn);
    });

    private static UiTestServices Ready()
    {
        var services = new UiTestServices();
        services.IndexTask.Wait(TimeSpan.FromSeconds(120));
        return services;
    }

    private static void Host(FrameworkElement element, Action test)
    {
        var window = new Window { Content = new Grid { Children = { element } } };
        UiHarness.ApplyAppResources(window, dark: true);
        UiHarness.PlaceOffScreen(window, 1000, 400);
        window.Show();
        try
        {
            UiHarness.Pump(TimeSpan.FromMilliseconds(150));
            test();
        }
        finally
        {
            window.Close();
        }
    }

    private void WithPage(Action<UiTestServices, BulbFactoryPage> test) => ui.Run(() =>
    {
        using UiTestServices services = Ready();
        var window = new SettingsWindow(services);
        UiHarness.ApplyAppResources(window, dark: false);
        UiHarness.PlaceOffScreen(window, 1240, 800);
        window.Show();
        try
        {
            window.ShowPage(SettingsPageId.BulbFactory);
            UiHarness.Pump(TimeSpan.FromMilliseconds(200));
            test(services, (BulbFactoryPage)window.GetPage(SettingsPageId.BulbFactory));
        }
        finally
        {
            window.Close();
        }
    }, TimeSpan.FromMinutes(3));
}
