using SharpGen.Runtime;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using RectI = HolidayLights.Core.Abstractions.RectI;

namespace HolidayLights.App.ScreenSaver.FullScreen;

/// <summary>
/// The Direct3D 11 and DirectComposition devices of the full-screen saver (CONTRACTS 11.8: the saver composes its own
/// tree). Pictures are uploaded once into premultiplied BGRA surfaces and shared; moving things are visuals whose offset or
/// opacity changes, so a frame costs a few property changes and one commit, never a redraw. Premultiplied pixels whose
/// colour exceeds their alpha (the glow halos) are added to what lies behind them.
/// </summary>
/// <remarks>Create and use on one thread (the saver's UI thread).</remarks>
internal sealed class SaverCompositor : IDisposable
{
    private static readonly FeatureLevel[] FeatureLevels =
        [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];

    private readonly ID3D11Device d3d;
    private readonly ID3D11DeviceContext context;
    private readonly IDCompositionDevice dcomp;
    private readonly Dictionary<PremultipliedImage, IDCompositionSurface> surfaces = new(ReferenceEqualityComparer.Instance);

    private SaverCompositor(ID3D11Device d3d, IDCompositionDevice dcomp)
    {
        this.d3d = d3d;
        context = d3d.ImmediateContext;
        this.dcomp = dcomp;
    }

    /// <summary>Creates the devices on the hardware adapter, or on WARP when there is none.</summary>
    /// <returns>The compositor.</returns>
    /// <exception cref="SharpGenException">DirectComposition is not available.</exception>
    public static SaverCompositor Create()
    {
        Result result = D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware, DeviceCreationFlags.BgraSupport, FeatureLevels, out ID3D11Device? device);
        if (result.Failure || device is null)
        {
            D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Warp, DeviceCreationFlags.BgraSupport, FeatureLevels, out device).CheckError();
        }

        try
        {
            using IDXGIDevice dxgi = device!.QueryInterface<IDXGIDevice>();
            return new SaverCompositor(device, DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgi));
        }
        catch
        {
            device!.Dispose();
            throw;
        }
    }

    /// <summary>Binds a window to a new composition target drawn above the window's own content.</summary>
    /// <param name="hwnd">The window.</param>
    /// <returns>The target.</returns>
    public IDCompositionTarget CreateTarget(nint hwnd)
    {
        dcomp.CreateTargetForHwnd(hwnd, true, out IDCompositionTarget? target).CheckError();
        return target!;
    }

    /// <summary>Creates a visual that shows pictures pixel for pixel.</summary>
    /// <returns>The visual.</returns>
    public IDCompositionVisual CreateVisual()
    {
        IDCompositionVisual visual = dcomp.CreateVisual();
        visual.SetBitmapInterpolationMode(BitmapInterpolationMode.NearestNeighbor);
        visual.SetBorderMode(BorderMode.Hard);
        return visual;
    }

    /// <summary>Creates a visual at a pixel position.</summary>
    /// <param name="x">Left in pixels.</param>
    /// <param name="y">Top in pixels.</param>
    /// <param name="content">The picture, or null.</param>
    /// <returns>The visual.</returns>
    public IDCompositionVisual CreateVisual(int x, int y, IDCompositionSurface? content)
    {
        IDCompositionVisual visual = CreateVisual();
        visual.SetOffsetX(x);
        visual.SetOffsetY(y);
        if (content is not null)
        {
            visual.SetContent(content);
        }

        return visual;
    }

    /// <summary>Adds a child in front of all its siblings, so children appear in the order they are added.</summary>
    /// <remarks>
    /// <c>AddVisual(child, insertAbove: false, referenceVisual: null)</c> is the call that puts the child in front;
    /// <c>insertAbove: true</c> with no reference puts it behind all siblings (checked on Windows 11 26H2).
    /// </remarks>
    /// <param name="parent">The parent visual.</param>
    /// <param name="child">The new child.</param>
    public static void Append(IDCompositionVisual parent, IDCompositionVisual child)
    {
        ArgumentNullException.ThrowIfNull(parent);
        parent.AddVisual(child, false, null).CheckError();
    }

    /// <summary>Creates an opacity effect for a visual.</summary>
    /// <param name="opacity">The starting opacity.</param>
    /// <returns>The effect group.</returns>
    public IDCompositionEffectGroup CreateOpacity(float opacity)
    {
        IDCompositionEffectGroup effect = dcomp.CreateEffectGroup();
        effect.SetOpacity(opacity);
        return effect;
    }

    /// <summary>The surface of a picture, uploaded the first time it is asked for; null for an empty picture.</summary>
    /// <param name="image">A picture that does not change any more.</param>
    /// <returns>The shared surface.</returns>
    public IDCompositionSurface? GetSurface(PremultipliedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width <= 0 || image.Height <= 0)
        {
            return null;
        }

        if (!surfaces.TryGetValue(image, out IDCompositionSurface? surface))
        {
            dcomp.CreateSurface((uint)image.Width, (uint)image.Height, Format.B8G8R8A8_UNorm, Vortice.DXGI.AlphaMode.Premultiplied, out surface)
                .CheckError();
            Upload(surface!, new RectI(0, 0, image.Width, image.Height), image, new RectI(0, 0, image.Width, image.Height));
            surfaces[image] = surface!;
        }

        return surface;
    }

    /// <summary>Creates a surface whose memory is allocated only where it is drawn (stamped piles, snow on the bulbs).</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <returns>The surface (transparent until drawn).</returns>
    public IDCompositionVirtualSurface CreateVirtualSurface(int width, int height)
    {
        dcomp.CreateVirtualSurface((uint)Math.Max(1, width), (uint)Math.Max(1, height), Format.B8G8R8A8_UNorm, Vortice.DXGI.AlphaMode.Premultiplied,
            out IDCompositionVirtualSurface? surface).CheckError();
        return surface!;
    }

    /// <summary>Replaces a rectangle of a surface with pixels of a picture.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="area">The rectangle of the surface to replace (inside it).</param>
    /// <param name="source">The picture holding the new pixels.</param>
    /// <param name="sourceArea">Where they are in the picture (same size as <paramref name="area"/>).</param>
    public unsafe void Upload(IDCompositionSurface surface, RectI area, PremultipliedImage source, RectI sourceArea)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(source);
        if (area.IsEmpty)
        {
            return;
        }

        using (ID3D11Texture2D texture = surface.BeginDraw<ID3D11Texture2D>(new RawRect(area.Left, area.Top, area.Right, area.Bottom), out Int2 offset))
        {
            fixed (uint* pixels = source.Pixels)
            {
                uint* first = pixels + sourceArea.Top * source.Width + sourceArea.Left;
                var box = new Box(offset.X, offset.Y, 0, offset.X + area.Width, offset.Y + area.Height, 1);
                context.UpdateSubresource(texture, 0, box, (nint)first, (uint)source.Width * 4, 0);
            }
        }

        surface.EndDraw().CheckError();
    }

    /// <summary>Shows every change made since the last commit.</summary>
    /// <returns>False when the device was lost.</returns>
    public bool Commit() => dcomp.Commit().Success;

    /// <summary>Releases the surfaces and both devices.</summary>
    public void Dispose()
    {
        foreach (IDCompositionSurface surface in surfaces.Values)
        {
            surface.Dispose();
        }

        surfaces.Clear();
        dcomp.Dispose();
        context.ClearState();
        context.Flush();
        context.Dispose();
        d3d.Dispose();
    }
}
