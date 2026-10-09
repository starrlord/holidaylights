using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HolidayLights.App.Controls;
using HolidayLights.App.Gallery;
using HolidayLights.App.Settings;
using HolidayLights.App.Settings.Pages;
using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.Ui;

/// <summary>
/// Bulb Factory in a real Settings window (off-screen, closed at once): the acceptance scenarios 5 (one bulb on the
/// whole frame), 6 (keyboard: "+" of the left edge, a bulb, Undo) and 9 (Remove Bulb), the frame editor's keyboard
/// model and names, the Bulb List's Show filter, search and Restore All, and opening or dropping files.
/// </summary>
[Collection(nameof(WpfCollection))]
public sealed class BulbFactoryPageTests(UiThread ui)
{
    private static readonly string CandyCanes = BulbIds.BuiltIn("candy-canes");
    private static readonly string SnowFamily = BulbIds.BuiltIn("snow-family");

    [Fact]
    public void OneBulbOnTheWholeFrameAndUndo()
    {
        WithPage((services, window, page) =>
        {
            SlotAssignment before = services.Settings.Current.Current.Arrangement;
            Assert.Equal(ArrangementTarget.WholeFrame, page.Editor.Target);
            page.Run(new BulbActionRequest(BulbAction.Use, [CandyCanes]));
            SlotAssignment after = services.Settings.Current.Current.Arrangement;
            foreach (Side side in CellSlots.Sides)
            {
                Assert.Equal([CandyCanes], after.GetEdge(side));
            }

            foreach (Corner corner in CellSlots.Corners)
            {
                Assert.Equal(CandyCanes, after.GetCorner(corner));
            }

            Assert.True(window.Snackbar.IsShown);
            Assert.Equal("Using Candy Canes on the whole frame.", window.Snackbar.Message);
            window.UndoLast();
            Assert.Equal(before, services.Settings.Current.Current.Arrangement);
        });
    }

    [Fact]
    public void ThePlusOfAnEdgeAddsAndMovesTheTargetToTheNewChip()
    {
        WithPage((services, window, page) =>
        {
            SlotAssignment before = services.Settings.Current.Current.Arrangement;
            int count = before.GetEdge(Side.Left).Count;
            page.Editor.Target = ArrangementTarget.ForPlus(Side.Left);
            page.Run(new BulbActionRequest(BulbAction.Use, [SnowFamily]));
            IReadOnlyList<string> left = services.Settings.Current.Current.Arrangement.GetEdge(Side.Left);
            Assert.Equal(count + 1, left.Count);
            Assert.Equal(SnowFamily, left[^1]);
            Assert.Equal(ArrangementTarget.ForChip(Side.Left, count), page.Editor.Target);
            Assert.Equal("the 3rd bulb on the left edge", page.Editor.Target.Sentence(services.Settings.Current.Current.Arrangement));
            window.UndoLast();
            Assert.Equal(before, services.Settings.Current.Current.Arrangement);
        });
    }

    [Fact]
    public void RemovingAMyBulbsBulbUpdatesTheEdgesAndUndoBringsBothBack()
    {
        WithPage((services, window, page) =>
        {
            Directory.CreateDirectory(services.Paths.MyBulbsFolder);
            string mine = Path.Combine(services.Paths.MyBulbsFolder, "My Bulb.bul");
            File.Copy(Directory.EnumerateFiles(services.Paths.BundledBulbsFolder, "*.bul").First(), mine);
            string id = services.Bulbs.LoadUserBulb(mine)?.Id ?? throw new InvalidOperationException("The copied bulb did not load.");
            Assert.Equal(BulbOrigin.UserAddOn, services.Bulbs.TryGetInfo(id, out BulbInfo? info) ? info.Origin : BulbOrigin.BuiltIn);
            services.Settings.Update(
                s => s with { Current = s.Current with { Arrangement = s.Current.Arrangement.WithEdge(Side.Top, [id]).WithEdge(Side.Bottom, [SnowFamily, id]) } },
                SettingsChange.Internal);
            page.Run(new BulbActionRequest(BulbAction.Remove, [id]));
            SlotAssignment removed = services.Settings.Current.Current.Arrangement;
            Assert.DoesNotContain(id, removed.GetEdge(Side.Top));
            Assert.Equal([SnowFamily], removed.GetEdge(Side.Bottom));
            Assert.False(services.Bulbs.TryGetBulb(id, out _));
            Assert.Single(services.Holding.Items);
            Assert.StartsWith("Removed ", window.Snackbar.Message, StringComparison.Ordinal);
            window.UndoLast();
            UiHarness.Pump(TimeSpan.FromMilliseconds(100));
            SlotAssignment restored = services.Settings.Current.Current.Arrangement;
            Assert.Equal([id], restored.GetEdge(Side.Top));
            Assert.Equal([SnowFamily, id], restored.GetEdge(Side.Bottom));
            Assert.True(services.Bulbs.TryGetBulb(id, out _));

            // The catalog raises its events on its own thread; the chips' thumbnails of the bulb must take them there.
            UiHarness.Pump(TimeSpan.FromMilliseconds(200));
            Assert.DoesNotContain(services.Log.Entries, e => e.Level == AppLogLevel.Error && e.Source == "Bulbs.Catalog");
        });
    }

    [Fact]
    public void FrameEditorNamesItsBoxesForScreenReaders()
    {
        WithPage((services, window, page) =>
        {
            services.Settings.Update(
                s => s with { Current = s.Current with { Arrangement = SlotAssignment.Empty.WithEdge(Side.Top, [BulbIds.BuiltIn("standard-bulbs"), SnowFamily]).WithCorner(Corner.TopLeft, BulbIds.BuiltIn("jolly-holly")) } },
                SettingsChange.Internal);
            UiHarness.Pump(TimeSpan.FromMilliseconds(50));
            ArrangementChip[] chips = [.. UiHarness.Descendants<ArrangementChip>(page.Editor)];
            string[] names = [.. chips.Select(System.Windows.Automation.AutomationProperties.GetName)];
            Assert.Contains("Standard Bulbs, position 1 of 2 on the top edge.", names);
            Assert.Contains("Snow Family, position 2 of 2 on the top edge.", names);
            Assert.Contains("Add a bulb to the top edge, 2 of 6 used.", names);
            Assert.Contains("Top-left corner: Jolly Holly", names);
            Assert.Contains("Top-right corner: empty", names);
            string[] groups = [.. UiHarness.Descendants<AutomationGroup>(page.Editor).Select(System.Windows.Automation.AutomationProperties.GetName)];
            Assert.Contains("Top edge, 2 of 6 bulb types: Standard Bulbs, Snow Family.", groups);
            Assert.Contains("Left edge, 0 of 6 bulb types.", groups);
            Assert.StartsWith("Top: Standard Bulbs, Snow Family.", System.Windows.Automation.AutomationProperties.GetHelpText(page.Editor), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ArrowsMoveBetweenBoxesAndDeleteRemoves()
    {
        WithPage((services, window, page) =>
        {
            // The roving tab stop is the target's item (keyboard focus itself depends on window activation).
            ArrangementChip Focused() => UiHarness.Descendants<ArrangementChip>(page.Editor).Single(c => c.IsTabStop);
            page.Editor.Target = ArrangementTarget.ForCorner(Corner.TopLeft);
            page.Editor.FocusTarget();
            Assert.Equal(ArrangementTarget.ForCorner(Corner.TopLeft), page.Editor.Target);
            UiHarness.Press(Focused(), Key.Right);
            Assert.Equal(ArrangementTarget.ForChip(Side.Top, 0), page.Editor.Target);
            UiHarness.Press(Focused(), Key.Left);
            UiHarness.Press(Focused(), Key.Down);
            Assert.Equal(ArrangementTarget.ForChip(Side.Left, 0), page.Editor.Target);
            UiHarness.Press(Focused(), Key.End);
            Assert.Equal(ArrangementTarget.ForPlus(Side.Left), page.Editor.Target);
            UiHarness.Press(Focused(), Key.Up);
            int before = services.Settings.Current.Current.Arrangement.GetEdge(Side.Left).Count;
            UiHarness.Press(Focused(), Key.Delete);
            Assert.Equal(before - 1, services.Settings.Current.Current.Arrangement.GetEdge(Side.Left).Count);
            bool picked = false;
            page.Editor.PickBulbRequested += (_, _) => picked = true;
            UiHarness.Press(Focused(), Key.Enter);
            Assert.True(picked);
        });
    }

    [Fact]
    public void TheBulbListFiltersSearchesAndSelects()
    {
        WithPage((services, window, page) =>
        {
            BulbList list = page.List;
            int total = services.Bulbs.Query(new BulbQuery()).Count;
            Assert.Equal(total, list.Items.Count);
            Assert.Equal(ShowOption.AllKey, list.CurrentOption.Key);
            Assert.True(list.Select(SnowFamily));
            Assert.Equal(SnowFamily, list.SelectedItem?.Id);
            ComboBox show = UiHarness.Descendants<ComboBox>(list).First(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Show");
            show.SelectedItem = show.Items.OfType<ComboBoxItem>().First(i => i.Tag is ShowOption { Key: "builtin" });
            Assert.Equal(49, list.Items.Count);
            SearchBox search = UiHarness.Descendants<SearchBox>(list).First();
            search.Clear();
            search.Text = "snow";
            UiHarness.Pump(TimeSpan.FromMilliseconds(300));
            Assert.NotEmpty(list.Items);
            Assert.All(list.Items, i => Assert.Equal(BulbOrigin.BuiltIn, i.Info.Origin));
            Assert.True(list.Select(services.Bulbs.Query(new BulbQuery { Filter = BulbFilter.AddOn }).First().Id));
            Assert.Equal(ShowOption.AllKey, list.CurrentOption.Key);
            Assert.Equal("", search.Text);
        });
    }

    [Fact]
    public void OpeningABulbFileThatIsAlreadyPresentSelectsThatBulb()
    {
        WithPage((services, window, page) =>
        {
            string copy = Path.Combine(services.Paths.LocalRoot, "Copy of a bundled bulb.bul");
            File.Copy(Directory.EnumerateFiles(services.Paths.BundledBulbsFolder, "*.bul").First(), copy);
            string expected = services.Bulbs.FindByContent(copy) ?? throw new InvalidOperationException("The copy matches no bulb.");
            // As in the app, the request runs inside the dispatcher loop (its awaits resume on the UI thread).
            window.Dispatcher.InvokeAsync(() => page.HandleRequest(new OpenFilesRequest([copy])));
            for (int i = 0; i < 200 && !page.MessageBar.IsOpen; i++)
            {
                UiHarness.Pump(TimeSpan.FromMilliseconds(25));
            }

            Assert.Equal(BulbImportTexts.AlreadyPresent(page.Editor.NameOf(expected)), page.MessageBar.Message);
            Assert.Equal(expected, page.List.SelectedItem?.Id);
            Assert.False(Directory.Exists(services.Paths.MyBulbsFolder) && Directory.EnumerateFiles(services.Paths.MyBulbsFolder).Any());
        });
    }

    [Fact]
    public void OtherFilesDroppedOnTheWindowAreRefusedWithTheExplanation()
    {
        WithPage((services, window, page) =>
        {
            window.AddFiles([Path.Combine(services.Paths.LocalRoot, "Notes.txt")]);
            Assert.True(window.WindowInfoBar.IsOpen);
            Assert.Equal(DroppedFiles.UnsupportedText, window.WindowInfoBar.Message);
        });
    }

    [Fact]
    public void RestoreAllListsEveryRemovedBulbAgainAsOneStep()
    {
        WithPage((services, window, page) =>
        {
            string[] addOns = [.. services.Bulbs.Query(new BulbQuery { Filter = BulbFilter.AddOn }).Take(2).Select(b => b.Id).Order(StringComparer.Ordinal)];
            Assert.All(addOns, id => Assert.StartsWith(BulbIds.AddOnPrefix, id, StringComparison.Ordinal));
            services.Bulbs.Hide(addOns[0]);
            services.Bulbs.Hide(addOns[1]);
            BulbList list = page.List;
            ComboBox show = UiHarness.Descendants<ComboBox>(list).First(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Show");
            ComboBoxItem? removed = null;
            for (int i = 0; i < 80 && removed is null; i++)
            {
                UiHarness.Pump(TimeSpan.FromMilliseconds(25));
                removed = show.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag is ShowOption { Key: ShowOption.RemovedKey });
            }

            show.SelectedItem = removed ?? throw new InvalidOperationException("Removed Bulbs is not offered.");
            UiHarness.Pump(TimeSpan.FromMilliseconds(100));
            Assert.Equal(addOns, list.Items.Select(i => i.Id).Order(StringComparer.Ordinal));
            Assert.Equal(Visibility.Visible, list.RestoreAllButton.Visibility);
            if (UiHarness.SnapshotFolder is { } folder)
            {
                UiHarness.SavePng(UiHarness.Render(list, dark: false), Path.Combine(folder, "BulbList-Removed-light.png"));
            }

            list.RestoreAllButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            UiHarness.Pump(TimeSpan.FromMilliseconds(100));
            Assert.Empty(services.Bulbs.Query(new BulbQuery { Filter = BulbFilter.Removed }));
            Assert.Equal("Restore 2 bulbs", window.Session.History.UndoDescription);
            window.UndoLast();
            Assert.Equal(2, services.Bulbs.Query(new BulbQuery { Filter = BulbFilter.Removed }).Count);
        });
    }

    private void WithPage(Action<UiTestServices, SettingsWindow, BulbFactoryPage> test) => ui.Run(() =>
    {
        using var services = new UiTestServices();
        services.IndexTask.Wait(TimeSpan.FromSeconds(120));
        var window = new SettingsWindow(services);
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
            window.Close();
        }
    }, TimeSpan.FromMinutes(3));
}
