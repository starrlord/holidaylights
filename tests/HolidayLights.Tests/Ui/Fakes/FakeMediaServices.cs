using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HolidayLights.Audio;

namespace HolidayLights.Tests.Ui.Fakes;

/// <summary>A Music Box engine that plays nothing: it records commands and reports a configurable state.</summary>
public sealed class FakeMusicDirector : IMusicDirector
{
    /// <inheritdoc />
    public event EventHandler? StateChanged;

    /// <inheritdoc />
    public MusicState State { get; private set; } = MusicState.Initial;

    /// <inheritdoc />
    public IMusicEventSource Events { get; } = new SilentEvents();

    /// <summary>The last policy applied.</summary>
    public MusicPolicy? Policy { get; private set; }

    /// <summary>Every command, e.g. "PlayNow bundled:Jingle Bells.mid".</summary>
    public List<string> Commands { get; } = [];

    /// <summary>Replaces the state and raises <see cref="StateChanged"/>.</summary>
    /// <param name="state">The state.</param>
    public void SetState(MusicState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void ApplyPolicy(MusicPolicy policy) => Policy = policy;

    /// <inheritdoc />
    public void PlayNow(string songId) => Commands.Add($"PlayNow {songId}");

    /// <inheritdoc />
    public void NextSong() => Commands.Add("NextSong");

    /// <inheritdoc />
    public void Previous() => Commands.Add("Previous");

    /// <inheritdoc />
    public void Pause() => Commands.Add("Pause");

    /// <inheritdoc />
    public void Resume() => Commands.Add("Resume");

    /// <inheritdoc />
    public void Seek(TimeSpan position) => Commands.Add($"Seek {position}");

    /// <inheritdoc />
    public void RetryNow() => Commands.Add("RetryNow");

    /// <inheritdoc />
    public IReadOnlyList<string> GetMidiOutputDevices() => ["Microsoft GS Wavetable Synth"];

    /// <inheritdoc />
    public Task StopAsync(TimeSpan fadeOut) => Task.CompletedTask;

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class SilentEvents : IMusicEventSource
    {
        public IMusicEventReader Subscribe(int capacity = 4096) => new SilentReader();
    }

    private sealed class SilentReader : IMusicEventReader
    {
        public int Read(Span<MusicEvent> destination) => 0;

        public void Dispose()
        {
        }
    }
}

/// <summary>The bundled screen saver pictures of the test output (the real library belongs to the screensaver owner).</summary>
public sealed class FakePictureLibrary : IPictureLibrary
{
    private readonly ISettingsStore settings;
    private readonly List<PictureInfo> all;

    /// <summary>Lists the bundled pictures.</summary>
    /// <param name="paths">The data paths.</param>
    /// <param name="settings">The settings (hidden pictures).</param>
    public FakePictureLibrary(DataPaths paths, ISettingsStore settings)
    {
        this.settings = settings;
        all = Directory.Exists(paths.BundledPicturesFolder)
            ? [.. Directory.EnumerateFiles(paths.BundledPicturesFolder, "*.bmp")
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(f => new PictureInfo(MediaIds.Bundled(Path.GetFileName(f)), Path.GetFileNameWithoutExtension(f), f, MediaOrigin.Bundled))]
            : [];
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IReadOnlyList<PictureInfo> Pictures => [.. all.Where(p => !settings.Current.Pictures.Hidden.Contains(p.Id, MediaIds.Comparer))];

    /// <inheritdoc />
    public void Start()
    {
    }

    /// <inheritdoc />
    public bool TryGetPicture(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PictureInfo? picture)
    {
        picture = all.FirstOrDefault(p => MediaIds.Comparer.Equals(p.Id, id));
        return picture is not null;
    }

    /// <inheritdoc />
    /// <remarks>A bundled picture holds two BMP files back to back (the 5.4 license notice, then the picture): the second is decoded.</remarks>
    public Rgba32Image? LoadImage(string id)
    {
        if (!TryGetPicture(id, out PictureInfo? picture) || !File.Exists(picture.FilePath))
        {
            return null;
        }

        byte[] bytes = File.ReadAllBytes(picture.FilePath);
        int offset = bytes.Length > 6 && bytes[0] == 'B' && bytes[1] == 'M' ? BitConverter.ToInt32(bytes, 2) : 0;
        if (offset <= 0 || offset + 2 > bytes.Length || bytes[offset] != 'B' || bytes[offset + 1] != 'M')
        {
            offset = 0;
        }

        using var stream = new MemoryStream(bytes, offset, bytes.Length - offset);
        var frame = new System.Windows.Media.Imaging.FormatConvertedBitmap(
            System.Windows.Media.Imaging.BitmapDecoder.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0],
            System.Windows.Media.PixelFormats.Bgra32,
            null,
            0);
        uint[] pixels = new uint[frame.PixelWidth * frame.PixelHeight];
        frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
        return new Rgba32Image(frame.PixelWidth, frame.PixelHeight, pixels);
    }

    /// <inheritdoc />
    public IReadOnlyList<MediaImportResult> AddFiles(IEnumerable<string> paths) =>
        [.. paths.Select(p => new MediaImportResult(p, MediaImportOutcome.Added, MediaIds.User(Path.GetFileName(p)), null))];

    /// <inheritdoc />
    public HeldItem? Remove(string id)
    {
        settings.Update(s => s with { Pictures = new HiddenItems { Hidden = [.. s.Pictures.Hidden, id] } }, SettingsChange.Edit("Remove a picture"));
        Changed?.Invoke(this, EventArgs.Empty);
        return null;
    }

    /// <inheritdoc />
    public void Restore(HeldItem item)
    {
    }

    /// <inheritdoc />
    public void RestoreHiddenPictures()
    {
        settings.Update(s => s with { Pictures = new HiddenItems() }, SettingsChange.Edit("Restore removed pictures"));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>A screen saver service whose preview is a plain night-gradient panel.</summary>
public sealed class FakeScreenSaverService : IScreenSaverService
{
    /// <inheritdoc />
    public bool IsPreviewRunning { get; private set; }

    /// <summary>How often "Preview Screen Saver" ran.</summary>
    public int PreviewRuns { get; private set; }

    /// <inheritdoc />
    public FrameworkElement CreatePreview() => new Border
    {
        Background = new LinearGradientBrush(Color.FromRgb(0x0B, 0x15, 0x30), Color.FromRgb(0x1D, 0x34, 0x66), 90),
        Child = new TextBlock { Text = "Merry Christmas!", Foreground = Brushes.Red, FontSize = 18, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };

    /// <inheritdoc />
    public Task RunPreviewAsync()
    {
        PreviewRuns++;
        return Task.CompletedTask;
    }
}

/// <summary>Bulb dialogs that record what they were asked and create GIF bulbs through the catalog.</summary>
public sealed class FakeBulbFactoryDialogs(IBulbCatalog catalog) : IBulbFactoryDialogs
{
    /// <summary>Every call, e.g. "Credits builtin:standard-bulbs".</summary>
    public List<string> Calls { get; } = [];

    /// <inheritdoc />
    public bool HasUnsavedChanges => false;

    /// <inheritdoc />
    public Task<BulbEditingResult> EditBulbAsync(Window owner, string bulbId)
    {
        Calls.Add($"Edit {bulbId}");
        return Task.FromResult(new BulbEditingResult(false, bulbId, false));
    }

    /// <inheritdoc />
    public Task<bool> EditCategoriesAsync(Window owner, string bulbId)
    {
        Calls.Add($"EditCategories {bulbId}");
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public void ShowCredits(Window owner, string bulbId) => Calls.Add($"Credits {bulbId}");

    /// <inheritdoc />
    public Task<string?> ExportBulbFileAsync(Window owner, string bulbId)
    {
        Calls.Add($"Export {bulbId}");
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public GifBulbCreation CreateBulbFromGif(string gifPath)
    {
        BulbImportResult result = catalog.ImportFile(gifPath);
        return result.Outcome == BulbImportOutcome.Added && result.BulbId is { } id && catalog.TryGetInfo(id, out BulbInfo? info)
            ? new GifBulbCreation(gifPath, id, info.FilePath, null, null)
            : new GifBulbCreation(gifPath, null, null, "Cannot Import GIF File", "Sorry, this GIF file can't be imported. It may be damaged in some way.");
    }

    /// <inheritdoc />
    public Task<bool> ConfirmDiscardAsync() => Task.FromResult(true);
}
