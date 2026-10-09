using System.Windows;
using HolidayLights.App.Controls;
using HolidayLights.App.Settings.Undo;

namespace HolidayLights.App.Settings;

/// <summary>A page of the Settings window (PRODUCT-SPEC 2.3 page skeleton): header texts and actions, help and requests.</summary>
public interface ISettingsPage
{
    /// <summary>Which page this is.</summary>
    SettingsPageId PageId { get; }

    /// <summary>The page title ("Bulb Factory"; Home: the greeting "Happy Halloween!").</summary>
    string Title { get; }

    /// <summary>The one-line description under the title.</summary>
    string Description { get; }

    /// <summary>The page's own header actions ("Peek", "Clear All Bulbs"), or null.</summary>
    FrameworkElement? HeaderActions { get; }

    /// <summary>True for pages that scroll as a whole (all but Bulb Factory, which scrolls inside its columns).</summary>
    bool ScrollsAsWhole { get; }

    /// <summary>Raised when <see cref="Title"/> or <see cref="Description"/> changed.</summary>
    event EventHandler? HeaderChanged;

    /// <summary>Connects the page to its window (called once, before the page is shown).</summary>
    /// <param name="host">The window's services for pages.</param>
    void Attach(ISettingsHost host);

    /// <summary>The page became the visible page (also when the window opens on it).</summary>
    void OnShown();

    /// <summary>Another page replaced it.</summary>
    void OnHidden();

    /// <summary>Does something the caller of the window asked for (open files, show an InfoBar, ask a confirmation).</summary>
    /// <param name="request">The request.</param>
    void HandleRequest(SettingsRequest request);

    /// <summary>The Help topic for F1 with focus on an element of the page (its section's topic, else the page's).</summary>
    /// <param name="focused">The focused element, or null.</param>
    /// <returns>A <see cref="HelpTopics"/> id.</returns>
    string HelpTopicFor(DependencyObject? focused);
}

/// <summary>What the Settings window offers its pages.</summary>
public interface ISettingsHost
{
    /// <summary>The window (owner of dialogs).</summary>
    Window Window { get; }

    /// <summary>The Undo history of this opening.</summary>
    UndoHistory History { get; }

    /// <summary>The Cancel snapshot.</summary>
    AppSettings Snapshot { get; }

    /// <summary>Shows a snackbar at the bottom of the page (PRODUCT-SPEC 3.0.1): one sentence and at most one action.</summary>
    /// <param name="message">The sentence.</param>
    /// <param name="actionText">"Undo" or "Show", or null.</param>
    /// <param name="action">What the action does.</param>
    void ShowSnackbar(string message, string? actionText = null, Action? action = null);

    /// <summary>Undoes the last step (the "Undo" of a snackbar or InfoBar) and announces it.</summary>
    void UndoLast();

    /// <summary>
    /// Shows a sentence in the InfoBar above the page: the problems of dialogs and of actions that have no InfoBar of their
    /// own ("Couldn't restore Halloween: &lt;Windows reason&gt;").
    /// </summary>
    /// <param name="text">The sentence.</param>
    /// <param name="severity">The severity.</param>
    void ShowMessage(string text, InfoBarSeverity severity);

    /// <summary>Raises a polite screen-reader notification ("Candy Canes now on the whole frame.").</summary>
    /// <param name="text">The sentence.</param>
    void Announce(string text);

    /// <summary>Shows another page, optionally with a request for it.</summary>
    /// <param name="page">The page.</param>
    /// <param name="request">A request, or null.</param>
    void Navigate(SettingsPageId page, SettingsRequest? request = null);

    /// <summary>Hides the window for a moment so the desktop shows (Peek, PRODUCT-SPEC 3.2.3).</summary>
    Peek Peek { get; }

    /// <summary>
    /// Adds files dropped or opened anywhere in the window (PRODUCT-SPEC 3.2.7): bulb files and GIFs go to Bulb Factory,
    /// songs to Music Box, pictures to the screen saver pictures; any other file shows an InfoBar.
    /// </summary>
    /// <param name="paths">The files.</param>
    void AddFiles(IReadOnlyList<string> paths);
}
