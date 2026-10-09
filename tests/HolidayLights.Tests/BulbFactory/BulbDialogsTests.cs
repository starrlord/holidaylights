using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HolidayLights.App.BulbFactory;
using HolidayLights.App.Controls;
using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Bulbs.Writing;
using HolidayLights.Tests.Ui.Fakes;
using static HolidayLights.Tests.BulbFactory.WritingTestData;

namespace HolidayLights.Tests.BulbFactory;

/// <summary>Edit Categories, New Category, Bulb Credits, Export Bulb File and the GIF-to-bulb entry point.</summary>
public sealed class BulbDialogsTests : IDisposable
{
    private readonly EditorTestBed bed = new();

    public void Dispose() => bed.Dispose();

    [Fact]
    public void EditCategories_StoresAnOverrideInSettingsAndNeverTouchesAFile()
    {
        Assert.True(bed.Catalog.TryGetInfo("builtin:standard-bulbs", out BulbInfo? info));
        bed.Prompts.NewCategories.Enqueue("Classics");
        var dialog = new EditCategoriesViewModel(bed.Catalog, info, bed.Prompts);
        Assert.Equal("Edit Categories - Standard Bulbs", dialog.Title);
        Assert.Equal(info.Categories.Order(), dialog.Categories.Items.Where(i => i.IsChecked).Select(i => i.Name).Order());

        dialog.NewCategoryCommand.Execute(null);
        dialog.SaveCommand.Execute(null);

        Assert.True(dialog.IsSaved);
        Assert.Equal([.. info.Categories, "Classics"], bed.Settings.Current.Bulbs.CategoryOverrides["builtin:standard-bulbs"]);
        Assert.Single(bed.Settings.History);
    }

    [Theory]
    [InlineData("Christmas", true)]
    [InlineData(" - ", false)]
    public void NewCategory_EnablesOkForALetterOrDigit(string name, bool enabled) =>
        Assert.Equal(enabled, new NewCategoryViewModel { Name = name }.CanCreate);

    [Fact]
    public async Task Credits_ShowTheBuiltInSourceAndAnimateThePreview()
    {
        Assert.True(bed.Catalog.TryGetBulb("builtin:standard-bulbs", out IBulb? bulb));
        var credits = new BulbCreditsViewModel(bulb, new FakeShell());

        await credits.LoadAsync();

        Assert.Equal("Standard Bulbs", credits.Title);
        Assert.Equal("Built-in bulb from Holiday Lights 5.4", credits.Source);
        Assert.Equal(bulb.Author, credits.Author);
        Assert.False(credits.CanShowInFolder);
        Assert.NotNull(credits.CurrentFrame);
        Assert.Equal("This art may not be used for other purposes without the author's permission.", credits.Notice);
    }

    [Theory]
    [InlineData("Doru.bul", 932)]
    [InlineData("YesMan.bul", 949)]
    public void Credits_ReadTheTwoEastAsianArtistsInTheirCodePage(string fileName, int codePage)
    {
        string path = Path.Combine(BundledBulbsFolder, fileName);
        IBulb bulb = AddOnBulbs.FromFile(BulFile.Read(path), BulbIds.AddOn(Path.GetFileNameWithoutExtension(fileName)), BulbOrigin.BundledAddOn, "credits-test:" + fileName);
        byte[] header = File.ReadAllBytes(path)[..0x15C];
        Encoding encoding = CodePagesEncodingProvider.Instance.GetEncoding(codePage)!;

        var credits = new BulbCreditsViewModel(bulb, new FakeShell());

        Assert.Equal(encoding.GetString(header.AsSpan(0x10C, 80).TrimEnd((byte)0)), credits.Author);
        Assert.Equal(encoding.GetString(header.AsSpan(0xBC, 80).TrimEnd((byte)0)), credits.Copyright);
        Assert.DoesNotContain(credits.Author, c => c is >= '\u0080' and <= 'ÿ');
        Assert.Equal($"Add-on bulb included with Holiday Lights - {fileName}", credits.Source);
    }

    [Fact]
    public void Credits_OfAMyBulb_OfferShowInFolder()
    {
        string id = bed.AddMyBulb(Document(NumberedGif(1), "Star"));
        Assert.True(bed.Catalog.TryGetBulb(id, out IBulb? bulb));
        var shell = new FakeShell();
        var credits = new BulbCreditsViewModel(bulb, shell);

        credits.ShowInFolderCommand.Execute(null);

        Assert.Equal($"My Bulb - {bulb.FilePath}", credits.Source);
        Assert.True(credits.CanShowInFolder);
        Assert.Equal([bulb.FilePath!], shell.ShownFiles);
    }

    [Fact]
    public void CreateBulbFromGif_MakesAMyBulbOrSaysWhyNot()
    {
        var dialogs = new BulbFactoryDialogs(new FakeAppServices(bed));

        GifBulbCreation made = dialogs.CreateBulbFromGif(bed.Scratch("Snow Globe.gif", NumberedGif(5)));
        GifBulbCreation broken = dialogs.CreateBulbFromGif(bed.Scratch("Broken.gif", NumberedGif(5)[..20]));
        GifBulbCreation picture = dialogs.CreateBulbFromGif(bed.Scratch("Photo.jpg", [1, 2, 3]));

        Assert.True(made.Succeeded);
        Assert.Equal("user:Snow Globe", made.BulbId);
        Assert.Equal(Path.Combine(bed.Paths.MyBulbsFolder, "Snow Globe.bul"), made.FilePath);
        Assert.Equal(("Cannot Import GIF File", "Sorry, this GIF file can't be imported. It may be damaged in some way."), (broken.ErrorTitle, broken.ErrorText));
        Assert.False(broken.Succeeded);
        Assert.Equal("Problem Importing File", picture.ErrorTitle);
        Assert.False(dialogs.HasUnsavedChanges);
    }

    [Fact]
    public async Task ConfirmDiscard_IsTrueWhenNoEditorIsOpen() =>
        Assert.True(await new BulbFactoryDialogs(new FakeAppServices(bed)).ConfirmDiscardAsync());

    [Fact]
    public void Export_CopiesThroughATemporaryFile()
    {
        string source = bed.Scratch("Star.bul", [1, 2, 3]);
        string target = bed.Scratch("Copy.bul", [9]);

        BulbExport.Copy(source, target);
        BulbExport.Copy(source, source);

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(target));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(source));
        Assert.False(File.Exists(target + ".tmp"));
        string folder = Path.GetDirectoryName(source)!;
        Assert.ThrowsAny<IOException>(() => BulbExport.Copy(Path.Combine(folder, "Missing.bul"), target));
        Assert.False(File.Exists(target + ".tmp"));
    }

    [Fact]
    public async Task Windows_BuildFromTheirXaml()
    {
        string id = bed.AddMyBulb(Document(NumberedGif(1), "Star"));
        BulbEditorSession session = await BulbEditorSession.OpenAsync(bed.Environment, id, Path.Combine(bed.Paths.MyBulbsFolder, "Star.bul"));
        Assert.True(bed.Catalog.TryGetInfo(id, out BulbInfo? info));
        Assert.True(bed.Catalog.TryGetBulb(id, out IBulb? bulb));

        Shared.StaThread.Run(() =>
        {
            Window[] windows =
            [
                new BulbEditingWindow(session, ["Christmas"], TimeSpan.FromMilliseconds(300), () => { }),
                new EditCategoriesWindow(bed.Catalog, info),
                new NewCategoryWindow(),
                new BulbCreditsWindow(new BulbCreditsViewModel(bulb, new FakeShell()), TimeSpan.FromMilliseconds(300)),
            ];
            foreach (Window window in windows)
            {
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(980, 640));
                content.Arrange(new Rect(0, 0, 980, 640));
                Assert.True(content.ActualWidth > 0, window.GetType().Name);
            }
        });
    }

    /// <summary>
    /// The 5.4 group boxes "Bulb &amp;Information" and "Animation &amp;Frames" keep their access keys, which go where 5.4's
    /// went (Bulb Name, the slot combo box), clash with no field label, and sit where the Card draws its own header.
    /// </summary>
    [Fact]
    public async Task Editing_GroupHeadersKeepThe54AccessKeys()
    {
        const string Description = "The text below appears in the bulb list and bulb credits window.";
        string id = bed.AddMyBulb(Document(NumberedGif(1), "Star"));
        BulbEditorSession session = await BulbEditorSession.OpenAsync(bed.Environment, id, Path.Combine(bed.Paths.MyBulbsFolder, "Star.bul"));

        Shared.StaThread.Run(() =>
        {
            var window = new BulbEditingWindow(session, ["Christmas"], TimeSpan.FromMilliseconds(300), () => { });
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(980, 640));
            content.Arrange(new Rect(0, 0, 980, 640));
            UiHarness.Pump(TimeSpan.FromMilliseconds(50)); // label targets are ElementName bindings, bound after loading

            AccessText[] keys = [.. UiHarness.Descendants<AccessText>(content).Where(text => text.AccessKey != default)];
            Assert.Equal("ACDFINT", string.Concat(keys.Select(text => char.ToUpperInvariant(text.AccessKey)).Order()));
            Assert.Same(window.FindName("NameBox"), AccessKeyTarget(keys, "Bulb _Information"));
            Assert.Same(window.FindName("SlotBox"), AccessKeyTarget(keys, "Animation _Frames"));

            Card[] cards = [.. UiHarness.Descendants<Card>(content)];
            Assert.Equal(2, cards.Length);
            AutomationPeer information = UIElementAutomationPeer.CreatePeerForElement(cards[0]);
            Assert.Equal(("Bulb Information", Description), (information.GetName(), information.GetHelpText()));
            Assert.Equal("Animation Frames", UIElementAutomationPeer.CreatePeerForElement(cards[1]).GetName());

            AssertDrawnLikeTheCardHeader(cards[0], (Label)window.FindName("InformationHeading"), "Bulb Information", Description);
            AssertDrawnLikeTheCardHeader(cards[1], (Label)window.FindName("FramesHeading"), "Animation Frames", null);
        });
    }

    /// <summary>What an access key focuses: the target that its label gives AccessKeyManager.</summary>
    private static object? AccessKeyTarget(IEnumerable<AccessText> keys, string text)
    {
        AccessText key = Assert.Single(keys, candidate => candidate.Text == text);
        var pressed = new AccessKeyPressedEventArgs(key.AccessKey.ToString()) { RoutedEvent = AccessKeyManager.AccessKeyPressedEvent };
        key.RaiseEvent(pressed);
        return pressed.Target;
    }

    /// <summary>The heading, description and content sit where a Card with that Header and Description puts them.</summary>
    private static void AssertDrawnLikeTheCardHeader(Card card, Label heading, string header, string? description)
    {
        var reference = new Card
        {
            Header = header,
            Description = description,
            Content = new Border { Height = 10 },
            UseLayoutRounding = card.UseLayoutRounding,
        };
        reference.Measure(new Size(card.ActualWidth, double.PositiveInfinity));
        reference.Arrange(new Rect(0, 0, card.ActualWidth, reference.DesiredSize.Height));
        TextBlock referenceHeader = UiHarness.Descendants<TextBlock>(reference).First(text => text.Text == header);
        DependencyObject parent = LogicalTreeHelper.GetParent(heading);
        var body = (FrameworkElement)(parent as DockPanel ?? (DockPanel)LogicalTreeHelper.GetParent(parent)).Children[^1];

        Assert.Equal(Top(referenceHeader, reference), Top(heading, card), 3);
        Assert.Equal(referenceHeader.ActualHeight, heading.ActualHeight, 3);
        Assert.Equal(Top((Border)reference.Content, reference), Top(body, card), 3);
    }

    private static double Top(Visual element, Visual ancestor) => element.TransformToAncestor(ancestor).Transform(default).Y;
}
