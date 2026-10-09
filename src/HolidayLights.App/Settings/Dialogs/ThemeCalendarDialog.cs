using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace HolidayLights.App.Settings.Dialogs;

/// <summary>
/// The Theme Calendar dialog (PRODUCT-SPEC 3.6.4): which holidays switch the theme, their dates, this year's
/// occurrence and the theme of each row; the between-holidays theme and the region. Changes apply at once and are
/// undoable.
/// </summary>
public sealed class ThemeCalendarDialog : DialogWindow
{
    private static readonly string[] MonthNames = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames[..12];

    private readonly IAppServices services;
    private readonly Grid table = new();
    private readonly TextBlock status;
    private readonly ComboBox betweenBox = new() { MinWidth = 180 };
    private readonly ComboBox regionBox = new() { MinWidth = 200 };
    private readonly Dictionary<string, TextBlock> thisYear = [];
    private bool building;

    /// <summary>Creates the dialog.</summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="services">The services.</param>
    public ThemeCalendarDialog(Window? owner, IAppServices services)
        : base(owner, "Theme Calendar")
    {
        ArgumentNullException.ThrowIfNull(services);
        this.services = services;
        SizeToContent = SizeToContent.Manual;
        ResizeMode = ResizeMode.CanResize;
        Width = 820;
        Height = 640;
        MinWidth = 700;
        MinHeight = 480;
        status = Secondary("");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var root = new DockPanel();
        var intro = new TextBlock { Text = "When automatic themes are on, your lights change on these dates.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);
        var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        bottom.Children.Add(Secondary("Check the holidays you celebrate. When dates overlap, the shorter one wins."));
        var choices = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        choices.Children.Add(Labeled("Between holidays, _show", betweenBox));
        choices.Children.Add(Labeled("Holidays _for", regionBox));
        bottom.Children.Add(choices);
        status.Margin = new Thickness(0, 10, 0, 0);
        status.FontSize = 14;
        bottom.Children.Add(status);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        // The scroll bar sits in the dialog's right padding instead of over the Theme column.
        table.Margin = new Thickness(0, 0, 16, 0);
        root.Children.Add(new ScrollViewer
        {
            Content = table,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Margin = new Thickness(0, 0, -16, 0),
        });
        Body = root;
        ((FrameworkElement)Body).Margin = new Thickness(0);
        AddLeftButton("_Reset to Defaults", ResetToDefaults);
        AddButton("Close", isPrimary: true, isCancel: true, Close);
        betweenBox.SelectionChanged += (_, _) => OnBetweenChanged();
        regionBox.SelectionChanged += (_, _) => OnRegionChanged();
        AutomationProperties.SetName(betweenBox, "Between holidays, show");
        AutomationProperties.SetName(regionBox, "Holidays for");
        Build();
    }

    /// <summary>Shows the dialog over the Settings window.</summary>
    /// <param name="host">The Settings window.</param>
    /// <param name="services">The services.</param>
    public static void Show(ISettingsHost host, IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(host);
        new ThemeCalendarDialog(host.Window, services).ShowDialog();
    }

    /// <summary>The rule of a computed row as text: "14 days before Easter to Easter Monday".</summary>
    /// <param name="rule">The rule.</param>
    /// <returns>The text.</returns>
    public static string RuleText(CalendarRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        static string Easter(int offset, bool end) => offset switch
        {
            0 => "Easter Sunday",
            1 when end => "Easter Monday",
            < 0 => string.Create(CultureInfo.CurrentCulture, $"{-offset} {(offset == -1 ? "day" : "days")} before Easter"),
            _ => string.Create(CultureInfo.CurrentCulture, $"{offset} {(offset == 1 ? "day" : "days")} after Easter"),
        };
        return rule.Type switch
        {
            CalendarRuleType.Easter => $"{Easter(rule.FromOffset, false)} to {Easter(rule.ToOffset, true)}",
            CalendarRuleType.UsThanksgiving => $"{MonthDayText(rule.From)} to Thanksgiving Day",
            CalendarRuleType.CaThanksgiving => rule.FromOffset == 0
                ? "Thanksgiving Day"
                : string.Create(CultureInfo.CurrentCulture, $"{-rule.FromOffset} days before Thanksgiving to Thanksgiving Day"),
            CalendarRuleType.AfterUsThanksgiving => $"Day after Thanksgiving to {MonthDayText(rule.To)}",
            CalendarRuleType.Chanukah => "The eight nights of Chanukah",
            _ => $"{MonthDayText(rule.From)} to {MonthDayText(rule.To)}",
        };
    }

    /// <summary>"MM-DD" as "Nov 1".</summary>
    /// <param name="monthDay">The month and day.</param>
    /// <returns>The text, or "" when invalid.</returns>
    public static string MonthDayText(string? monthDay) =>
        TryParseMonthDay(monthDay, out int month, out int day) ? $"{MonthNames[month - 1]} {day.ToString(CultureInfo.CurrentCulture)}" : "";

    private static bool TryParseMonthDay(string? text, out int month, out int day)
    {
        month = 0;
        day = 0;
        return text is { Length: 5 } && text[2] == '-'
            && int.TryParse(text.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out month)
            && int.TryParse(text.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out day)
            && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(2024, month);
    }

    private static string FormatMonthDay(int month, int day) => string.Create(CultureInfo.InvariantCulture, $"{month:00}-{day:00}");

    private static StackPanel Labeled(string label, Control control)
    {
        var text = new Label { Content = label, Target = control, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0, 0, 8, 0) };
        text.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        return new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 24, 6), Children = { text, control } };
    }

    private CalendarSettings Calendar => services.Settings.Current.Calendar;

    private void Build()
    {
        building = true;
        try
        {
            BuildTable();
            string[] themes = [.. services.Themes.Themes.Select(t => t.Name).Order(StringComparer.CurrentCultureIgnoreCase)];
            betweenBox.ItemsSource = themes;
            betweenBox.SelectedItem = themes.FirstOrDefault(t => string.Equals(t, Calendar.Between, StringComparison.CurrentCultureIgnoreCase));
            (string Code, string Name)[] regions = Regions();
            regionBox.ItemsSource = regions.Select(r => r.Name).ToArray();
            regionBox.Tag = regions;
            regionBox.SelectedIndex = Array.FindIndex(regions, r => string.Equals(r.Code, Calendar.Region, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            building = false;
        }

        UpdateTexts();
    }

    private static (string Code, string Name)[] Regions()
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                string code = new RegionInfo(culture.Name).TwoLetterISORegionName;
                if (code.Length == 2 && char.IsLetter(code[0]) && char.IsLetter(code[1]))
                {
                    codes.Add(code.ToUpperInvariant());
                }
            }
            catch (ArgumentException)
            {
                // A culture without a region (none in SpecificCultures on current Windows) is skipped.
            }
        }

        // A region made from its code alone is named in the user's language ("United States"), not in a culture's own.
        return [.. codes.Select(code => (code, new RegionInfo(code).DisplayName)).OrderBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    private void BuildTable()
    {
        table.Children.Clear();
        table.RowDefinitions.Clear();
        table.ColumnDefinitions.Clear();
        foreach (GridLength width in new[] { GridLength.Auto, new GridLength(124), new GridLength(1, GridUnitType.Star), new GridLength(112), new GridLength(160) })
        {
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        }

        string[] headers = ["Use", "Holiday", "Dates", "This Year", "Theme"];
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < headers.Length; i++)
        {
            var header = new TextBlock { Text = headers[i], FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 12, 6) };
            Grid.SetColumn(header, i);
            table.Children.Add(header);
        }

        string[] themes = [.. services.Themes.Themes.Select(t => t.Name).Order(StringComparer.CurrentCultureIgnoreCase)];
        thisYear.Clear();
        int row = 1;
        foreach (CalendarEntry entry in Calendar.Entries)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string id = entry.Id;
            string name = services.Calendar.GetDisplayName(id);
            var use = new CheckBox { IsChecked = entry.Use, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 4) };
            AutomationProperties.SetName(use, "Use " + name);
            // Click, and Checked/Unchecked (UI Automation's Toggle raises no Click; review r1 #34): only a real change applies.
            void OnUseToggled()
            {
                bool on = use.IsChecked == true;
                if (services.Settings.Current.Calendar.Entries.FirstOrDefault(e => e.Id == id) is { } current && current.Use != on)
                {
                    Change(id, e => e with { Use = on }, on ? $"Use {name}" : $"Don't use {name}");
                }
            }

            use.Click += (_, _) => OnUseToggled();
            use.Checked += (_, _) => OnUseToggled();
            use.Unchecked += (_, _) => OnUseToggled();
            Place(use, row, 0);
            Place(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) }, row, 1);
            Place(DatesEditor(entry, name), row, 2);
            var year = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            year.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            thisYear[id] = year;
            Place(year, row, 3);
            var theme = new ComboBox { ItemsSource = themes, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
            theme.SelectedItem = themes.FirstOrDefault(t => string.Equals(t, entry.Theme, StringComparison.CurrentCultureIgnoreCase));
            AutomationProperties.SetName(theme, $"Theme for {name}");
            theme.SelectionChanged += (_, _) =>
            {
                if (!building && theme.SelectedItem is string chosen)
                {
                    Change(id, e => e with { Theme = chosen }, $"Use {chosen} for {name}");
                }
            };
            Place(theme, row, 4);
            row++;
        }
    }

    private void Place(UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        table.Children.Add(element);
    }

    /// <summary>Fixed rows: two month/day pickers; computed rows: the rule and "Use Fixed Dates".</summary>
    private FrameworkElement DatesEditor(CalendarEntry entry, string name)
    {
        string id = entry.Id;
        if (entry.Rule.Type != CalendarRuleType.Fixed)
        {
            var panel = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(new TextBlock { Text = RuleText(entry.Rule), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var link = new Button { Content = "Use Fixed Dates" };
            link.SetResourceReference(StyleProperty, "HL.Style.Button.Link");
            AutomationProperties.SetName(link, $"Use fixed dates for {name}");
            link.Click += (_, _) => UseFixedDates(id, name);
            panel.Children.Add(link);
            return panel;
        }

        TryParseMonthDay(entry.Rule.From, out int fromMonth, out int fromDay);
        TryParseMonthDay(entry.Rule.To, out int toMonth, out int toDay);
        var dates = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 12, 2) };
        (ComboBox fromM, ComboBox fromD) = DatePicker(fromMonth, fromDay, $"{name} start");
        (ComboBox toM, ComboBox toD) = DatePicker(toMonth, toDay, $"{name} end");
        void Commit()
        {
            if (building || fromM.SelectedIndex < 0 || fromD.SelectedIndex < 0 || toM.SelectedIndex < 0 || toD.SelectedIndex < 0)
            {
                return;
            }

            string from = FormatMonthDay(fromM.SelectedIndex + 1, Math.Min(fromD.SelectedIndex + 1, DateTime.DaysInMonth(2024, fromM.SelectedIndex + 1)));
            string to = FormatMonthDay(toM.SelectedIndex + 1, Math.Min(toD.SelectedIndex + 1, DateTime.DaysInMonth(2024, toM.SelectedIndex + 1)));
            Change(id, e => e with { Rule = e.Rule with { From = from, To = to } }, $"Change the dates of {name}");
        }

        foreach (ComboBox box in new[] { fromM, fromD, toM, toD })
        {
            box.SelectionChanged += (_, _) => Commit();
        }

        dates.Children.Add(fromM);
        dates.Children.Add(fromD);
        dates.Children.Add(new TextBlock { Text = "to", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
        dates.Children.Add(toM);
        dates.Children.Add(toD);
        return dates;
    }

    private static (ComboBox Month, ComboBox Day) DatePicker(int month, int day, string name)
    {
        var monthBox = new ComboBox { ItemsSource = MonthNames, SelectedIndex = month - 1, MinWidth = 64, Margin = new Thickness(0, 0, 4, 0) };
        var dayBox = new ComboBox { ItemsSource = Enumerable.Range(1, 31).ToArray(), SelectedIndex = day - 1, MinWidth = 54 };
        AutomationProperties.SetName(monthBox, name + " month");
        AutomationProperties.SetName(dayBox, name + " day");
        return (monthBox, dayBox);
    }

    /// <summary>Applies a change to one row (one undo step; repeated changes of a row coalesce).</summary>
    private void Change(string id, Func<CalendarEntry, CalendarEntry> transform, string description)
    {
        if (building)
        {
            return;
        }

        services.Settings.Update(
            s => s with { Calendar = s.Calendar with { Entries = [.. s.Calendar.Entries.Select(e => e.Id == id ? transform(e) : e)] } },
            SettingsChange.Edit(description));
        UpdateTexts();
    }

    /// <summary>"Use Fixed Dates": a computed row becomes this year's dates as a fixed range.</summary>
    private void UseFixedDates(string id, string name)
    {
        CalendarEntry? entry = Calendar.Entries.FirstOrDefault(e => e.Id == id);
        if (entry is null)
        {
            return;
        }

        HolidayOccurrence occurrence = services.Calendar.GetOccurrence(entry, Calendar.Region, DateOnly.FromDateTime(DateTime.Now));
        var rule = new CalendarRule
        {
            Type = CalendarRuleType.Fixed,
            From = FormatMonthDay(occurrence.Start.Month, occurrence.Start.Day),
            To = FormatMonthDay(occurrence.End.Month, occurrence.End.Day),
        };
        Change(id, e => e with { Rule = rule }, $"Use fixed dates for {name}");
        Rebuild();
    }

    private void OnBetweenChanged()
    {
        if (building || betweenBox.SelectedItem is not string theme)
        {
            return;
        }

        services.Settings.Update(s => s with { Calendar = s.Calendar with { Between = theme } }, SettingsChange.Edit($"Show {theme} between holidays"));
        UpdateTexts();
    }

    /// <summary>"Holidays for": the region-dependent rules (Thanksgiving, Christmas, the seasons) follow the new region; checks and themes stay.</summary>
    private void OnRegionChanged()
    {
        if (building || regionBox.Tag is not (string Code, string Name)[] regions || regionBox.SelectedIndex < 0)
        {
            return;
        }

        string region = regions[regionBox.SelectedIndex].Code;
        IReadOnlyList<CalendarEntry> defaults = services.Calendar.CreateDefaultEntries(region);
        string[] regional = ["thanksgiving", "christmas", "winter", "spring", "summer", "autumn"];
        services.Settings.Update(
            s => s with
            {
                Calendar = s.Calendar with
                {
                    Region = region,
                    Entries = [.. s.Calendar.Entries.Select(e => regional.Contains(e.Id) && defaults.FirstOrDefault(d => d.Id == e.Id) is { } d ? e with { Rule = d.Rule } : e)],
                },
            },
            SettingsChange.Edit($"Use the holidays of {regions[regionBox.SelectedIndex].Name}"));
        Rebuild();
    }

    /// <summary>"Reset to Defaults": every row, the between-holidays theme and the region-derived checks.</summary>
    private void ResetToDefaults()
    {
        string region = Calendar.Region;
        services.Settings.Update(
            s => s with { Calendar = s.Calendar with { Entries = services.Calendar.CreateDefaultEntries(region), Between = ShippedThemeNames.ClassicLights } },
            SettingsChange.Edit("Reset the Theme Calendar"));
        Rebuild();
    }

    private void Rebuild()
    {
        IInputElement? focused = System.Windows.Input.Keyboard.FocusedElement;
        Build();
        if (focused is UIElement { IsVisible: true } element)
        {
            element.Focus();
        }
    }

    private void UpdateTexts()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        CalendarSettings calendar = Calendar;
        foreach (CalendarEntry entry in calendar.Entries)
        {
            if (thisYear.TryGetValue(entry.Id, out TextBlock? text))
            {
                HolidayOccurrence occurrence = services.Calendar.GetOccurrence(entry, calendar.Region, today);
                text.Text = $"{LightsStatusText.ShortDate(occurrence.Start)} - {LightsStatusText.ShortDate(occurrence.End)}";
            }
        }

        CalendarSettings on = calendar with { Enabled = true };
        CalendarResolution resolution = services.Calendar.Resolve(on, today);
        string line = resolution.EntryId is { } id ? $"Today: {services.Calendar.GetDisplayName(id)}." : $"Today: between holidays ({resolution.ThemeName}).";
        if (services.Calendar.GetNextChange(on, today) is { } next)
        {
            string name = next.Resolution.EntryId is { } nextId ? services.Calendar.GetDisplayName(nextId) : next.Resolution.ThemeName;
            line += $" Next: {name} on {LightsStatusText.ShortDate(next.Date)}.";
        }

        status.Text = line;
    }
}
