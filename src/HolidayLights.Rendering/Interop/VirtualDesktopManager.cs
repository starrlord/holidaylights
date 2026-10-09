namespace HolidayLights.Rendering.Interop;

/// <summary>
/// The documented <c>IVirtualDesktopManager</c> (shobjidl_core.h), called through its vtable so no runtime COM marshalling is
/// needed. Used by the 2 s maintenance as a safety net: a top-level layer that is not on the current virtual desktop is moved
/// there (PRODUCT-SPEC 5.1). Create and use it on the Lights thread (STA).
/// </summary>
internal sealed unsafe class VirtualDesktopManager : IDisposable
{
    private const uint ClassContextInprocServer = 0x1;
    private const uint ClassContextLocalServer = 0x4;
    private static readonly Guid ClassId = new("AA509086-5CA9-4C25-8F95-589D3C07B48A");
    private static readonly Guid InterfaceId = new("A5CD92FF-29BE-454C-8D04-D82879FB3F1B");

    private nint instance;

    private VirtualDesktopManager(nint instance) => this.instance = instance;

    /// <summary>Creates the manager, or returns null when the shell does not provide it.</summary>
    public static VirtualDesktopManager? TryCreate()
    {
        Guid classId = ClassId;
        Guid interfaceId = InterfaceId;
        nint result = 0;
        int hr = Ole32.CoCreateInstance(&classId, 0, ClassContextInprocServer | ClassContextLocalServer, &interfaceId, &result);
        return hr >= 0 && result != 0 ? new VirtualDesktopManager(result) : null;
    }

    /// <summary>
    /// Returns false only when Windows positively reports that the window is on another virtual desktop; windows the shell does
    /// not track (tool windows, owned windows) and failed queries count as visible.
    /// </summary>
    public bool IsOnCurrentDesktop(nint hwnd)
    {
        if (instance == 0)
        {
            return true;
        }

        int onCurrent = 1;
        var isOnCurrent = (delegate* unmanaged[Stdcall]<nint, nint, int*, int>)VTable[3];
        int hr = isOnCurrent(instance, hwnd, &onCurrent);
        return hr < 0 || onCurrent != 0;
    }

    /// <summary>Moves a window of this process to the virtual desktop of <paramref name="referenceWindow"/>.</summary>
    /// <returns>True when the window was moved.</returns>
    public bool MoveToDesktopOf(nint hwnd, nint referenceWindow)
    {
        if (instance == 0 || referenceWindow == 0)
        {
            return false;
        }

        Guid desktop = Guid.Empty;
        var getDesktopId = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, int>)VTable[4];
        if (getDesktopId(instance, referenceWindow, &desktop) < 0 || desktop == Guid.Empty)
        {
            return false;
        }

        var moveToDesktop = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, int>)VTable[5];
        return moveToDesktop(instance, hwnd, &desktop) >= 0;
    }

    /// <summary>Releases the COM object.</summary>
    public void Dispose()
    {
        if (instance != 0)
        {
            var release = (delegate* unmanaged[Stdcall]<nint, uint>)VTable[2];
            release(instance);
            instance = 0;
        }
    }

    private nint* VTable => *(nint**)instance;
}
