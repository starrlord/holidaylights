using SharpGen.Runtime;
using Vortice.DXGI;

namespace HolidayLights.Rendering.Composition;

/// <summary>
/// The displays that run in Windows HDR (Advanced Color with the ST 2084 / BT.2020 color space), where the additive glow is
/// drawn at half intensity (PRODUCT-SPEC 5.9, risk R2; product-owner decision 2). Reads the DXGI outputs
/// (<c>IDXGIOutput6::GetDesc1</c>); DXGI caches them in its factory, so a new factory is made when the old one is no longer
/// current or a display change asks for it. Lights thread only.
/// </summary>
internal sealed class AdvancedColorOutputs : IDisposable
{
    /// <summary>The glow intensity factor on a display in HDR.</summary>
    public const float HdrGlowScale = 0.5f;

    private IDXGIFactory1? factory;
    private HashSet<RectI> hdr = [];

    /// <summary>The desktop rectangles (physical pixels) of the outputs in HDR.</summary>
    public IReadOnlySet<RectI> HdrBounds => hdr;

    /// <summary>The glow factor for the display with <paramref name="bounds"/>: half in HDR, else 1.</summary>
    public float GlowScale(RectI bounds) => GlowScale(hdr, bounds);

    /// <summary>The glow factor for the display with <paramref name="bounds"/> given the HDR outputs.</summary>
    public static float GlowScale(IReadOnlySet<RectI> hdrBounds, RectI bounds) => hdrBounds.Contains(bounds) ? HdrGlowScale : 1f;

    /// <summary>Reads the outputs again when DXGI reports a change, or always with <paramref name="force"/> (display changes).</summary>
    /// <returns>True when the set of HDR displays changed.</returns>
    public bool Refresh(bool force)
    {
        try
        {
            if (!force && factory is not null && factory.IsCurrent)
            {
                return false;
            }

            factory?.Dispose();
            factory = null;
            factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            HashSet<RectI> found = [];
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

            bool changed = !found.SetEquals(hdr);
            hdr = found;
            return changed;
        }
        catch (SharpGenException)
        {
            // DXGI unavailable (a remote session without outputs, a driver update): keep what is known and try again later.
            factory?.Dispose();
            factory = null;
            return false;
        }
    }

    /// <summary>Releases the factory.</summary>
    public void Dispose()
    {
        factory?.Dispose();
        factory = null;
    }
}
