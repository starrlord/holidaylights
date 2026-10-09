using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace HolidayLights.App.Settings.Groups;

/// <summary>
/// "Flash Settings" (PRODUCT-SPEC 3.2.8): the pattern combo with a 5-bulb animated demo and a second line per pattern, the
/// 9-position speed slider (position p gives interval 10 - p, the 5.4 mapping) and Smooth Fading. Bound to the settings:
/// the Home and Bulb Factory copies always agree.
/// </summary>
public partial class FlashSettingsGroup : UserControl
{
    /// <summary>The fastest position allowed while "Limit Flashing to 3 Flashes per Second" is on (interval 3).</summary>
    public const int LimitedMaxPosition = 10 - HolidayLights.Core.Flash.FlashClock.LimitedMinimumInterval;

    private const string StandardBulbs = "builtin:standard-bulbs";
    private const string MiniBulbs = "builtin:mini-bulbs";

    private readonly List<FlashPatternId?> itemPatterns = [];
    private IAppServices? services;
    private bool refreshing;

    /// <summary>Creates the group.</summary>
    public FlashSettingsGroup()
    {
        InitializeComponent();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    /// <summary>The services (set by the page).</summary>
    public IAppServices? Services
    {
        get => services;
        set
        {
            Detach();
            services = value;
            if (IsLoaded)
            {
                Attach();
            }
        }
    }

    /// <summary>The second line of a pattern in the combo (PRODUCT-SPEC 3.2.8).</summary>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The sentence.</returns>
    public static string Explanation(FlashPatternId pattern) => pattern switch
    {
        FlashPatternId.DontFlash => "The bulbs stay lit.",
        FlashPatternId.FlashTogether => "All bulbs change at the same time.",
        FlashPatternId.Alternating => "Every other bulb flashes.",
        FlashPatternId.BulbChase => "The lights run along each edge.",
        FlashPatternId.RandomFlashing => "Bulbs flash at random, repeating every 8 steps like the original.",
        FlashPatternId.Twinkle => "Each light twinkles on its own, like real twinkle lights.",
        FlashPatternId.SlowGlow => "All lights slowly brighten and dim together.",
        FlashPatternId.ChaseAround => "One light in three runs clockwise around the whole screen.",
        FlashPatternId.DanceToMusic => "The lights play along with the music. Between songs: Slow Glow.",
        FlashPatternId.Waves => "Waves of light travel around the screen.",
        FlashPatternId.Combination => "A different pattern every few seconds.",
        _ => "",
    };

    /// <summary>The value text under the slider: "One step every 0.30 s".</summary>
    /// <param name="interval">The interval (1-9).</param>
    /// <returns>The text.</returns>
    public static string StepText(int interval) =>
        string.Format(CultureInfo.CurrentCulture, "One step every {0:0.00} s", interval * StepClock.TickMilliseconds / 1000.0);

    private void Attach()
    {
        if (services is null)
        {
            return;
        }

        if (itemPatterns.Count == 0)
        {
            BuildItems(services);
        }

        services.Settings.Changed -= OnSettingsChanged;
        services.Settings.Changed += OnSettingsChanged;
        Refresh();
    }

    private void Detach()
    {
        if (services is not null)
        {
            services.Settings.Changed -= OnSettingsChanged;
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => Refresh();

    /// <summary>
    /// "New in 6.0": the four MUST patterns, then "Waves" and "Combination", which the spec lists after "Dance to the
    /// Music" once built (the flash engine and the presenter draw both).
    /// </summary>
    private static IEnumerable<FlashPatternId> NewPatterns() =>
        FlashPatterns.New.Concat(new[] { FlashPatternId.Waves, FlashPatternId.Combination }.Except(FlashPatterns.New));

    /// <summary>Fills the combo: "Classic" with the five 5.4 patterns, then "New in 6.0".</summary>
    private void BuildItems(IAppServices services)
    {
        AddHeader("Classic");
        foreach (FlashPatternId pattern in FlashPatterns.Classic)
        {
            AddPattern(new PatternChoice(pattern, StandardBulbs, services));
        }

        AddHeader("New in 6.0");
        foreach (FlashPatternId pattern in NewPatterns())
        {
            AddPattern(new PatternChoice(pattern, MiniBulbs, services));
        }
    }

    private void AddHeader(string text)
    {
        var header = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) };
        header.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        PatternCombo.Items.Add(new ComboBoxItem { Content = header, IsEnabled = false, Focusable = false, IsTabStop = false });
        itemPatterns.Add(null);
    }

    private void AddPattern(PatternChoice choice)
    {
        var item = new ComboBoxItem { Content = choice, ContentTemplate = (DataTemplate)FindResource("PatternChoiceTemplate") };
        System.Windows.Automation.AutomationProperties.SetName(item, choice.Name);
        System.Windows.Automation.AutomationProperties.SetHelpText(item, choice.Explanation);
        PatternCombo.Items.Add(item);
        itemPatterns.Add(choice.Pattern);
    }

    /// <summary>Shows the settings.</summary>
    private void Refresh()
    {
        if (services is null)
        {
            return;
        }

        refreshing = true;
        try
        {
            AppSettings settings = services.Settings.Current;
            FlashPatternId pattern = settings.Current.Flash.Pattern;
            bool limited = settings.Accessibility.LimitFlashing;
            int index = itemPatterns.IndexOf(pattern);
            PatternCombo.SelectedIndex = index;

            int interval = HolidayLights.Core.Flash.FlashClock.LimitInterval(settings.Current.Flash.Interval, limited);
            int position = FlashSettings.SliderPositionFromInterval(interval);
            SpeedSlider.Value = position;
            bool flashes = pattern != FlashPatternId.DontFlash;
            SpeedSlider.IsEnabled = flashes;
            SlowText.Opacity = FastText.Opacity = flashes ? 1 : 0.5;
            SpeedLabel.Text = pattern == FlashPatternId.DanceToMusic ? "Speed Between Songs" : "Speed";
            SpeedValue.Text = flashes ? StepText(interval) : "Speed doesn't apply when bulbs don't flash.";
            string? note = limited
                ? "Faster speeds are off because Limit Flashing is on (General)."
                : flashes && position >= 8 ? "Very fast flashing can be uncomfortable for some people." : null;
            SpeedNote.Text = note ?? "";
            SpeedNote.Visibility = note is null ? Visibility.Collapsed : Visibility.Visible;
            SpeedSlider.SetValue(System.Windows.Automation.AutomationProperties.HelpTextProperty, SpeedValue.Text);

            SmoothFadingCheck.IsChecked = settings.Look.SmoothFading || limited;
            SmoothFadingCheck.IsEnabled = !limited;
        }
        finally
        {
            refreshing = false;
        }
    }

    private void OnPatternSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || services is null || PatternCombo.SelectedIndex < 0 || itemPatterns[PatternCombo.SelectedIndex] is not { } pattern)
        {
            return;
        }

        services.Settings.Update(
            s => s.Current.Flash.Pattern == pattern
                ? s
                : s with
                {
                    Current = s.Current with { Flash = s.Current.Flash with { Pattern = pattern } },
                    Lights = pattern == FlashPatternId.DanceToMusic ? s.Lights with { PatternBeforeDance = s.Current.Flash.Pattern } : s.Lights,
                },
            SettingsChange.Edit($"Change the flash pattern to {FlashPatterns.DisplayName(pattern)}"));
    }

    private void OnSpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (refreshing || services is null)
        {
            return;
        }

        int position = (int)Math.Round(e.NewValue);
        if (services.Settings.Current.Accessibility.LimitFlashing && position > LimitedMaxPosition)
        {
            SpeedSlider.Value = LimitedMaxPosition;
            return;
        }

        int interval = FlashSettings.IntervalFromSliderPosition(position);
        services.Settings.Update(
            s => s.Current.Flash.Interval == interval ? s : s with { Current = s.Current with { Flash = s.Current.Flash with { Interval = interval } } },
            SettingsChange.Edit("Change the flash speed"));
    }

    /// <summary>Click, and Checked/Unchecked (UI Automation's Toggle raises no Click; review r1 #34); never while the settings are shown.</summary>
    private void OnSmoothFadingClick(object sender, RoutedEventArgs e)
    {
        if (services is null || refreshing || !SmoothFadingCheck.IsEnabled)
        {
            return;
        }

        bool on = SmoothFadingCheck.IsChecked == true;
        services.Settings.Update(
            s => s.Look.SmoothFading == on ? s : s with { Look = s.Look with { SmoothFading = on } },
            SettingsChange.Edit(on ? "Turn Smooth Fading on" : "Turn Smooth Fading off"));
    }
}
