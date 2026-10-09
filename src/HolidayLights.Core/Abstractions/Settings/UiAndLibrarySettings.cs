using System.Text.Json.Serialization;

namespace HolidayLights.Core.Abstractions;

/// <summary>The pages of the Settings window. JSON and command-line names: home, bulbs, music, saver, themes, general.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SettingsPageId>))]
public enum SettingsPageId
{
    /// <summary>"Home" (new).</summary>
    [JsonStringEnumMemberName("home")]
    Home,

    /// <summary>"Bulb Factory" (5.4 tab 0).</summary>
    [JsonStringEnumMemberName("bulbs")]
    BulbFactory,

    /// <summary>"Music Box" (5.4 tab 1).</summary>
    [JsonStringEnumMemberName("music")]
    MusicBox,

    /// <summary>"Screen Saver" (5.4 tab 2).</summary>
    [JsonStringEnumMemberName("saver")]
    ScreenSaver,

    /// <summary>"Themes" (in the place of the 5.4 "Sharing" tab).</summary>
    [JsonStringEnumMemberName("themes")]
    Themes,

    /// <summary>"General" (5.4 tab 4).</summary>
    [JsonStringEnumMemberName("general")]
    General,
}

/// <summary>Bulb List view (persisted as <c>ui.gallery.view</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BulbListView>))]
public enum BulbListView
{
    /// <summary>"Tiles" (default).</summary>
    [JsonStringEnumMemberName("tiles")]
    Tiles,

    /// <summary>"Details": the 5.4 row.</summary>
    [JsonStringEnumMemberName("details")]
    Details,
}

/// <summary>Remembered size and position of the Settings window, in DIP of the virtual screen.</summary>
public sealed record WindowPlacementSettings
{
    /// <summary>Left edge.</summary>
    public double Left { get; set; }

    /// <summary>Top edge.</summary>
    public double Top { get; set; }

    /// <summary>Width (minimum 760).</summary>
    public double Width { get; set; } = 1240;

    /// <summary>Height (minimum 560).</summary>
    public double Height { get; set; } = 800;

    /// <summary>True when the window was maximized.</summary>
    public bool Maximized { get; set; }
}

/// <summary>Settings key <c>ui.settings</c>.</summary>
public sealed record SettingsWindowSettings
{
    /// <summary>The last page used (opened again by the tray and a second launch).</summary>
    public SettingsPageId LastPage { get; set; } = SettingsPageId.Home;

    /// <summary>Remembered placement; null until the window was first closed (then it opens centred on the pointer's display).</summary>
    public WindowPlacementSettings? Window { get; set; }
}

/// <summary>Settings key <c>ui.gallery</c> (the Show filter is not stored: it resets to All Bulbs).</summary>
public sealed record GallerySettings
{
    /// <summary>Tiles or Details.</summary>
    public BulbListView View { get; set; } = BulbListView.Tiles;

    /// <summary>Sort order.</summary>
    public BulbSortOrder Sort { get; set; } = BulbSortOrder.Original;
}

/// <summary>Settings key <c>ui</c>.</summary>
public sealed record UiSettings
{
    /// <summary>"Decorate the Settings Window with Lights" (the string of lights).</summary>
    public bool DecorateWindow { get; set; } = true;

    /// <summary>Settings window state.</summary>
    public SettingsWindowSettings Settings { get; set; } = new();

    /// <summary>Bulb List view and sort.</summary>
    public GallerySettings Gallery { get; set; } = new();
}

/// <summary>Settings key <c>bulbs</c>: per-bulb user data (never written into bulb files).</summary>
public sealed record BulbPreferences
{
    /// <summary>Favorite bulb ids.</summary>
    public IReadOnlyList<string> Favorites { get; set; } = [];

    /// <summary>Bundled bulbs the user removed (hidden from the Bulb List; arrangements and themes keep them).</summary>
    public IReadOnlyList<string> Hidden { get; set; } = [];

    /// <summary>Category overrides by bulb id (built-in and other people's bulbs; imported 5.4 "Included Bulb Categories").</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> CategoryOverrides { get; set; } = new Dictionary<string, IReadOnlyList<string>>();
}

/// <summary>Settings keys <c>songs</c> and <c>pictures</c>: bundled items the user removed.</summary>
public sealed record HiddenItems
{
    /// <summary>Ids of hidden bundled items (<see cref="MediaIds"/>).</summary>
    public IReadOnlyList<string> Hidden { get; set; } = [];
}

/// <summary>Settings key <c>themes</c>.</summary>
public sealed record ThemePreferences
{
    /// <summary>The last loaded or saved theme name (Save Theme pre-fill, 5.4).</summary>
    public string LastName { get; set; } = "";
}
