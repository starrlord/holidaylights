using System.ComponentModel;
using System.Windows;

namespace HolidayLights.App.Styles;

/// <summary>
/// Keeps the content tokens of <c>Styles/Theme.xaml</c> correct in High Contrast (PRODUCT-SPEC 4.5): night wells and the
/// stage pills become the system Window colour and the text drawn on them the system WindowText colour, and the favorite
/// star the system Highlight colour, live, while every other token keeps its fixed content colour. Works by adding (and removing) top-level overrides to a resource dictionary that merges
/// Theme.xaml, so every <c>DynamicResource</c> consumer updates.
/// </summary>
public static class HighContrastResources
{
    private static readonly List<WeakReference<ResourceDictionary>> Targets = [];
    private static bool subscribed;

    /// <summary>Starts tracking High Contrast for a dictionary (the application's resources, or a window's in tests).</summary>
    /// <param name="resources">A dictionary that merges <c>Styles/Theme.xaml</c>.</param>
    public static void Attach(ResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        lock (Targets)
        {
            Targets.RemoveAll(t => !t.TryGetTarget(out ResourceDictionary? existing) || ReferenceEquals(existing, resources));
            Targets.Add(new WeakReference<ResourceDictionary>(resources));
            if (!subscribed)
            {
                SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
                subscribed = true;
            }
        }

        Apply(resources, SystemParameters.HighContrast);
    }

    /// <summary>Applies or removes the High Contrast overrides.</summary>
    /// <param name="resources">The dictionary.</param>
    /// <param name="highContrast">True when High Contrast is on.</param>
    public static void Apply(ResourceDictionary resources, bool highContrast)
    {
        ArgumentNullException.ThrowIfNull(resources);
        Set(resources, AppResourceKeys.NightWellBrush, highContrast ? SystemColors.WindowBrush : null);
        Set(resources, ThemeKeys.OnNightWellBrush, highContrast ? SystemColors.WindowTextBrush : null);
        Set(resources, ThemeKeys.OnNightWellSecondaryBrush, highContrast ? SystemColors.GrayTextBrush : null);
        Set(resources, ThemeKeys.StagePillBrush, highContrast ? SystemColors.WindowBrush : null);

        // The gold favorite star is about 1.7:1 on a light High Contrast window: use the Highlight colour instead.
        Set(resources, ThemeKeys.FavoriteStarBrush, highContrast ? SystemColors.HighlightBrush : null);
    }

    private static void Set(ResourceDictionary resources, string key, object? value)
    {
        if (value is null)
        {
            if (resources.Contains(key))
            {
                resources.Remove(key);
            }
        }
        else
        {
            resources[key] = value;
        }
    }

    private static void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SystemParameters.HighContrast))
        {
            return;
        }

        List<ResourceDictionary> live = [];
        lock (Targets)
        {
            foreach (WeakReference<ResourceDictionary> target in Targets)
            {
                if (target.TryGetTarget(out ResourceDictionary? dictionary))
                {
                    live.Add(dictionary);
                }
            }
        }

        // SystemParameters raises its change events on the UI thread that handles WM_SETTINGCHANGE.
        foreach (ResourceDictionary dictionary in live)
        {
            Apply(dictionary, SystemParameters.HighContrast);
        }
    }
}
