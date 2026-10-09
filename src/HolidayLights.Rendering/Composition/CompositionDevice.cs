using System.Diagnostics;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace HolidayLights.Rendering.Composition;

/// <summary>
/// The D3D11 device and the DirectComposition device of the Lights thread (ARCHITECTURE 5).
/// Single-threaded: create and use it on the Lights thread only.
/// </summary>
internal sealed class CompositionDevice : IDisposable
{
    private static readonly FeatureLevel[] FeatureLevels =
        [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];

    private readonly ID3D11Device d3d;
    private readonly ID3D11DeviceContext context;
    private readonly IDCompositionDevice dcomp;

    private CompositionDevice(ID3D11Device d3d, IDCompositionDevice dcomp, bool isSoftware, string adapter)
    {
        this.d3d = d3d;
        context = d3d.ImmediateContext;
        this.dcomp = dcomp;
        IsSoftware = isSoftware;
        Adapter = adapter;
    }

    /// <summary>True when the device is WARP (software).</summary>
    public bool IsSoftware { get; }

    /// <summary>The adapter description (for the log and diagnostics).</summary>
    public string Adapter { get; }

    /// <summary>The DirectComposition device.</summary>
    public IDCompositionDevice DComp => dcomp;

    /// <summary>Creates the devices.</summary>
    /// <param name="software">Use WARP instead of the hardware adapter.</param>
    /// <exception cref="CompositionUnavailableException">No device could be created.</exception>
    public static CompositionDevice Create(bool software)
    {
        ID3D11Device? device = null;
        try
        {
            Result result = D3D11.D3D11CreateDevice(
                IntPtr.Zero, software ? DriverType.Warp : DriverType.Hardware, DeviceCreationFlags.BgraSupport, FeatureLevels, out device);
            if (result.Failure || device is null)
            {
                throw new CompositionUnavailableException($"D3D11CreateDevice failed (0x{result.Code:X8}).");
            }

            using IDXGIDevice dxgi = device.QueryInterface<IDXGIDevice>();
            string adapter;
            using (IDXGIAdapter dxgiAdapter = dxgi.GetAdapter())
            {
                adapter = dxgiAdapter.Description.Description;
            }

            IDCompositionDevice dcomp = Vortice.DirectComposition.DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgi);
            return new CompositionDevice(device, dcomp, software, adapter);
        }
        catch (SharpGenException exception)
        {
            device?.Dispose();
            throw new CompositionUnavailableException("DirectComposition is not available.", exception);
        }
        catch (CompositionUnavailableException)
        {
            device?.Dispose();
            throw;
        }
    }

    /// <summary>Creates a visual.</summary>
    public IDCompositionVisual CreateVisual()
    {
        IDCompositionVisual visual = dcomp.CreateVisual();
        visual.SetBitmapInterpolationMode(BitmapInterpolationMode.NearestNeighbor);
        visual.SetBorderMode(BorderMode.Hard);
        return visual;
    }

    /// <summary>Creates an effect group (per-visual opacity).</summary>
    public IDCompositionEffectGroup CreateEffectGroup() => dcomp.CreateEffectGroup();

    /// <summary>Binds a composition target to a window, or returns null when the window cannot be composed.</summary>
    public IDCompositionTarget? TryCreateTarget(nint hwnd)
    {
        Result result = dcomp.CreateTargetForHwnd(hwnd, true, out IDCompositionTarget? target);
        return result.Success ? target : null;
    }

    /// <summary>Uploads a premultiplied BGRA picture into a new surface (null for an empty picture).</summary>
    public unsafe IDCompositionSurface? CreateSurface(PremultipliedImage image)
    {
        if (image.Width <= 0 || image.Height <= 0)
        {
            return null;
        }

        dcomp.CreateSurface((uint)image.Width, (uint)image.Height, Format.B8G8R8A8_UNorm, Vortice.DXGI.AlphaMode.Premultiplied, out IDCompositionSurface? surface)
            .CheckError();
        try
        {
            using (ID3D11Texture2D texture = surface!.BeginDraw<ID3D11Texture2D>(null, out Int2 offset))
            {
                fixed (uint* pixels = image.Pixels)
                {
                    var box = new Box(offset.X, offset.Y, 0, offset.X + image.Width, offset.Y + image.Height, 1);
                    context.UpdateSubresource(texture, 0, box, (nint)pixels, (uint)image.Width * 4, 0);
                }
            }

            surface.EndDraw().CheckError();
            return surface;
        }
        catch
        {
            surface?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Turns a curve into a DirectComposition animation. The curve is re-anchored at the current time
    /// (<see cref="OpacityCurve.StartingAt"/>) because an animation starts with the frame that processes its commit, which
    /// follows at once; no absolute composition-clock time is involved.
    /// </summary>
    public IDCompositionAnimation CreateAnimation(OpacityCurve curve)
    {
        OpacityCurve local = curve.StartingAt(Stopwatch.GetTimestamp());
        IDCompositionAnimation animation = dcomp.CreateAnimation();
        foreach (CubicSegment segment in local.Segments)
        {
            animation.AddCubic(segment.OffsetSeconds, (float)segment.C0, (float)segment.C1, (float)segment.C2, (float)segment.C3).CheckError();
        }

        if (local.Repeats)
        {
            animation.AddRepeat(local.TerminalOffsetSeconds, local.RepeatSeconds).CheckError();
        }
        else
        {
            animation.End(local.TerminalOffsetSeconds, local.EndValue).CheckError();
        }

        return animation;
    }

    /// <summary>Commits the batch; false when the device was lost.</summary>
    public bool TryCommit() => dcomp.Commit().Success;

    /// <summary>True while the device is usable (<c>CheckDeviceState</c> and the D3D removal reason).</summary>
    public bool IsValid()
    {
        try
        {
            return dcomp.CheckDeviceState() && d3d.DeviceRemovedReason.Success;
        }
        catch (SharpGenException)
        {
            return false;
        }
    }

    /// <summary>Releases both devices.</summary>
    public void Dispose()
    {
        dcomp.Dispose();
        context.ClearState();
        context.Flush();
        context.Dispose();
        d3d.Dispose();
    }
}

/// <summary>No Direct3D or DirectComposition device can be created (PRODUCT-SPEC 5.12.2 "No graphics device").</summary>
internal sealed class CompositionUnavailableException : Exception
{
    /// <summary>Creates the exception.</summary>
    public CompositionUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with its cause.</summary>
    public CompositionUnavailableException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
