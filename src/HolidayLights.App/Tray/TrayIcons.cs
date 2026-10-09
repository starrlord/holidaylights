using System.IO;
using System.Windows;
using HolidayLights.App.Shell;
using DrawingIcon = System.Drawing.Icon;
using DrawingSize = System.Drawing.Size;

namespace HolidayLights.App.Tray;

/// <summary>
/// The four tray icons (lit and unlit, for light and dark taskbars; PRODUCT-SPEC 2.2, 4.7) loaded from the branding
/// resources at the notification area's size (16 px at 100 %, 24 px at 150 %), so Windows never rescales them.
/// </summary>
/// <remarks>
/// H.NotifyIcon's <c>TaskbarIcon</c> disposes the icon it replaces whenever its <c>Icon</c> changes, so an icon handed to it
/// must never be handed to it again. <see cref="Create"/> therefore returns a new copy each time (its own icon handle) and
/// keeps the loaded originals to itself.
/// </remarks>
internal sealed class TrayIcons : IDisposable
{
    private readonly Dictionary<(bool Lit, bool LightTaskbar), DrawingIcon> originals = [];
    private readonly Func<bool, bool, DrawingIcon> load;
    private bool disposed;

    /// <summary>Creates the set with the branding resources.</summary>
    public TrayIcons()
        : this(LoadFromResources)
    {
    }

    /// <summary>Creates the set with another source of the originals (tests).</summary>
    /// <param name="load">Loads the original icon of a state (lit, light taskbar).</param>
    internal TrayIcons(Func<bool, bool, DrawingIcon> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        this.load = load;
    }

    /// <summary>A new icon for a state, owned by the caller (the tray icon disposes it when it is replaced).</summary>
    /// <param name="lit">The lights are on.</param>
    /// <param name="lightTaskbar">The taskbar uses the light theme.</param>
    /// <returns>A new icon.</returns>
    public DrawingIcon Create(bool lit, bool lightTaskbar)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!originals.TryGetValue((lit, lightTaskbar), out DrawingIcon? original))
        {
            original = load(lit, lightTaskbar);
            originals[(lit, lightTaskbar)] = original;
        }

        return (DrawingIcon)original.Clone();
    }

    /// <summary>Releases the originals (the copies belong to their callers).</summary>
    public void Dispose()
    {
        disposed = true;
        foreach (DrawingIcon icon in originals.Values)
        {
            icon.Dispose();
        }

        originals.Clear();
    }

    private static DrawingIcon LoadFromResources(bool lit, bool lightTaskbar)
    {
        string uri = (lit, lightTaskbar) switch
        {
            (true, true) => AppAssets.TrayLitLight,
            (true, false) => AppAssets.TrayLitDark,
            (false, true) => AppAssets.TrayUnlitLight,
            _ => AppAssets.TrayUnlitDark,
        };
        int size = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmallIconWidthMetric, NativeMethods.GetDpiForSystem());
        using Stream stream = Application.GetResourceStream(AppAssetUris.Resolve(uri)).Stream;
        return new DrawingIcon(stream, new DrawingSize(size, size));
    }
}
