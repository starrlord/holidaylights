using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace HolidayLights.Platform.Native;

/// <summary>Runs shell COM calls in a single-threaded apartment and creates source-generated COM objects.</summary>
internal static class ComApartment
{
    private static readonly StrategyBasedComWrappers Wrappers = new();

    /// <summary>Runs COM work on an STA thread: inline when the caller already is one (the UI thread), otherwise on a short-lived STA thread.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="work">The work.</param>
    /// <returns>The result of <paramref name="work"/>.</returns>
    public static T Run<T>(Func<T> work)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return work();
        }

        T result = default!;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception e)
            {
                failure = ExceptionDispatchInfo.Capture(e);
            }
        })
        {
            IsBackground = true,
            Name = "Holiday Lights shell call",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
        return result;
    }

    /// <summary>Creates a COM object (in-process or local server) and returns the requested interface.</summary>
    /// <typeparam name="T">A <see cref="GeneratedComInterfaceAttribute"/> interface.</typeparam>
    /// <param name="classId">The class.</param>
    /// <returns>The interface; give it back with <see cref="Release"/>.</returns>
    /// <exception cref="COMException">The object cannot be created.</exception>
    public static T Create<T>(Guid classId)
        where T : class
    {
        int hr = NativeMethods.CoCreateInstance(
            classId, 0, NativeMethods.CLSCTX_INPROC_SERVER | NativeMethods.CLSCTX_LOCAL_SERVER, typeof(T).GUID, out nint instance);
        Marshal.ThrowExceptionForHR(hr);
        return Wrap<T>(instance);
    }

    /// <summary>Wraps a COM pointer that the caller owns (the caller's reference is released here).</summary>
    /// <typeparam name="T">A <see cref="GeneratedComInterfaceAttribute"/> interface.</typeparam>
    /// <param name="instance">The pointer.</param>
    /// <returns>The interface; give it back with <see cref="Release"/>.</returns>
    public static T Wrap<T>(nint instance)
        where T : class
    {
        try
        {
            return (T)Wrappers.GetOrCreateObjectForComInstance(instance, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(instance);
        }
    }

    /// <summary>Releases a COM object at once instead of waiting for the garbage collector.</summary>
    /// <param name="comObject">An object from <see cref="Create{T}"/> or <see cref="Wrap{T}"/> (null is ignored).</param>
    public static void Release(object? comObject)
    {
        if (comObject is ComObject wrapper)
        {
            wrapper.FinalRelease();
        }
    }
}
