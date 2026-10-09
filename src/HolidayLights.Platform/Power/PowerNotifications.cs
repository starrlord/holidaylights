using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HolidayLights.Platform.Native;

namespace HolidayLights.Platform.Power;

/// <summary>
/// Power setting and effective power mode callbacks without a window (<c>PowerSettingRegisterNotification</c> with
/// <c>DEVICE_NOTIFY_CALLBACK</c>, <c>PowerRegisterForEffectivePowerModeNotifications</c>). Windows delivers the current
/// value at registration and every change later, on its own threads.
/// </summary>
/// <remarks>
/// The native callbacks find their subscriber through an id in a static table instead of a <c>GCHandle</c>, so a callback
/// that races with <see cref="Dispose"/> finds nothing instead of touching freed memory.
/// </remarks>
internal sealed unsafe class PowerNotifications : IDisposable
{
    /// <summary><c>GUID_SESSION_DISPLAY_STATUS</c>: 0 off, 1 on, 2 dimmed.</summary>
    public static readonly Guid SessionDisplayStatus = new("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");

    /// <summary><c>GUID_ENERGY_SAVER_STATUS</c> (Windows 11 24H2+): 0 off, 1 standard, 2 high savings.</summary>
    public static readonly Guid EnergySaverStatus = new("550E8400-E29B-41D4-A716-446655440000");

    /// <summary><c>GUID_POWER_SAVING_STATUS</c> (battery saver on older Windows): 0 off, 1 on.</summary>
    public static readonly Guid PowerSavingStatus = new("E00958C0-C213-4ACE-AC77-FECCED2EEEA5");

    private static readonly ConcurrentDictionary<nint, PowerNotifications> Subscribers = new();
    private static long lastId;

    private readonly nint id;
    private readonly Action<Guid, int> onSetting;
    private readonly Action<int> onEffectiveMode;
    private readonly IAppLog log;
    private readonly List<nint> registrations = [];
    private nint effectiveModeRegistration;

    /// <summary>Registers for the settings and the effective power mode.</summary>
    /// <param name="settings">The power setting GUIDs (unsupported ones are skipped).</param>
    /// <param name="onSetting">Receives a setting and its 32-bit value (any thread).</param>
    /// <param name="onEffectiveMode">Receives the effective power mode, <c>EFFECTIVE_POWER_MODE_V2</c> (any thread).</param>
    /// <param name="log">Receives exceptions thrown by the handlers.</param>
    public PowerNotifications(IEnumerable<Guid> settings, Action<Guid, int> onSetting, Action<int> onEffectiveMode, IAppLog log)
    {
        this.onSetting = onSetting;
        this.onEffectiveMode = onEffectiveMode;
        this.log = log;
        id = (nint)Interlocked.Increment(ref lastId);
        Subscribers[id] = this;

        foreach (Guid setting in settings)
        {
            Guid guid = setting;
            var recipient = new DeviceNotifySubscribeParameters { Callback = &OnPowerSetting, Context = id };
            if (NativeMethods.PowerSettingRegisterNotification(&guid, NativeMethods.DEVICE_NOTIFY_CALLBACK, &recipient, out nint registration) == 0)
            {
                registrations.Add(registration);
            }
        }

        if (NativeMethods.PowerRegisterForEffectivePowerModeNotifications(
                NativeMethods.EFFECTIVE_POWER_MODE_V2, &OnEffectivePowerMode, id, out nint modeRegistration) == 0)
        {
            effectiveModeRegistration = modeRegistration;
        }
    }

    /// <summary>Unregisters everything.</summary>
    public void Dispose()
    {
        Subscribers.TryRemove(id, out _);
        foreach (nint registration in registrations)
        {
            NativeMethods.PowerSettingUnregisterNotification(registration);
        }

        registrations.Clear();
        if (effectiveModeRegistration != 0)
        {
            NativeMethods.PowerUnregisterFromEffectivePowerModeNotifications(effectiveModeRegistration);
            effectiveModeRegistration = 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint OnPowerSetting(nint context, uint type, nint setting)
    {
        // POWERBROADCAST_SETTING: GUID PowerSetting; DWORD DataLength; BYTE Data[].
        if (type == NativeMethods.PBT_POWERSETTINGCHANGE && setting != 0 && Subscribers.TryGetValue(context, out PowerNotifications? subscriber))
        {
            Guid guid = *(Guid*)setting;
            uint length = *(uint*)(setting + 16);
            int value = length >= 4 ? *(int*)(setting + 20) : 0;
            subscriber.Deliver(() => subscriber.onSetting(guid, value));
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnEffectivePowerMode(int mode, nint context)
    {
        if (Subscribers.TryGetValue(context, out PowerNotifications? subscriber))
        {
            subscriber.Deliver(() => subscriber.onEffectiveMode(mode));
        }
    }

    /// <summary>Runs a handler; exceptions must never cross the native callback (it would end the process).</summary>
    private void Deliver(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            log.Error("Platform.Pause", "A power notification handler failed.", e);
        }
    }
}
