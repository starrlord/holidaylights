namespace HolidayLights.App.Contracts;

/// <summary>
/// Ids of the Help topics (PRODUCT-SPEC 6.7). The content (branding-docs) lives in <c>HelpContent/topics/&lt;id&gt;.md</c>
/// with the book structure in <c>HelpContent/contents.json</c>; the Help window (app-shell) opens topics by these ids; F1
/// mappings (settings-ui, bulb-factory) use them.
/// </summary>
public static class HelpTopics
{
    /// <summary>Getting Started: Welcome to Holiday Lights.</summary>
    public const string Welcome = "welcome";

    /// <summary>Getting Started: About Holiday Lights.</summary>
    public const string About = "about-holiday-lights";

    /// <summary>Getting Started: Starting Holiday Lights.</summary>
    public const string Starting = "starting-holiday-lights";

    /// <summary>Getting Started: The Taskbar Icon.</summary>
    public const string TaskbarIcon = "taskbar-icon";

    /// <summary>Getting Started: Turning the Lights On and Off.</summary>
    public const string LightsOnOff = "turning-lights-on-off";

    /// <summary>Getting Started: Exiting Holiday Lights.</summary>
    public const string Exiting = "exiting";

    /// <summary>Getting Started: Permanently Removing Holiday Lights.</summary>
    public const string Removing = "removing-holiday-lights";

    /// <summary>Getting Started: Coming from Holiday Lights 5.4.</summary>
    public const string ComingFrom54 = "coming-from-5-4";

    /// <summary>Getting Started: What's New in 6.0.</summary>
    public const string WhatsNew = "whats-new";

    /// <summary>Arranging the Bulbs: About Holiday Lights Bulbs.</summary>
    public const string AboutBulbs = "about-bulbs";

    /// <summary>Arranging the Bulbs: Changing Bulbs in the Settings Window.</summary>
    public const string ChangingBulbs = "changing-bulbs";

    /// <summary>Arranging the Bulbs: Finding Bulbs.</summary>
    public const string FindingBulbs = "finding-bulbs";

    /// <summary>Arranging the Bulbs: Viewing Bulbs by Category.</summary>
    public const string BulbCategories = "bulb-categories";

    /// <summary>Arranging the Bulbs: Displaying Bulbs On the Desktop or Above All Windows.</summary>
    public const string DesktopOrOnTop = "desktop-or-on-top";

    /// <summary>Arranging the Bulbs: Using a Hot Key to Choose Where Bulbs Are Drawn.</summary>
    public const string LocationHotKey = "location-hot-key";

    /// <summary>Arranging the Bulbs: Using the Taskbar Icon Menu to Choose Where Bulbs Are Drawn.</summary>
    public const string TrayMenuLocation = "tray-menu-location";

    /// <summary>Arranging the Bulbs: Bulbs on Several Displays.</summary>
    public const string SeveralDisplays = "several-displays";

    /// <summary>Arranging the Bulbs: Bulb Size, Glow and the Classic 2003 Look.</summary>
    public const string SizeGlowLook = "size-glow-look";

    /// <summary>Arranging the Bulbs: Changing the Flash Pattern.</summary>
    public const string FlashPattern = "flash-pattern";

    /// <summary>Arranging the Bulbs: Changing the Flash Speed.</summary>
    public const string FlashSpeed = "flash-speed";

    /// <summary>Arranging the Bulbs: Adding Your Own Bulbs.</summary>
    public const string AddingBulbs = "adding-bulbs";

    /// <summary>Arranging the Bulbs: Making Your Own Bulbs (F1 in Bulb Editing).</summary>
    public const string MakingBulbs = "making-bulbs";

    /// <summary>Arranging the Bulbs: Sharing Your Bulbs.</summary>
    public const string SharingBulbs = "sharing-bulbs";

    /// <summary>Playing Background Music: About Music.</summary>
    public const string AboutMusic = "about-music";

    /// <summary>Playing Background Music: Turning Music On and Off.</summary>
    public const string MusicOnOff = "music-on-off";

    /// <summary>Playing Background Music: Turning Songs On and Off.</summary>
    public const string SongsOnOff = "songs-on-off";

    /// <summary>Playing Background Music: Choosing When Music is Played.</summary>
    public const string WhenMusicPlays = "when-music-plays";

    /// <summary>Playing Background Music: Adding New Songs.</summary>
    public const string AddingSongs = "adding-songs";

    /// <summary>Playing Background Music: Volume.</summary>
    public const string Volume = "volume";

    /// <summary>Playing Background Music: Making the Lights Dance.</summary>
    public const string LightsDance = "lights-dance";

    /// <summary>Playing Background Music: Solving Music Problems.</summary>
    public const string MusicProblems = "music-problems";

    /// <summary>Using the Screen Saver: About the Screen Saver.</summary>
    public const string AboutScreenSaver = "about-screen-saver";

    /// <summary>Using the Screen Saver: Turning the Screen Saver On or Off.</summary>
    public const string ScreenSaverOnOff = "screen-saver-on-off";

    /// <summary>Using the Screen Saver: Animations and Styles.</summary>
    public const string AnimationsAndStyles = "animations-and-styles";

    /// <summary>Using the Screen Saver: Background Pictures.</summary>
    public const string BackgroundPictures = "background-pictures";

    /// <summary>Using the Screen Saver: Text Message.</summary>
    public const string TextMessage = "text-message";

    /// <summary>Using the Screen Saver: Several Displays.</summary>
    public const string ScreenSaverDisplays = "screen-saver-displays";

    /// <summary>Holiday Lights Themes: About Holiday Lights Themes.</summary>
    public const string AboutThemes = "about-themes";

    /// <summary>Holiday Lights Themes: Changing Themes Automatically on Holidays.</summary>
    public const string AutomaticThemes = "automatic-themes";

    /// <summary>Holiday Lights Themes: Recent Settings.</summary>
    public const string RecentSettings = "recent-settings";

    /// <summary>When the Lights Rest.</summary>
    public const string LightsRest = "when-the-lights-rest";

    /// <summary>Keyboard Shortcuts.</summary>
    public const string KeyboardShortcuts = "keyboard-shortcuts";

    /// <summary>Troubleshooting: I Can't See My Lights.</summary>
    public const string CantSeeLights = "cant-see-lights";

    /// <summary>Troubleshooting: The Taskbar Icon Is Missing.</summary>
    public const string TaskbarIconMissing = "taskbar-icon-missing";

    /// <summary>Troubleshooting: There Is No Music.</summary>
    public const string NoMusic = "no-music";

    /// <summary>Troubleshooting: The Screen Saver Doesn't Start.</summary>
    public const string ScreenSaverDoesNotStart = "screen-saver-does-not-start";

    /// <summary>Troubleshooting: My Hot Key Doesn't Work.</summary>
    public const string HotKeyDoesNotWork = "hot-key-does-not-work";

    /// <summary>Troubleshooting: The Bulbs Are in Front of My Icons.</summary>
    public const string BulbsInFrontOfIcons = "bulbs-in-front-of-icons";

    /// <summary>Credits: Art Copyright Information.</summary>
    public const string ArtCopyright = "art-copyright";

    /// <summary>Credits: Music Copyright Information.</summary>
    public const string MusicCopyright = "music-copyright";

    /// <summary>Credits: Software Credits.</summary>
    public const string SoftwareCredits = "software-credits";

    /// <summary>The topic F1 and the Help button open for a Settings page.</summary>
    /// <param name="page">The page.</param>
    /// <returns>A topic id.</returns>
    public static string ForPage(SettingsPageId page) => page switch
    {
        SettingsPageId.Home => LightsOnOff,
        SettingsPageId.BulbFactory => ChangingBulbs,
        SettingsPageId.MusicBox => AboutMusic,
        SettingsPageId.ScreenSaver => AboutScreenSaver,
        SettingsPageId.Themes => AboutThemes,
        SettingsPageId.General => Starting,
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, null),
    };
}
