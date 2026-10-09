using System.Globalization;

namespace HolidayLights.Core.Legacy;

/// <summary>The value names under <c>HKCU\Software\Tiger Technologies\Holiday Lights</c>.</summary>
internal static class LegacyValueNames
{
    /// <summary>The arrangement (32 int32 bulb ids).</summary>
    public const string BulbSettings = "Bulb Settings";

    /// <summary>Ticks of 60 ms per flash step.</summary>
    public const string FlashInterval = "Flash Interval";

    /// <summary>The flash pattern 0-4.</summary>
    public const string FlashPattern = "Flash Pattern";

    /// <summary>0 = On Desktop, else On Top.</summary>
    public const string BulbLocation = "Bulb Location";

    /// <summary>The unchecked songs.</summary>
    public const string DisabledMusic = "Disabled Music";

    /// <summary>The checked songs (the theme copy of the song checks).</summary>
    public const string EnabledMusic = "Enabled Music";

    /// <summary>"Play the Chosen Songs" 0-4.</summary>
    public const string MusicPlay = "Music Play";

    /// <summary>The screen saver animation.</summary>
    public const string SaverModule = "Screen Saver Module";

    /// <summary>The screen saver movement style 0-3.</summary>
    public const string SaverMovementType = "Screen Saver Movement Type";

    /// <summary>The screen saver message.</summary>
    public const string SaverMessage = "Screen Saver Message";

    /// <summary>The message font (LOGFONTA).</summary>
    public const string SaverMessageFont = "Screen Saver Message Font";

    /// <summary>The message colour (COLORREF).</summary>
    public const string SaverMessageColor = "Screen Saver Message Color";

    /// <summary>The background colour (COLORREF).</summary>
    public const string SaverBackgroundColor = "Screen Saver Background Color";

    /// <summary>The background picture name.</summary>
    public const string SaverPictureName = "Screen Saver Picture Name";

    /// <summary>The picture placement 0-2.</summary>
    public const string SaverPictureDisplayType = "Screen Saver Picture Display Type";

    /// <summary>The location hot key switch.</summary>
    public const string HotKeyOnOrOff = "Bulb Location Hot Key On or Off";

    /// <summary>The location hot key's virtual-key code.</summary>
    public const string HotKeyChar = "Bulb Location Hot Key Char";

    /// <summary>Set once the slow-bulb warning was shown.</summary>
    public const string PreventSlowBulbWarning = "Prevent Slow Bulb Warning";

    /// <summary>Values 6.0 does not import: registration, version and caches of 5.4.</summary>
    public static IReadOnlySet<string> Obsolete { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Path", "Current Version", "User", "Serial Number", "Last Converted Picture Name", "Last Converted Picture Time",
        PreventSlowBulbWarning,
    };

    /// <summary>The main-key values in the order of the 5.4 table, for the import report.</summary>
    public static IReadOnlyList<string> ReportOrder { get; } =
    [
        "Path", "Current Version", BulbSettings, FlashInterval, FlashPattern, BulbLocation, DisabledMusic, EnabledMusic,
        MusicPlay, "User", "Serial Number", SaverModule, SaverMessage, SaverMessageFont, SaverMessageColor,
        SaverBackgroundColor, .. Enumerable.Range(0, ColorSettings.CustomColorCount).Select(CustomColor), SaverPictureName,
        SaverPictureDisplayType, SaverMovementType, "Last Converted Picture Name", "Last Converted Picture Time", HotKeyOnOrOff,
        HotKeyChar, PreventSlowBulbWarning,
    ];

    /// <summary>The spelling 5.4 used for a value name (registry names compare case-insensitively).</summary>
    /// <param name="name">A stored value name.</param>
    /// <returns>The canonical name, or <paramref name="name"/> when it is not a 5.4 value.</returns>
    public static string Canonical(string name) =>
        ReportOrder.FirstOrDefault(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase)) ?? name;

    /// <summary>The name of a custom colour value (with two spaces, as 5.4 wrote it: <c>"%s %d"</c> of "Custom Color ").</summary>
    /// <param name="index">0-15.</param>
    /// <returns>E.g. "Custom Color  0".</returns>
    public static string CustomColor(int index) => "Custom Color  " + index.ToString(CultureInfo.InvariantCulture);
}
