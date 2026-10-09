namespace HolidayLights.App.Contracts;

/// <summary>Pack URIs of the branding art (files owned by branding-docs in <c>Assets/</c>).</summary>
public static class AppAssets
{
    /// <summary>The application icon (16-256 px).</summary>
    public const string AppIcon = "pack://application:,,,/Assets/HolidayLights.ico";

    /// <summary>Tray icon: lights on, light taskbar (16/20/24/32 px).</summary>
    public const string TrayLitLight = "pack://application:,,,/Assets/Tray/TrayLitLight.ico";

    /// <summary>Tray icon: lights on, dark taskbar.</summary>
    public const string TrayLitDark = "pack://application:,,,/Assets/Tray/TrayLitDark.ico";

    /// <summary>Tray icon: lights off, light taskbar.</summary>
    public const string TrayUnlitLight = "pack://application:,,,/Assets/Tray/TrayUnlitLight.ico";

    /// <summary>Tray icon: lights off, dark taskbar.</summary>
    public const string TrayUnlitDark = "pack://application:,,,/Assets/Tray/TrayUnlitDark.ico";

    /// <summary>The XAML dictionary of vector illustrations (merged by <c>App.xaml</c>).</summary>
    public const string IllustrationsDictionary = "pack://application:,,,/Assets/Illustrations.xaml";

    /// <summary>Descriptions and tooltips (PRODUCT-SPEC Appendix A), keys <c>HL.Tip.&lt;Area&gt;.&lt;Control&gt;</c>; merged by <c>Illustrations.xaml</c>.</summary>
    public const string TooltipsDictionary = "pack://application:,,,/Assets/Tooltips.xaml";

    /// <summary>
    /// The 2003 ABOUT banner with "Modern Edition 6.0" (PRODUCT-SPEC 3.9), MMPX-enlarged 4x (1920 x 376 px); show at
    /// 480 x 94 DIP with <c>RenderOptions.BitmapScalingMode="Fant"</c>.
    /// </summary>
    public const string AboutBanner = "pack://application:,,,/Assets/Banner/AboutBanner.png";

    /// <summary>Light strip above the banner, frame 1 (4x; show at 296 x 15 DIP); alternate with <see cref="AboutFlash2"/> every 500 ms.</summary>
    public const string AboutFlash1 = "pack://application:,,,/Assets/Banner/AboutFlash1.png";

    /// <summary>Light strip above the banner, frame 2.</summary>
    public const string AboutFlash2 = "pack://application:,,,/Assets/Banner/AboutFlash2.png";

    /// <summary>The 2003 help banner bm0, MMPX-enlarged 4x (show at 216 x 53 DIP) for the top of every Help topic.</summary>
    public const string HelpBanner = "pack://application:,,,/Assets/Banner/HelpBanner.png";
}

/// <summary>
/// Keys of resources merged by <c>App.xaml</c>: tokens and styles (settings-ui, <c>Styles/Theme.xaml</c>) and
/// illustrations (branding-docs, <c>Assets/Illustrations.xaml</c>). Use them with <c>DynamicResource</c>.
/// </summary>
public static class AppResourceKeys
{
    /// <summary>Brush: the night well behind bulb art (#14203A to #1F3157; High Contrast: Window colour).</summary>
    public const string NightWellBrush = "HL.Brush.NightWell";

    /// <summary>Brush: the night gradient of stages without a wallpaper (#0B1530 to #1D3466).</summary>
    public const string NightGradientBrush = "HL.Brush.NightGradient";

    /// <summary>Brush: the taskbar band in stages (#202020 at 85 %).</summary>
    public const string TaskbarBandBrush = "HL.Brush.TaskbarBand";

    /// <summary>Brush: the stage bezel (#3A3A3A).</summary>
    public const string StageBezelBrush = "HL.Brush.StageBezel";

    /// <summary>Brush: the heritage sky behind the 5.4 ABOUT banner (#DDEEFF).</summary>
    public const string HeritageSkyBrush = "HL.Brush.HeritageSky";

    /// <summary>Style (Border): a Fluent card (8 DIP radius, 16 DIP padding).</summary>
    public const string CardStyle = "HL.Style.Card";

    /// <summary>Style (CheckBox): the ToggleSwitch template ("On"/"Off").</summary>
    public const string ToggleSwitchStyle = "HL.Style.ToggleSwitch";

    /// <summary>Style (TextBlock): Title 28 (page titles, the Home greeting).</summary>
    public const string TitleTextStyle = "HL.Style.Text.Title";

    /// <summary>Style (TextBlock): Subtitle 20.</summary>
    public const string SubtitleTextStyle = "HL.Style.Text.Subtitle";

    /// <summary>Style (TextBlock): BodyStrong 14 (group headers).</summary>
    public const string BodyStrongTextStyle = "HL.Style.Text.BodyStrong";

    /// <summary>Style (TextBlock): Caption 12.</summary>
    public const string CaptionTextStyle = "HL.Style.Text.Caption";

    /// <summary>Style (TextBlock): secondary text (descriptions under groups and controls).</summary>
    public const string SecondaryTextStyle = "HL.Style.Text.Secondary";

    /// <summary>DrawingImage 160 x 96: "On Desktop, Behind the Icons" (General > Where Bulbs Are Drawn).</summary>
    public const string IllustrationBehindIcons = "HL.Illustration.BehindIcons";

    /// <summary>DrawingImage 160 x 96: "On Desktop, In Front of the Icons".</summary>
    public const string IllustrationInFrontOfIcons = "HL.Illustration.InFrontOfIcons";

    /// <summary>DrawingImage 160 x 96: "On Top of All Windows".</summary>
    public const string IllustrationOnTop = "HL.Illustration.OnTop";

    /// <summary>DrawingImage: the Windows 11 taskbar corner with the "^" flyout and the red bulb (Welcome card), light mode.</summary>
    public const string IllustrationTaskbarCornerLight = "HL.Illustration.TaskbarCorner.Light";

    /// <summary>DrawingImage: the taskbar corner picture, dark mode.</summary>
    public const string IllustrationTaskbarCornerDark = "HL.Illustration.TaskbarCorner.Dark";
}
