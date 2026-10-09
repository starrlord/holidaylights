using System.Windows.Controls;
using System.Windows.Input;
using HolidayLights.App.Controls;
using HolidayLights.App.Gallery;
using HolidayLights.App.Settings;
using HolidayLights.App.Settings.Pages;
using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.Ui;

/// <summary>
/// The acceptance scenarios of PRODUCT-SPEC 7.5 that settings-ui owns, in a real Settings window (off-screen): 6 (keyboard
/// only, with the screen-reader announcements), 8 (Cancel keeps what was added and brings back what was removed) and 9
/// (a removed bulb goes to the Recycle Bin when Settings closes). Scenario 5 is in <see cref="BulbFactoryPageTests"/>.
/// </summary>
[Collection(nameof(WpfCollection))]
public sealed class AcceptanceScenarioTests(UiThread ui)
{
    private static readonly string SnowFamily = BulbIds.BuiltIn("snow-family");

    [Fact]
    public void KeyboardOnlyAddsSnowFamilyToTheLeftEdgeAndUndoRemovesIt()
    {
        WithBulbFactory((services, window, page) =>
        {
            SlotAssignment before = services.Settings.Current.Current.Arrangement;
            ArrangementChip Focused() => UiHarness.Descendants<ArrangementChip>(page.Editor).Single(c => c.IsTabStop);

            // The frame editor, arrows to the Left Edge "+", Enter: the Bulb List search picks a bulb for it.
            page.Editor.Target = ArrangementTarget.ForCorner(Corner.TopLeft);
            page.Editor.FocusTarget();
            UiHarness.Press(Focused(), Key.Down);
            UiHarness.Press(Focused(), Key.End);
            Assert.Equal(ArrangementTarget.ForPlus(Side.Left), page.Editor.Target);
            bool picked = false;
            page.Editor.PickBulbRequested += (_, _) => picked = true;
            UiHarness.Press(Focused(), Key.Enter);
            Assert.True(picked);

            // Type "snow", arrow to Snow Family, Enter: it is added at the end of the left edge, and announced.
            SearchBox search = UiHarness.Descendants<SearchBox>(page.List).First();
            search.Text = "snow";
            UiHarness.Pump(TimeSpan.FromMilliseconds(300));
            Assert.Contains(page.List.Items, i => i.Id == SnowFamily);
            Assert.True(page.List.Select(SnowFamily));
            ListBox grid = UiHarness.Descendants<ListBox>(page.List).First(l => l.IsVisible);
            UiHarness.Press(grid, Key.Enter);
            IReadOnlyList<string> left = services.Settings.Current.Current.Arrangement.GetEdge(Side.Left);
            Assert.Equal([.. before.GetEdge(Side.Left), SnowFamily], left);
            Assert.Contains("Snow Family", window.LastAnnouncement, StringComparison.Ordinal);

            // Ctrl+Z removes it again, and says so.
            window.UndoLast();
            Assert.Equal(before, services.Settings.Current.Current.Arrangement);
            Assert.StartsWith("Undone: ", window.LastAnnouncement, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void CancelKeepsAnAddedBulbAndBringsBackARemovedOne()
    {
        WithBulbFactory((services, window, page) =>
        {
            string removed = AddMyBulb(services, "Removed Star.bul");
            // A bundled bulb under another name (offset 0x1C) is a bulb nobody has yet.
            string added = Path.Combine(services.Paths.LocalRoot, "Added Star.bul");
            byte[] bytes = File.ReadAllBytes(Directory.EnumerateFiles(services.Paths.BundledBulbsFolder, "*.bul").ElementAt(1));
            bytes[0x1C] = (byte)'Z';
            File.WriteAllBytes(added, bytes);
            page.Run(new BulbActionRequest(BulbAction.Remove, [removed]));
            Assert.False(services.Bulbs.TryGetBulb(removed, out _));

            // As in the app, the request runs inside the dispatcher loop (its awaits resume on the UI thread).
            window.Dispatcher.InvokeAsync(() => page.HandleRequest(new OpenFilesRequest([added])));
            for (int i = 0; i < 200 && !window.Snackbar.Message.StartsWith("Added ", StringComparison.Ordinal); i++)
            {
                UiHarness.Pump(TimeSpan.FromMilliseconds(25));
            }

            string addedId = page.List.SelectedItem?.Id ?? throw new InvalidOperationException("The added bulb is not selected.");
            Assert.StartsWith(BulbIds.UserPrefix, addedId, StringComparison.Ordinal);

            window.Session.Cancel();
            UiHarness.Pump(TimeSpan.FromMilliseconds(100));
            Assert.True(services.Bulbs.TryGetBulb(removed, out _), "Cancel brings back what was removed.");
            Assert.True(services.Bulbs.TryGetBulb(addedId, out _), "Cancel keeps what was added.");
        });
    }

    [Fact]
    public void ClosingSettingsSendsTheRemovedBulbToTheRecycleBin()
    {
        WithBulbFactory((services, window, page) =>
        {
            string id = AddMyBulb(services, "Old Star.bul");
            string file = services.Bulbs.TryGetInfo(id, out BulbInfo? info) && info.FilePath is { } path ? path : throw new InvalidOperationException("No file.");
            page.Run(new BulbActionRequest(BulbAction.Remove, [id]));
            Assert.False(File.Exists(file));
            Assert.Single(services.Holding.Items);

            window.CloseKeepingChanges();
            Assert.Empty(services.Holding.Items);
            Assert.False(File.Exists(file));
        });
    }

    /// <summary>Copies a bundled bulb into My Bulbs and loads it.</summary>
    private static string AddMyBulb(UiTestServices services, string name)
    {
        Directory.CreateDirectory(services.Paths.MyBulbsFolder);
        string mine = Path.Combine(services.Paths.MyBulbsFolder, name);
        File.Copy(Directory.EnumerateFiles(services.Paths.BundledBulbsFolder, "*.bul").First(), mine);
        return services.Bulbs.LoadUserBulb(mine)?.Id ?? throw new InvalidOperationException("The copied bulb did not load.");
    }

    private void WithBulbFactory(Action<UiTestServices, SettingsWindow, BulbFactoryPage> test) => ui.Run(() =>
    {
        using var services = new UiTestServices();
        services.IndexTask.Wait(TimeSpan.FromSeconds(120));
        var window = new SettingsWindow(services);
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        UiHarness.ApplyAppResources(window, dark: false);
        UiHarness.PlaceOffScreen(window, 1240, 800);
        window.Show();
        try
        {
            window.ShowPage(SettingsPageId.BulbFactory);
            UiHarness.Pump(TimeSpan.FromMilliseconds(200));
            test(services, window, (BulbFactoryPage)window.GetPage(SettingsPageId.BulbFactory));
        }
        finally
        {
            if (!closed)
            {
                window.Close();
            }
        }
    }, TimeSpan.FromMinutes(3));
}
