using HolidayLights.App.BulbFactory;
using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Bulbs.Writing;
using HolidayLights.Platform.Files;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.BulbFactory;

/// <summary>
/// A real bulb catalog over a private data root (no bundled bulbs, so it starts at once), platform's real holding folder
/// (never committed, so nothing reaches the Recycle Bin), a recording Undo history and scripted prompts: everything Bulb
/// Editing needs, without windows.
/// </summary>
internal sealed class EditorTestBed : IDisposable
{
    private readonly TempDataRoot root = new();

    /// <summary>Creates the test bed and starts the catalog.</summary>
    public EditorTestBed()
    {
        Paths = DataPaths.ForDataRoot(root.Root, Directory.CreateDirectory(Path.Combine(root.Root, "Install")).FullName);
        Holding = new HoldingFolder(Paths, new FakeShell(), Log);
        Catalog = new BulbCatalog(Paths, Settings, Holding, Log, () => "Pat Smith");
        Assert.True(Catalog.StartAsync().Wait(TimeSpan.FromSeconds(30)), "The catalog did not start.");
        Environment = new BulbEditorEnvironment(Catalog, Holding, () => Undo, Log);
    }

    /// <summary>The data paths.</summary>
    public DataPaths Paths { get; }

    /// <summary>The settings.</summary>
    public InMemorySettingsStore Settings { get; } = new();

    /// <summary>The log.</summary>
    public RecordingLog Log { get; } = new();

    /// <summary>The holding folder.</summary>
    public HoldingFolder Holding { get; }

    /// <summary>The catalog.</summary>
    public BulbCatalog Catalog { get; }

    /// <summary>The Undo history of "Settings".</summary>
    public RecordingUndoHistory Undo { get; } = new();

    /// <summary>The prompts the editor shows.</summary>
    public ScriptedPrompts Prompts { get; } = new();

    /// <summary>The editor's services.</summary>
    public BulbEditorEnvironment Environment { get; }

    /// <summary>Writes a bulb into My Bulbs and loads it in the catalog.</summary>
    /// <param name="document">The bulb.</param>
    /// <param name="fileName">The file name.</param>
    /// <returns>The bulb id.</returns>
    public string AddMyBulb(BulbDocument document, string fileName = "Star.bul")
    {
        string path = Path.Combine(DataPaths.EnsureFolder(Paths.MyBulbsFolder), fileName);
        BulFileWriter.WriteAtomically(path, BulFileWriter.Encode(document, 4242));
        return Catalog.LoadUserBulb(path)!.Id;
    }

    /// <summary>Opens Bulb Editing on a My Bulbs bulb.</summary>
    /// <param name="bulbId">The bulb.</param>
    /// <returns>The editor, with its first decodes done.</returns>
    public async Task<BulbEditingViewModel> OpenAsync(string bulbId)
    {
        Assert.True(Catalog.TryGetInfo(bulbId, out BulbInfo? info));
        BulbEditorSession session = await BulbEditorSession.OpenAsync(Environment, bulbId, info.FilePath!);
        var editor = new BulbEditingViewModel(session, Catalog.GetAllCategoryNames(), Prompts);
        await editor.Frames.WhenIdleAsync();
        return editor;
    }

    /// <summary>A file in a scratch folder of the data root.</summary>
    /// <param name="fileName">The file name.</param>
    /// <param name="bytes">The content.</param>
    /// <returns>The path.</returns>
    public string Scratch(string fileName, byte[] bytes)
    {
        string path = Path.Combine(Directory.CreateDirectory(Path.Combine(root.Root, "Scratch")).FullName, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Catalog.Dispose();
        Holding.Dispose();
        root.Dispose();
    }
}

/// <summary>The Settings Undo history, recording the steps instead of showing them.</summary>
internal sealed class RecordingUndoHistory : IUndoHistory
{
    /// <summary>The recorded steps.</summary>
    public List<UndoStep> Steps { get; } = [];

    /// <inheritdoc />
    public bool IsRecording => true;

    /// <inheritdoc />
    public void Record(UndoStep step) => Steps.Add(step);

    /// <inheritdoc />
    public IDisposable BeginGroup(string description) => new Group();

    private sealed class Group : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

/// <summary>Answers the editor's questions from queues; records how often each was asked.</summary>
internal sealed class ScriptedPrompts : IBulbEditingPrompts
{
    /// <summary>The files "Change..." picks next (null = cancel).</summary>
    public Queue<IReadOnlyList<string>?> PictureFiles { get; } = new();

    /// <summary>The names "New Category" returns next (null = cancel).</summary>
    public Queue<string?> NewCategories { get; } = new();

    /// <summary>The answer to "Discard Changes?".</summary>
    public bool Discard { get; set; }

    /// <summary>How often "Discard Changes?" was asked, and for which name last.</summary>
    public (int Count, string? Name) DiscardQuestions { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<string>? PickPictureFiles() => PictureFiles.Count > 0 ? PictureFiles.Dequeue() : null;

    /// <inheritdoc />
    public string? AskNewCategory() => NewCategories.Count > 0 ? NewCategories.Dequeue() : null;

    /// <inheritdoc />
    public Task<bool> ConfirmDiscardAsync(string bulbName)
    {
        DiscardQuestions = (DiscardQuestions.Count + 1, bulbName);
        return Task.FromResult(Discard);
    }
}
