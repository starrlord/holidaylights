using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HolidayLights.App.Settings;

/// <summary>A theme card of Home ("Choose a Theme", PRODUCT-SPEC 3.1) or the Themes page (3.6.2).</summary>
public sealed class ThemeCardItem : INotifyPropertyChanged
{
    private bool isSelected;
    private bool isCurrent;
    private bool isRenaming;

    /// <summary>Creates a card.</summary>
    /// <param name="name">The name shown ("Automatic" or the theme's name).</param>
    /// <param name="theme">The theme shown in the preview (Automatic: today's theme), or null.</param>
    /// <param name="isAutomatic">True for the "Automatic" card.</param>
    public ThemeCardItem(string name, ThemeDefinition? theme, bool isAutomatic)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Theme = theme;
        IsAutomatic = isAutomatic;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The name shown.</summary>
    public string Name { get; }

    /// <summary>The theme of the preview, or null (Automatic between holidays without a theme).</summary>
    public ThemeDefinition? Theme { get; }

    /// <summary>True for the "Automatic" card.</summary>
    public bool IsAutomatic { get; }

    /// <summary>The arrangement of the preview.</summary>
    public SlotAssignment? Arrangement => Theme?.Arrangement;

    /// <summary>The theme's own flash pattern for the preview.</summary>
    public FlashOptions? Flash { get; init; }

    /// <summary>The theme's own flash interval for the preview.</summary>
    public int? Interval => Theme?.Flash?.Interval;

    /// <summary>The small line under the name: "Oct 1 - Oct 31", or "Halloween today" on the Automatic card.</summary>
    public string? DateText { get; init; }

    /// <summary>The summary of the Themes page: "Twinkle - 12 songs, Always - Santa".</summary>
    public string? Summary { get; init; }

    /// <summary>"1 bulb missing" (warning badge), or null.</summary>
    public string? MissingText { get; init; }

    /// <summary>The names of the missing bulbs (the badge's tooltip).</summary>
    public string? MissingTip { get; init; }

    /// <summary>True for a shipped theme whose values differ from the original ("Changed").</summary>
    public bool IsChanged { get; init; }

    /// <summary>True when a shipped theme can be restored to its original.</summary>
    public bool CanRestoreOriginal => IsChanged;

    /// <summary>True when the current settings equal the theme ("Current" badge).</summary>
    public bool IsCurrent
    {
        get => isCurrent;
        set => Set(ref isCurrent, value);
    }

    /// <summary>True for Home's selected card (2 DIP accent border + check badge).</summary>
    public bool IsSelected
    {
        get => isSelected;
        set => Set(ref isSelected, value);
    }

    /// <summary>True while the card shows its inline rename box (Themes page, F2).</summary>
    public bool IsRenaming
    {
        get => isRenaming;
        set => Set(ref isRenaming, value);
    }

    /// <summary>True when the card has a date line.</summary>
    public bool HasDates => !string.IsNullOrEmpty(DateText);

    /// <summary>
    /// The screen-reader name: "Halloween theme, Oct 1 - Oct 31, current"; Home's selected card (accent border and check
    /// badge) adds "selected".
    /// </summary>
    public string AccessibleName
    {
        get
        {
            string text = IsAutomatic ? "Automatic themes" : $"{Name} theme";
            if (!string.IsNullOrEmpty(DateText))
            {
                text += ", " + DateText;
            }

            if (!string.IsNullOrEmpty(Summary))
            {
                text += ", " + Summary;
            }

            if (IsSelected)
            {
                text += ", selected";
            }

            if (IsCurrent)
            {
                text += ", current";
            }

            if (IsChanged)
            {
                text += ", changed";
            }

            if (MissingText is not null)
            {
                text += ", " + MissingText;
            }

            return text;
        }
    }

    /// <inheritdoc />
    public override string ToString() => Name;

    private void Set(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessibleName)));
    }
}
