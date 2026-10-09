using HolidayLights.App.ScreenSaver;
using HolidayLights.App.ScreenSaver.Scene;
using HolidayLights.App.ScreenSaver.Simulation;
using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Flash;
using HolidayLights.Core.Layout;
using HolidayLights.Core.Sprites;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.ScreenSaver;

/// <summary>
/// The saver's services with the real engines (catalog, layout, flash, sprites, compositor) and the real picture library,
/// on a private data root; bundled content comes from the test output folder.
/// </summary>
internal sealed class SaverTestKit : IDisposable
{
    public SaverTestKit(AppSettings? settings = null)
    {
        Settings = new InMemorySettingsStore(settings);
        Holding = new TestHoldingFolder(Root.Paths);
        Catalog = new BulbCatalog(Root.Paths, Settings, Holding, Log);
        Sprites = new SpriteProvider(Root.Paths, Log);
        Pictures = new PictureLibrary(Root.Paths, Settings, Holding, Log);
        Pictures.Start();
        Services = new SaverServices(Catalog, new ClassicLayoutEngine(), new FlashEngine(), Sprites, new CpuCompositor(), Pictures, Log);
    }

    public TempDataRoot Root { get; } = new();

    public RecordingLog Log { get; } = new();

    public InMemorySettingsStore Settings { get; }

    public TestHoldingFolder Holding { get; }

    public BulbCatalog Catalog { get; }

    public SpriteProvider Sprites { get; }

    public PictureLibrary Pictures { get; }

    public SaverServices Services { get; }

    /// <summary>A display in physical pixels.</summary>
    public static DisplayInfo Display(int width, int height, int dpi = 96, int left = 0, bool primary = true, int number = 1) => new()
    {
        DeviceId = $"test-display-{number}",
        DeviceName = $@"\\.\DISPLAY{number}",
        Number = number,
        Bounds = RectI.FromXYWH(left, 0, width, height),
        WorkArea = RectI.FromXYWH(left, 0, width, height - 48 * dpi / 96),
        Dpi = dpi,
        IsPrimary = primary,
    };

    /// <summary>Settings with a different saver look.</summary>
    public static AppSettings WithSaver(SaverLook look) => new() { Current = new ThemeableSettings { Saver = look } };

    /// <summary>The scene of one main display (STA thread: the message uses WPF text).</summary>
    public SaverScene MainScene(DisplayInfo display, AppSettings settings, uint seed = 1)
    {
        SaverOptions options = SaverOptions.FromSettings(settings, seed);
        DecodedPicture? picture = DecodedPicture.Load(Pictures, options.Look.Picture);
        return Services.CreateScenes([new SaverDisplayPlan(display, true, true)], options, picture, seed, 0)[0];
    }

    public void Dispose()
    {
        Pictures.Dispose();
        Catalog.Dispose();
        Root.Dispose();
    }
}

/// <summary>A random stream that returns given numbers, then fails (each test states exactly the 5.4 <c>rand()</c> calls).</summary>
internal sealed class ScriptedRandom(params int[] values) : ISaverRandom
{
    private readonly Queue<int> values = new(values);

    public int Remaining => values.Count;

    public int Next() => values.Count > 0 ? values.Dequeue() : throw new InvalidOperationException("The script has no more numbers.");
}

/// <summary>A random stream that always returns the same number.</summary>
internal sealed class ConstantRandom(int value) : ISaverRandom
{
    public int Calls { get; private set; }

    public int Next()
    {
        Calls++;
        return value;
    }
}
