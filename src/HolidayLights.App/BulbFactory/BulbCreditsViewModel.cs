using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using HolidayLights.Core.Bulbs.Writing;

namespace HolidayLights.App.BulbFactory;

/// <summary>
/// "Bulb Credits" (PRODUCT-SPEC 3.3.3): the bulb's preview animation (2x on a night well when it fits), the 5.4 notice,
/// "Author" and "Copyright Information" verbatim (selectable plain text), and where the bulb comes from, with
/// "Show in Folder" for My Bulbs.
/// </summary>
/// <remarks>UI thread; the preview frames are decoded on the thread pool.</remarks>
internal sealed class BulbCreditsViewModel : ObservableObject
{
    /// <summary>The largest picture size shown, in DIP.</summary>
    private const double MaxPictureWidth = 400;

    private const double MaxPictureHeight = 220;

    private readonly IBulb bulb;
    private readonly IShellOperations shell;
    private IReadOnlyList<ImageSource> frames = [];
    private int frameIndex;

    /// <summary>Creates the credits of a bulb.</summary>
    /// <param name="bulb">The bulb.</param>
    /// <param name="shell">Opens Explorer ("Show in Folder").</param>
    public BulbCreditsViewModel(IBulb bulb, IShellOperations shell)
    {
        this.bulb = bulb;
        this.shell = shell;
        // East Asian credits (Shift-JIS, CP949) read as written, for every copy of the file (PO decision 4; CreditText).
        (Author, Copyright) = bulb.Origin == BulbOrigin.BuiltIn
            ? (bulb.Author, bulb.Copyright)
            : HolidayLights.Core.Bulbs.CreditText.Decode(bulb.Author, bulb.Copyright);
        Source = bulb.Origin switch
        {
            BulbOrigin.BuiltIn => BulbFactoryStrings.BuiltInSource,
            BulbOrigin.BundledAddOn => string.Format(CultureInfo.CurrentCulture, BulbFactoryStrings.AddOnSourceFormat, Path.GetFileName(bulb.FilePath)),
            _ => string.Format(CultureInfo.CurrentCulture, BulbFactoryStrings.MyBulbSourceFormat, bulb.FilePath),
        };
        ShowInFolderCommand = new RelayCommand(() => shell.ShowInFolder(bulb.FilePath!), () => CanShowInFolder);
        SizeI size = bulb.GetCellSize(CellSlot.Preview, 0, 0);
        double zoom = size.Width * 2 <= MaxPictureWidth && size.Height * 2 <= MaxPictureHeight
            ? 2
            : Math.Min(1, Math.Min(MaxPictureWidth / Math.Max(size.Width, 1), MaxPictureHeight / Math.Max(size.Height, 1)));
        PictureWidth = size.Width * zoom;
        PictureHeight = size.Height * zoom;
    }

    /// <summary>The caption: the bulb's name (5.4).</summary>
    public string Title => bulb.Name;

    /// <summary>The 5.4 notice.</summary>
    public string Notice => BulbFactoryStrings.ArtNotice;

    /// <summary>The author field, verbatim with its line breaks.</summary>
    public string Author { get; }

    /// <summary>The copyright field, verbatim.</summary>
    public string Copyright { get; }

    /// <summary>"Built-in bulb from Holiday Lights 5.4", "Add-on bulb included with Holiday Lights - X.bul" or "My Bulb - path".</summary>
    public string Source { get; }

    /// <summary>True for My Bulbs: "Show in Folder" is offered.</summary>
    public bool CanShowInFolder => bulb.Origin == BulbOrigin.UserAddOn && bulb.FilePath is not null;

    /// <summary>"Show in Folder".</summary>
    public RelayCommand ShowInFolderCommand { get; }

    /// <summary>The frame shown now, or null while decoding.</summary>
    public ImageSource? CurrentFrame => frames.Count == 0 ? null : frames[frameIndex % frames.Count];

    /// <summary>Width of the picture in DIP.</summary>
    public double PictureWidth { get; }

    /// <summary>Height of the picture in DIP.</summary>
    public double PictureHeight { get; }

    /// <summary>Decodes the preview animation's frames on the thread pool.</summary>
    /// <returns>A task that completes when the picture shows.</returns>
    public async Task LoadAsync()
    {
        IBulb source = bulb;
        frames = await Task.Run(() =>
        {
            int count = source.GetAnimation(CellSlot.Preview, 0).FrameCount;
            return (IReadOnlyList<ImageSource>)[.. Enumerable.Range(0, count).Select(phase => PixelBitmaps.ToBitmap(source.GetCell(CellSlot.Preview, 0, phase).Image))];
        });
        OnPropertyChanged(nameof(CurrentFrame));
    }

    /// <summary>One flash step: the next frame.</summary>
    public void Tick()
    {
        if (frames.Count > 1)
        {
            frameIndex = (frameIndex + 1) % frames.Count;
            OnPropertyChanged(nameof(CurrentFrame));
        }
    }

}
