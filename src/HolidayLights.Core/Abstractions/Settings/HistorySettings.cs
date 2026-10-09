using System.Text.Json.Serialization;

namespace HolidayLights.Core.Abstractions;

/// <summary>One "Recent Settings" entry: the values a theme change replaced (PRODUCT-SPEC 3.6.6). At most 5 are kept, newest first.</summary>
public sealed record RecentSettingsEntry
{
    /// <summary>The label, e.g. "Before Halloween (automatic)" or "Holiday Lights 5.4 Settings".</summary>
    public string Label { get; set; } = "";

    /// <summary>When the values were replaced.</summary>
    public DateTimeOffset Date { get; set; }

    /// <summary>The replaced 13 values.</summary>
    public ThemeableSettings Values { get; set; } = new();
}

/// <summary>Status of one 5.4 item in the import report (PRODUCT-SPEC 6.8.5).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ImportItemStatus>))]
public enum ImportItemStatus
{
    /// <summary>"Imported".</summary>
    [JsonStringEnumMemberName("imported")]
    Imported,

    /// <summary>"Already included".</summary>
    [JsonStringEnumMemberName("alreadyIncluded")]
    AlreadyIncluded,

    /// <summary>"Not imported - &lt;reason&gt;".</summary>
    [JsonStringEnumMemberName("notImported")]
    NotImported,
}

/// <summary>One line of the 5.4 import report ("Import Details").</summary>
public sealed record ImportReportItem
{
    /// <summary>The 5.4 item, e.g. <c>Flash Interval</c>, <c>Themes\Grandma's Lights</c>, <c>Holiday Lights Bulbs\Star.bul</c>.</summary>
    public string Item { get; set; } = "";

    /// <summary>The status.</summary>
    public ImportItemStatus Status { get; set; }

    /// <summary>The reason for <see cref="ImportItemStatus.NotImported"/> ("obsolete", "unknown bulb id 12345").</summary>
    public string? Reason { get; set; }
}

/// <summary>Settings key <c>import54</c>: the result of the last Holiday Lights 5.4 import.</summary>
public sealed record Import54Record
{
    /// <summary>When the import ran.</summary>
    public DateOnly Date { get; set; }

    /// <summary>True when 5.4 was at its factory defaults (PRODUCT-SPEC 2.5.2): the newcomer look was applied.</summary>
    public bool FactoryDefaults { get; set; }

    /// <summary>Number of themes imported (General status line).</summary>
    public int Themes { get; set; }

    /// <summary>Number of bulb files imported.</summary>
    public int Bulbs { get; set; }

    /// <summary>Number of songs imported.</summary>
    public int Songs { get; set; }

    /// <summary>Number of pictures imported.</summary>
    public int Pictures { get; set; }

    /// <summary>Every 5.4 item and what happened to it.</summary>
    public IReadOnlyList<ImportReportItem> Report { get; set; } = [];
}

/// <summary>Settings key <c>onboarding</c>: one-time hints and notification throttling.</summary>
public sealed record OnboardingState
{
    /// <summary>The Welcome card was shown (it is shown once).</summary>
    public bool WelcomeShown { get; set; }

    /// <summary>The "Your lights stay on" notification was shown (first close of Settings).</summary>
    public bool CloseNotified { get; set; }

    /// <summary>How often the location hot key showed its second pill line (the first three uses).</summary>
    public int LocationHotKeyHints { get; set; }

    /// <summary>The "Automatic themes are on: on &lt;date&gt; your lights change to &lt;theme&gt;." InfoBar was shown.</summary>
    public bool AutomaticEditHintShown { get; set; }

    /// <summary>The one-time "Holiday Lights 5.4 used Ctrl+Shift+B ..." InfoBar was dismissed or acted on.</summary>
    public bool HotKeyImportNoticeDismissed { get; set; }

    /// <summary>Last day each notification kind was shown (at most one per kind per day), keyed by kind name.</summary>
    public IReadOnlyDictionary<string, DateOnly> LastNotified { get; set; } = new Dictionary<string, DateOnly>();
}
