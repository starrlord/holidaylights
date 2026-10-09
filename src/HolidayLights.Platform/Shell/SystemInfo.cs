using System.Globalization;
using HolidayLights.Platform.Native;
using Microsoft.Win32;

namespace HolidayLights.Platform.Shell;

/// <summary>Animation effects, High Contrast, taskbar theme, region, account name, Remote Desktop, build (see <see cref="ISystemInfo"/>). Owner: platform.</summary>
/// <remarks>
/// The values are read once and again whenever a hidden top-level window on the creating (UI) thread hears
/// <c>WM_SETTINGCHANGE</c> (animation effects, High Contrast, "ImmersiveColorSet", "intl"), <c>WM_SYSCOLORCHANGE</c> or a
/// session change (a Remote Desktop connect or disconnect); <see cref="Changed"/> is raised only when a value differs.
/// </remarks>
public sealed class SystemInfo : ISystemInfo, IDisposable
{
    private const string LogSource = "Platform.SystemInfo";
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly MessageWindow window;
    private readonly Lazy<string> userDisplayName = new(ReadUserDisplayName);
    private Snapshot values;

    /// <summary>Reads the values and listens for <c>WM_SETTINGCHANGE</c> on the calling (UI) thread.</summary>
    /// <param name="log">The log.</param>
    public SystemInfo(IAppLog log)
    {
        values = Snapshot.Read();
        window = new MessageWindow("Holiday Lights System Settings", OnMessage, log, LogSource);
        if (!NativeMethods.WTSRegisterSessionNotification(window.Handle, NativeMethods.NOTIFY_FOR_THIS_SESSION))
        {
            log.Warn(LogSource, "Remote Desktop connects and disconnects will be noticed at the next settings change only.");
        }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public bool AnimationsEnabled => values.AnimationsEnabled;

    /// <inheritdoc />
    public bool HighContrast => values.HighContrast;

    /// <inheritdoc />
    public bool TaskbarUsesLightTheme => values.TaskbarUsesLightTheme;

    /// <inheritdoc />
    public string RegionCode => values.RegionCode;

    /// <inheritdoc />
    /// <remarks>Read once, on first use: for a domain account Windows may have to ask the domain controller.</remarks>
    public string UserDisplayName => userDisplayName.Value;

    /// <inheritdoc />
    public bool IsRemoteSession => values.IsRemoteSession;

    /// <inheritdoc />
    public int OsBuild => Environment.OSVersion.Version.Build;

    /// <summary>Stops listening.</summary>
    public void Dispose()
    {
        if (window.Handle != 0)
        {
            NativeMethods.WTSUnRegisterSessionNotification(window.Handle);
        }

        window.Dispose();
    }

    /// <summary>The Windows region as an ISO two-letter code: the user's GeoID setting, else the culture's region.</summary>
    /// <returns>For example "US"; "" when unknown.</returns>
    internal static unsafe string ReadRegionCode()
    {
        // Both calls write a null-terminated string; a numeric UN M.49 code ("419") is not a country.
        char* buffer = stackalloc char[16];
        string name = NativeMethods.GetUserDefaultGeoName(buffer, 16) > 0 ? new string(buffer) : "";
        if (IsIsoRegion(name))
        {
            return name.ToUpperInvariant();
        }

        int geoId = NativeMethods.GetUserGeoID(NativeMethods.GEOCLASS_NATION);
        name = NativeMethods.GetGeoInfo(geoId, NativeMethods.GEO_ISO2, buffer, 16, 0) > 0 ? new string(buffer) : "";
        if (IsIsoRegion(name))
        {
            return name.ToUpperInvariant();
        }

        try
        {
            return RegionInfo.CurrentRegion.TwoLetterISORegionName.ToUpperInvariant();
        }
        catch (ArgumentException)
        {
            return "";
        }
    }

    /// <summary>The account's display name (for example "Pat Smith"), else the sign-in name.</summary>
    /// <returns>The name.</returns>
    internal static unsafe string ReadUserDisplayName()
    {
        uint size = 256;
        char* buffer = stackalloc char[256];
        if (NativeMethods.GetUserNameEx(NativeMethods.NAME_DISPLAY, buffer, ref size) && size > 0)
        {
            string name = new string(buffer, 0, (int)size).Trim();
            if (name.Length > 0)
            {
                return name;
            }
        }

        return Environment.UserName;
    }

    private static bool IsIsoRegion(string name) => name.Length == 2 && char.IsAsciiLetter(name[0]) && char.IsAsciiLetter(name[1]);

    private bool OnMessage(uint message, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (message is NativeMethods.WM_SETTINGCHANGE or NativeMethods.WM_SYSCOLORCHANGE or NativeMethods.WM_WTSSESSION_CHANGE)
        {
            Snapshot current = Snapshot.Read();
            if (current != values)
            {
                values = current;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        return false;
    }

    /// <summary>The values at one moment (value equality decides whether <see cref="Changed"/> is raised).</summary>
    private sealed record Snapshot(bool AnimationsEnabled, bool HighContrast, bool TaskbarUsesLightTheme, string RegionCode, bool IsRemoteSession)
    {
        public static unsafe Snapshot Read()
        {
            int animations = 1;
            NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETCLIENTAREAANIMATION, 0, &animations, 0);
            var contrast = new NativeHighContrast { Size = (uint)sizeof(NativeHighContrast) };
            bool highContrast = NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETHIGHCONTRAST, contrast.Size, &contrast, 0) &&
                                (contrast.Flags & NativeMethods.HCF_HIGHCONTRASTON) != 0;
            using RegistryKey? personalize = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            bool lightTaskbar = personalize?.GetValue("SystemUsesLightTheme") is int light && light != 0;
            return new Snapshot(
                animations != 0,
                highContrast,
                lightTaskbar,
                ReadRegionCode(),
                NativeMethods.GetSystemMetrics(NativeMethods.SM_REMOTESESSION) != 0);
        }
    }
}
