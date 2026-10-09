using System.Windows;
using HolidayLights.App.Preview;
using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.Ui;

/// <summary>The strips of lights in the Bulb List tiles: every bulb shows up, however wide its art.</summary>
[Collection(nameof(WpfCollection))]
public sealed class LightStripTests(UiThread ui)
{
    [Fact]
    public void ABulbWiderThanItsTileIsDrawnSmallerInsteadOfNotAtAll()
    {
        ui.Run(() =>
        {
            using var services = new UiTestServices();
            services.IndexTask.Wait(TimeSpan.FromSeconds(120));
            string wide = BulbIds.AddOn("2002");
            Assert.True(services.Bulbs.TryGetBulb(wide, out IBulb? bulb));
            Assert.Equal(50, bulb.GetCellSize(CellSlot.Top, 0, 0).Width);

            // A Bulb List tile: 96 x 56 DIP at 150 %, art at most twice its size (100 art pixels would need 150 device pixels).
            var strip = new LightStrip { SingleBulbId = wide, Side = Side.Top, BulbHeight = 56, MaxArtScale = 2, FallbackToListPreview = true };
            StripModel model = StripModel.Create(services, strip, 1.5, new Size(96, 56));
            BulbPlacement placement = Assert.Single(model.Layout.Placements);
            Assert.InRange(placement.Bounds.Width, 100, 144);

            // A bulb that fits keeps the size its height allows.
            string standardId = BulbIds.BuiltIn("standard-bulbs");
            Assert.True(services.Bulbs.TryGetBulb(standardId, out IBulb? standard));
            SizeI cell = standard.GetCellSize(CellSlot.Top, 0, 0);
            double scale = Math.Min(56 * 1.5 / cell.Height, 2 * 1.5);
            var fitting = new LightStrip { SingleBulbId = standardId, Side = Side.Top, BulbHeight = 56, MaxArtScale = 2 };
            StripModel fittingModel = StripModel.Create(services, fitting, 1.5, new Size(96, 56));
            Assert.NotEmpty(fittingModel.Layout.Placements);
            Assert.All(fittingModel.Layout.Placements, p => Assert.Equal(ArtScale.Scale(cell.Width, scale), p.Bounds.Width));
        }, TimeSpan.FromMinutes(2));
    }
}
