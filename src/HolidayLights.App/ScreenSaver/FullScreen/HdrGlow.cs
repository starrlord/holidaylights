using SharpGen.Runtime;
using Vortice.DXGI;

namespace HolidayLights.App.ScreenSaver.FullScreen;

/// <summary>
/// The displays that run in Windows HDR (Advanced Color with the ST 2084 / BT.2020 color space), where the screen saver draws
/// the additive glow at half intensity, as the desktop lights do (PRODUCT-SPEC 5.9, risk R2; product-owner decision 2;
/// review r1 #43). The DXGI outputs (<c>IDXGIOutput6::GetDesc1</c>) are read once per saver run and matched to a display by
/// its desktop rectangle. The same rule as rendering's <c>AdvancedColorOutputs</c>, which is internal to rendering.
/// </summary>
internal static class HdrGlow
{
    /// <summary>The glow intensity factor on a display in HDR.</summary>
    public const float HdrScale = 0.5f;

    private static readonly Lazy<HashSet<RectI>> HdrBounds = new(Read, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The glow factor of a display: half in HDR, else 1.</summary>
    /// <param name="bounds">The display's bounds (physical pixels, desktop coordinates).</param>
    /// <returns>The factor.</returns>
    public static float ScaleFor(RectI bounds) => HdrBounds.Value.Contains(bounds) ? HdrScale : 1f;

    private static HashSet<RectI> Read()
    {
        HashSet<RectI> found = [];
        try
        {
            using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint a = 0; factory.EnumAdapters1(a, out IDXGIAdapter1? adapter).Success; a++)
            {
                using (adapter)
                {
                    for (uint o = 0; adapter!.EnumOutputs(o, out IDXGIOutput? output).Success; o++)
                    {
                        using (output)
                        {
                            using IDXGIOutput6? output6 = output!.QueryInterfaceOrNull<IDXGIOutput6>();
                            if (output6?.Description1 is { ColorSpace: ColorSpaceType.RgbFullG2084NoneP2020 } description && description.AttachedToDesktop)
                            {
                                found.Add(new RectI(
                                    description.DesktopCoordinates.Left, description.DesktopCoordinates.Top, description.DesktopCoordinates.Right, description.DesktopCoordinates.Bottom));
                            }
                        }
                    }
                }
            }
        }
        catch (SharpGenException)
        {
            // DXGI unavailable (a remote session without outputs, a driver update): full glow, as on an SDR display.
        }

        return found;
    }
}
