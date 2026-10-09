using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HolidayLights.Platform.Native;

namespace HolidayLights.Platform.Displays;

/// <summary>Where <see cref="DisplayService"/> reads the monitors from (a seam for tests).</summary>
internal interface IDisplaySource
{
    /// <summary>The monitors in physical pixels (cheap: polled every 2 s).</summary>
    /// <returns>One sample per monitor.</returns>
    IReadOnlyList<MonitorSample> SampleMonitors();

    /// <summary>Device interface paths and EDID names of the monitors (more expensive: read when the topology changed).</summary>
    /// <param name="samples">The monitors to identify.</param>
    /// <returns>Identities by GDI device name.</returns>
    IReadOnlyDictionary<string, DisplayIdentity> ReadIdentities(IReadOnlyList<MonitorSample> samples);
}

/// <summary>Reads the monitors from Windows: <c>EnumDisplayMonitors</c>, <c>GetDpiForMonitor</c>, <c>EnumDisplayDevices</c>, <c>QueryDisplayConfig</c>.</summary>
internal sealed unsafe class SystemDisplaySource : IDisplaySource
{
    private const int ErrorInsufficientBuffer = 122;
    private const int DefaultDpi = 96;

    /// <inheritdoc />
    public IReadOnlyList<MonitorSample> SampleMonitors()
    {
        using PhysicalPixelsScope scope = PhysicalPixelsScope.Enter();
        var handles = new List<nint>(4);
        GCHandle list = GCHandle.Alloc(handles);
        try
        {
            NativeMethods.EnumDisplayMonitors(0, null, &CollectMonitor, GCHandle.ToIntPtr(list));
        }
        finally
        {
            list.Free();
        }

        var samples = new List<MonitorSample>(handles.Count);
        foreach (nint monitor in handles)
        {
            var info = new MonitorInfoEx { Size = (uint)sizeof(MonitorInfoEx) };
            if (!NativeMethods.GetMonitorInfo(monitor, &info))
            {
                continue;
            }

            int dpi = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0
                ? (int)dpiX
                : DefaultDpi;
            samples.Add(new MonitorSample(
                new string(info.Device),
                info.Monitor.ToRectI(),
                info.Work.ToRectI(),
                dpi,
                (info.Flags & NativeMethods.MONITORINFOF_PRIMARY) != 0));
        }

        return samples;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, DisplayIdentity> ReadIdentities(IReadOnlyList<MonitorSample> samples)
    {
        Dictionary<string, (string DevicePath, string FriendlyName)> targets = ReadTargetNames();
        var identities = new Dictionary<string, DisplayIdentity>(StringComparer.OrdinalIgnoreCase);
        foreach (MonitorSample sample in samples)
        {
            targets.TryGetValue(sample.GdiName, out (string DevicePath, string FriendlyName) target);
            string deviceId = ReadMonitorInterfacePath(sample.GdiName) ?? target.DevicePath ?? "";
            identities[sample.GdiName] = new DisplayIdentity(deviceId, target.FriendlyName ?? "");
        }

        return identities;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CollectMonitor(nint monitor, nint hdc, NativeRect* clip, nint data)
    {
        ((List<nint>)GCHandle.FromIntPtr(data).Target!).Add(monitor);
        return 1;
    }

    /// <summary>The device interface path of the (first active) monitor on a GDI device.</summary>
    private static string? ReadMonitorInterfacePath(string gdiName)
    {
        string? firstAttached = null;
        var device = new DisplayDevice { Size = (uint)sizeof(DisplayDevice) };
        for (uint index = 0; NativeMethods.EnumDisplayDevices(gdiName, index, &device, NativeMethods.EDD_GET_DEVICE_INTERFACE_NAME); index++)
        {
            string path = new(device.DeviceId);
            if (path.Length > 0)
            {
                if ((device.StateFlags & NativeMethods.DISPLAY_DEVICE_ACTIVE) != 0)
                {
                    return path;
                }

                firstAttached ??= path;
            }

            device = new DisplayDevice { Size = (uint)sizeof(DisplayDevice) };
        }

        return firstAttached;
    }

    /// <summary>EDID names and device paths of the active display paths, by source GDI device name.</summary>
    private static Dictionary<string, (string DevicePath, string FriendlyName)> ReadTargetNames()
    {
        var names = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        DisplayConfigPathInfo[] paths;
        int result;
        do
        {
            if (NativeMethods.GetDisplayConfigBufferSizes(NativeMethods.QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0)
            {
                return names;
            }

            paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];
            fixed (DisplayConfigPathInfo* pathBuffer = paths)
            fixed (DisplayConfigModeInfo* modeBuffer = modes)
            {
                result = NativeMethods.QueryDisplayConfig(NativeMethods.QDC_ONLY_ACTIVE_PATHS, ref pathCount, pathBuffer, ref modeCount, modeBuffer, 0);
            }

            Array.Resize(ref paths, (int)pathCount);
        }
        while (result == ErrorInsufficientBuffer);

        if (result != 0)
        {
            return names;
        }

        foreach (DisplayConfigPathInfo path in paths)
        {
            var source = new DisplayConfigSourceDeviceName
            {
                Header = new DisplayConfigDeviceInfoHeader
                {
                    Type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                    Size = (uint)sizeof(DisplayConfigSourceDeviceName),
                    AdapterId = path.SourceInfo.AdapterId,
                    Id = path.SourceInfo.Id,
                },
            };
            var target = new DisplayConfigTargetDeviceName
            {
                Header = new DisplayConfigDeviceInfoHeader
                {
                    Type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                    Size = (uint)sizeof(DisplayConfigTargetDeviceName),
                    AdapterId = path.TargetInfo.AdapterId,
                    Id = path.TargetInfo.Id,
                },
            };
            if (NativeMethods.DisplayConfigGetDeviceInfo(&source.Header) != 0 ||
                NativeMethods.DisplayConfigGetDeviceInfo(&target.Header) != 0)
            {
                continue;
            }

            names.TryAdd(new string(source.ViewGdiDeviceName), (new string(target.MonitorDevicePath), new string(target.MonitorFriendlyDeviceName)));
        }

        return names;
    }
}
