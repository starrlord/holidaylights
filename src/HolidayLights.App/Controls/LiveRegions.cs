using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace HolidayLights.App.Controls;

/// <summary>
/// Makes text live regions speak (PRODUCT-SPEC 3.0.4): WPF does not raise <c>LiveRegionChanged</c> when the text of a
/// <see cref="TextBlock"/> changes, so Narrator and NVDA would never read a result line ("37 bulbs match "snow"") or a
/// status line. Every <see cref="TextBlock"/> whose <see cref="AutomationProperties.LiveSettingProperty"/> is Polite or
/// Assertive - in any window, set in XAML or in code - raises it after its text changes while it is shown.
/// </summary>
public static class LiveRegions
{
    /// <summary>A copy of the watched text block's <see cref="TextBlock.Text"/> (bound), whose changes raise the event.</summary>
    private static readonly DependencyProperty WatchedTextProperty =
        DependencyProperty.RegisterAttached("WatchedText", typeof(string), typeof(LiveRegions), new PropertyMetadata(null, OnWatchedTextChanged));

    /// <summary>True while a raise is queued (several quick changes are read once, with the last text).</summary>
    private static readonly DependencyProperty PendingProperty =
        DependencyProperty.RegisterAttached("Pending", typeof(bool), typeof(LiveRegions), new PropertyMetadata(false));

    private static bool installed;

    /// <summary>True when text blocks announce their live text (the hook is in place).</summary>
    public static bool IsInstalled => installed;

    /// <summary>
    /// Puts the hook in place: it watches the live setting of every text block. Runs when the application's assembly loads,
    /// before any XAML (a later call does nothing).
    /// </summary>
    [ModuleInitializer]
    internal static void Install()
    {
        if (installed)
        {
            return;
        }

        try
        {
            AutomationProperties.LiveSettingProperty.OverrideMetadata(
                typeof(TextBlock), new FrameworkPropertyMetadata(AutomationLiveSetting.Off, OnLiveSettingChanged));
            installed = true;
        }
        catch (ArgumentException)
        {
            // Metadata for TextBlock was already in use (a host created live text blocks before loading this assembly).
        }
    }

    private static void OnLiveSettingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock text)
        {
            return;
        }

        if ((AutomationLiveSetting)e.NewValue == AutomationLiveSetting.Off)
        {
            BindingOperations.ClearBinding(text, WatchedTextProperty);
        }
        else if (!BindingOperations.IsDataBound(text, WatchedTextProperty))
        {
            BindingOperations.SetBinding(text, WatchedTextProperty, new Binding(nameof(TextBlock.Text)) { Source = text, Mode = BindingMode.OneWay });
        }
    }

    private static void OnWatchedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock text || !text.IsVisible || string.IsNullOrEmpty(e.NewValue as string)
            || AutomationProperties.GetLiveSetting(text) == AutomationLiveSetting.Off || (bool)text.GetValue(PendingProperty))
        {
            return;
        }

        text.SetValue(PendingProperty, true);
        text.Dispatcher.InvokeAsync(
            () =>
            {
                text.SetValue(PendingProperty, false);
                if (text.IsVisible && !string.IsNullOrEmpty(text.Text))
                {
                    Announcer.RaiseLiveRegionChanged(text);
                }
            },
            DispatcherPriority.Background);
    }
}
