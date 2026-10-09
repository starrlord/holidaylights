using System.Text.Json.Serialization;

namespace HolidayLights.Core.Abstractions;

/// <summary>
/// Every persisted setting of Holiday Lights 6 (<c>%APPDATA%\Holiday Lights\settings.json</c>, PRODUCT-SPEC Appendix C,
/// defaults 7.1). Immutable: change it with <c>with</c> expressions through <see cref="ISettingsStore.Update"/>.
/// </summary>
/// <remarks>
/// <para>Property initializers hold the static defaults: the 5.4 defaults for theme values and the newcomer defaults for
/// device preferences. Context-dependent first-run values (the region, the flash limit when Windows animation effects
/// are off, today's Automatic theme, 5.4 imports) are applied by core-settings when the file is first created.</para>
/// <para>JSON names are camel case (source-generated, <see cref="HolidayLightsJsonContext"/>); a missing value keeps its
/// default, unknown values are ignored.</para>
/// <para>Immutability is a convention: the persisted contract types have setters only because System.Text.Json source
/// generation cannot keep the initializer defaults of <c>init</c> properties. Never modify an instance that was published
/// (<see cref="ISettingsStore.Current"/>, event arguments, snapshots); build changed copies with <c>with</c>.</para>
/// </remarks>
public sealed record AppSettings
{
    /// <summary>The file format version written by this build (drives migrations).</summary>
    public const int CurrentVersion = 1;

    /// <summary>The file format version.</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>Show Lights, Bulb Drawing, displays, frame mode, bulb size.</summary>
    public LightsSettings Lights { get; set; } = new();

    /// <summary>Pixels, glow, Smooth Fading, Smooth Screen Saver Motion.</summary>
    public LookSettings Look { get; set; } = new();

    /// <summary>The 13 values themes replace.</summary>
    public ThemeableSettings Current { get; set; } = new();

    /// <summary>Play Holiday Music, volume, MIDI output, sync offset.</summary>
    public MusicSettings Music { get; set; } = new();

    /// <summary>Screen saver "Show On" and the remembered previous Windows screen saver.</summary>
    public SaverDeviceSettings Saver { get; set; } = new();

    /// <summary>The 16 custom colours.</summary>
    public ColorSettings Colors { get; set; } = new();

    /// <summary>Automatic themes and the Theme Calendar.</summary>
    public CalendarSettings Calendar { get; set; } = new();

    /// <summary>The two global hot keys.</summary>
    [JsonPropertyName("hotkeys")]
    public HotKeySettings HotKeys { get; set; } = new();

    /// <summary>"Automatically Start Holiday Lights".</summary>
    public StartupSettings Startup { get; set; } = new();

    /// <summary>"When the Lights Rest".</summary>
    public RestSettings Rest { get; set; } = new();

    /// <summary>"Limit Flashing to 3 Flashes per Second".</summary>
    public AccessibilitySettings Accessibility { get; set; } = new();

    /// <summary>Window decoration, Settings window state, Bulb List view and sort.</summary>
    public UiSettings Ui { get; set; } = new();

    /// <summary>Favorites, hidden bundled bulbs, category overrides.</summary>
    public BulbPreferences Bulbs { get; set; } = new();

    /// <summary>Hidden bundled songs.</summary>
    public HiddenItems Songs { get; set; } = new();

    /// <summary>Hidden bundled pictures.</summary>
    public HiddenItems Pictures { get; set; } = new();

    /// <summary>The <c>.bul</c> association switch.</summary>
    public FileSettings Files { get; set; } = new();

    /// <summary>The Save Theme pre-fill.</summary>
    public ThemePreferences Themes { get; set; } = new();

    /// <summary>"Recent Settings", newest first, at most 5.</summary>
    public IReadOnlyList<RecentSettingsEntry> RecentSettings { get; set; } = [];

    /// <summary>The last Holiday Lights 5.4 import, or null when none ran.</summary>
    public Import54Record? Import54 { get; set; }

    /// <summary>One-time hints and notification throttling.</summary>
    public OnboardingState Onboarding { get; set; } = new();
}
